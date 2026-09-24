using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public sealed record LicenceOcrResult(string FullName, string LicenceNumber, IReadOnlyList<int> Classes);
public sealed record StagedLicence(Guid SubmissionId, string FullName, string LicenceNumber, IReadOnlyList<int> Classes, bool NameMatches);

public interface ILicenceOcrProvider
{
    Task<string> ReadFirstPageAsync(byte[] content, string contentType, CancellationToken token);
}

public sealed class HttpLicenceOcrProvider(IHttpClientFactory clients, IConfiguration configuration) : ILicenceOcrProvider
{
    public async Task<string> ReadFirstPageAsync(byte[] content, string contentType, CancellationToken token)
    {
        var endpoint = configuration["LicenceOcr:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return await Task.Run(() => ReadLocally(content, contentType), token);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var apiKey = configuration["LicenceOcr:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", contentType == "application/pdf" ? "licence.pdf" : "licence-image");
        form.Add(new StringContent("1"), "page");
        form.Add(new StringContent("text"), "output");
        request.Content = form;
        using var response = await clients.CreateClient("LicenceOcr").SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new LicenceException("ocr_service_failed", "The licence could not be scanned. Try another image or enter the details manually.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("text", out var text) || string.IsNullOrWhiteSpace(text.GetString()))
            throw new LicenceException("ocr_no_text", "No readable licence details were found. Try a clearer image.");
        return text.GetString()!;
    }

    private static string ReadLocally(byte[] content, string contentType)
    {
        try
        {
            if (contentType == "application/pdf")
            {
                using var pdf = new MemoryStream(content);
#pragma warning disable CA1416 // PDFtoImage supports every runtime targeted by this web API.
                using var bitmap = PDFtoImage.Conversion.ToImage(pdf, page: 0, options: new PDFtoImage.RenderOptions(Dpi: 300));
#pragma warning restore CA1416
                using var firstPage = new MemoryStream();
                if (!bitmap.Encode(firstPage, SkiaSharp.SKEncodedImageFormat.Png, 100))
                    throw new LicenceException("unreadable_pdf", "The first PDF page could not be converted to an image.");
                content = firstPage.ToArray();
            }
            var dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            using var engine = new TesseractOCR.Engine(dataPath, TesseractOCR.Enums.Language.English, TesseractOCR.Enums.EngineMode.Default);
            var readings = new List<string>();
            var centreClassReadings = new List<string>();
            using (var image = TesseractOCR.Pix.Image.LoadFromMemory(content))
            using (var page = engine.Process(image)) readings.Add(page.Text);
            using var source = SkiaSharp.SKBitmap.Decode(content);
            if (source is not null)
            {
                // One enhanced full-card pass plus three broad semantic regions is both
                // faster and more reliable than repeatedly thresholding the whole image.
                // The regions are intentionally broad so rotated/cropped licence variants
                // are still handled without hard-coding one exact card template.
                using var enlarged = Resize(source, Math.Max(source.Width, 1600));
                using var enhanced = Grayscale(enlarged, 1.55f);
                readings.Add(Read(engine, enhanced));
                var regions = new[]
                {
                    new SkiaSharp.SKRectI(0, enlarged.Height / 12, enlarged.Width * 3 / 4, enlarged.Height / 2),
                    new SkiaSharp.SKRectI(enlarged.Width / 2, enlarged.Height / 12, enlarged.Width, enlarged.Height * 3 / 5),
                    new SkiaSharp.SKRectI(enlarged.Width / 5, enlarged.Height / 5, enlarged.Width * 4 / 5, enlarged.Height * 3 / 4),
                };
                foreach (var region in regions)
                {
                    using var cropped = new SkiaSharp.SKBitmap(region.Width, region.Height);
                    if (!enhanced.ExtractSubset(cropped, region)) continue;
                    readings.Add(Read(engine, cropped));
                }
                var classRegion = new SkiaSharp.SKRectI(enlarged.Width / 4, enlarged.Height / 4,
                    enlarged.Width * 2 / 3, enlarged.Height * 3 / 5);
                using var classCrop = new SkiaSharp.SKBitmap(classRegion.Width, classRegion.Height);
                if (enhanced.ExtractSubset(classCrop, classRegion))
                {
                    using var classEnlarged = Resize(classCrop, Math.Max(classCrop.Width, 1600));
                    using var strongerClassContrast = Grayscale(classEnlarged, 1.35f);
                    centreClassReadings.Add(Read(engine, strongerClassContrast, TesseractOCR.Enums.PageSegMode.SingleBlock));
                }
            }
            // Keep OCR passes separated. Without a non-whitespace boundary, a class
            // label at the end of one pass can be paired with the Conditions digit
            // from the beginning of another pass.
            const string passBreak = "\n[[CREMS_PASS_BREAK]]\n";
            var text = string.Join(passBreak, readings.Where(value => !string.IsNullOrWhiteSpace(value)));
            var centreText = string.Join(passBreak, centreClassReadings.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(centreText))
                text += $"\n[[CREMS_CENTRE_BEGIN]]\n{centreText}\n[[CREMS_CENTRE_END]]";
            if (string.IsNullOrWhiteSpace(text))
                throw new LicenceException("ocr_no_text", "No readable licence details were found. Try a clearer image.");
            return text;
        }
        catch (LicenceException) { throw; }
        catch
        {
            throw new LicenceException("ocr_service_failed", "The licence could not be scanned. Try another image or enter the details manually.");
        }
    }

    private static string Read(TesseractOCR.Engine engine, SkiaSharp.SKBitmap bitmap, TesseractOCR.Enums.PageSegMode? pageSegMode = null)
    {
        using var stream = new MemoryStream();
        bitmap.Encode(stream, SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var image = TesseractOCR.Pix.Image.LoadFromMemory(stream.ToArray());
        using var page = engine.Process(image, pageSegMode);
        return page.Text;
    }

    private static SkiaSharp.SKBitmap Resize(SkiaSharp.SKBitmap source, int targetWidth)
    {
        var scale = Math.Max(1f, targetWidth / (float)source.Width);
        var result = new SkiaSharp.SKBitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        using var canvas = new SkiaSharp.SKCanvas(result);
        canvas.DrawBitmap(source, new SkiaSharp.SKRect(0, 0, result.Width, result.Height),
            new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell), null);
        return result;
    }

    private static SkiaSharp.SKBitmap Grayscale(SkiaSharp.SKBitmap source, float contrast)
    {
        var result = new SkiaSharp.SKBitmap(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var color = source.GetPixel(x, y);
            var luminance = .299f * color.Red + .587f * color.Green + .114f * color.Blue;
            var adjusted = (byte)Math.Clamp((luminance - 128f) * contrast + 128f, 0, 255);
            result.SetPixel(x, y, new SkiaSharp.SKColor(adjusted, adjusted, adjusted));
        }
        return result;
    }

}

public sealed class CustomerLicenceService(ApplicationDbContext db, ILicenceOcrProvider ocr, IWebHostEnvironment environment)
{
    public const long MaximumFileSize = 5 * 1024 * 1024;
    private static readonly Regex NumberLine = new(@"(?im)licen[cs]e\s*(?:no|number)\s*[:#-]?\s*([A-Z0-9-]{4,30})", RegexOptions.Compiled);
    private static readonly Regex DlNumber = new(@"(?im)\b(DL[-\s:]?[A-Z0-9-]{4,30})", RegexOptions.Compiled);
    private static readonly Regex NumericNumber = new(@"(?<![\d/])\b(\d{6,10})\b(?![/\d])", RegexOptions.Compiled);
    private static readonly Regex NameLine = new(@"(?im)(?:name|holder)\s*[:#-]?\s*([A-Z][A-Z .'’-]{2,100})", RegexOptions.Compiled);
    private static readonly Regex TitledNameLine = new(@"^\s*(?:MR|MRS|MS|MISS|DR)[.,:]?\s+([A-Z][A-Z .'’,-]{4,100})\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ClassesLine = new(
        @"(?im)(?:full\s+licen[cs]e\s+class(?:es)?|licen[cs]e\s+class(?:es)?|class(?:es)?|categories)\s*[:#-]?\s*((?<!\d)[1-9](?:[\t ,;/]+[1-9])*)(?!\d)",
        RegexOptions.Compiled);
    private static readonly Regex CentreClassRegion = new(
        @"\[\[CREMS_CENTRE_BEGIN\]\](.*?)\[\[CREMS_CENTRE_END\]\]",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex StandaloneCentreClass = new(@"(?m)^\s*([1-9](?:[\t ,;/]+[1-9])*)\s*$", RegexOptions.Compiled);

    public async Task<StagedLicence> ScanAsync(Guid customerId, IFormFile file, LicenceUploadSource source, Guid? actorId, CancellationToken token)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, token) ?? throw new LicenceException("customer_not_found", "Customer account was not found.");
        var (bytes, contentType) = await ReadAndValidateAsync(file, token);
        var submission = await StoreAsync(customerId, bytes, contentType, source, actorId, token);
        try
        {
            var text = await ocr.ReadFirstPageAsync(bytes, contentType, token);
            // Name and number are required to continue. If a small class digit cannot be read
            // confidently, the confirmation checklist remains empty for safe user correction.
            var result = Parse(text, allowMissingClasses: true, expectedName: customer.Name);
            submission.ExtractedName = result.FullName;
            submission.LicenceNumber = result.LicenceNumber;
            submission.LicenceClasses = string.Join(',', result.Classes);
            submission.OcrStatus = LicenceOcrStatus.Completed;
            submission.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            return new(submission.Id, result.FullName, result.LicenceNumber, result.Classes, NamesMatch(customer.Name, result.FullName));
        }
        catch (LicenceException exception)
        {
            submission.OcrStatus = LicenceOcrStatus.Failed;
            submission.OcrFailureCode = exception.Code;
            submission.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            throw;
        }
        catch
        {
            submission.OcrStatus = LicenceOcrStatus.Failed;
            submission.OcrFailureCode = "ocr_service_failed";
            submission.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            throw new LicenceException("ocr_service_failed", "The licence could not be scanned. Try another image or enter the details manually.");
        }
    }

    public async Task<CustomerLicence> SaveManualAsync(Guid customerId, IFormFile file, string licenceNumber, IReadOnlyList<int> classes, LicenceUploadSource source, Guid? actorId, CancellationToken token)
    {
        var (bytes, contentType) = await ReadAndValidateAsync(file, token);
        var submission = await StoreAsync(customerId, bytes, contentType, source, actorId, token);
        return await ConfirmAsync(customerId, submission.Id, licenceNumber, classes, actorId, requireNameMatch: false, token);
    }

    public async Task<CustomerLicence> ConfirmAsync(Guid customerId, Guid submissionId, string licenceNumber, IReadOnlyList<int> classes, Guid? actorId, bool requireNameMatch, CancellationToken token)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, token) ?? throw new LicenceException("customer_not_found", "Customer account was not found.");
        var submission = await db.CustomerLicences.FirstOrDefaultAsync(x => x.Id == submissionId && x.CustomerId == customerId, token) ?? throw new LicenceException("submission_not_found", "Choose the licence file again.");
        var cleanNumber = licenceNumber.Trim();
        var cleanClasses = ValidateClasses(classes);
        if (string.IsNullOrWhiteSpace(cleanNumber)) throw new LicenceException("licence_number_required", "Enter the licence number.");
        if (requireNameMatch && (submission.OcrStatus != LicenceOcrStatus.Completed || !NamesMatch(customer.Name, submission.ExtractedName ?? string.Empty)))
            throw new LicenceException("name_mismatch", "The licence name does not match the customer name recorded in CREMS.");
        var previous = await db.CustomerLicences.Where(x => x.CustomerId == customerId && x.Status == LicenceVerificationStatus.Verified && x.Id != submission.Id).ToListAsync(token);
        foreach (var item in previous) { item.Status = LicenceVerificationStatus.Required; item.UpdatedAt = DateTimeOffset.UtcNow; }
        submission.LicenceNumber = cleanNumber;
        submission.LicenceClasses = string.Join(',', cleanClasses);
        submission.Status = LicenceVerificationStatus.Verified;
        submission.ConfirmedAt = DateTimeOffset.UtcNow;
        submission.ConfirmedByUserId = actorId;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        customer.IdentificationNumber = cleanNumber;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return submission;
    }

    public static LicenceOcrResult Parse(string text, bool allowMissingClasses = false, string? expectedName = null)
    {
        static string CleanName(string value) => Regex.Replace(value,
            @"(?i)\s+(?:lic\w*|date\s+of\s+birth|full\s+licen[cs]e\s+classes).*$", string.Empty).Trim();
        var names = NameLine.Matches(text).Select(match => CleanName(match.Groups[1].Value));
        var titledNames = TitledNameLine.Matches(text).Select(match => CleanName(match.Groups[1].Value));
        var name = names.Concat(titledNames).Select(value => value.Trim(' ', ',', '.', '-', '|')).Where(value => value.Length >= 3)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
            .Select(group => group.Key).FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedName) && (string.IsNullOrWhiteSpace(name) || !NamesMatch(expectedName, name)) && OcrSupportsExpectedName(text, expectedName))
            name = expectedName.Trim();
        var labelledNumbers = NumberLine.Matches(text).Select(match => match.Groups[1].Value.Trim().ToUpperInvariant());
        var dlNumbers = DlNumber.Matches(text).Select(match => Regex.Replace(match.Groups[1].Value.Trim().ToUpperInvariant(), @"^DL[\s:]", "DL-"));
        var numericNumbers = NumericNumber.Matches(text).Select(match => match.Groups[1].Value);
        var number = labelledNumbers.Concat(dlNumbers).Concat(numericNumbers)
            .Where(value => value.Length is >= 4 and <= 30 && value.Count(char.IsDigit) >= 3 &&
                !value.Equals("NUMBER", StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key.Count(char.IsDigit))
            .Select(group => group.Key).FirstOrDefault() ?? string.Empty;
        // Local OCR marks a centre-only crop which excludes the bottom Conditions
        // field. Prefer that spatially trusted region and allow a standalone value
        // there because a blurred label may disappear while its class digit survives.
        var centreText = CentreClassRegion.Match(text).Groups[1].Value;
        var classSource = string.IsNullOrWhiteSpace(centreText) ? text : centreText;
        var labelledClasses = ClassesLine.Matches(classSource)
            .SelectMany(match => Regex.Matches(match.Groups[1].Value, "[1-9]")
                .Select(value => int.Parse(value.Value)))
            .ToArray();
        var centreStandaloneClasses = string.IsNullOrWhiteSpace(centreText) || labelledClasses.Length > 0
            ? []
            : StandaloneCentreClass.Matches(centreText)
                .SelectMany(match => Regex.Matches(match.Groups[1].Value, "[1-9]")
                    .Select(value => int.Parse(value.Value)))
                .ToArray();
        var classes = labelledClasses.Concat(centreStandaloneClasses)
            .Distinct().Order().ToArray();
        if (string.IsNullOrWhiteSpace(name)) throw new LicenceException("ocr_name_missing", "The full name could not be read. Try a clearer image.");
        if (string.IsNullOrWhiteSpace(number)) throw new LicenceException("ocr_number_missing", "The licence number could not be read. Enter it manually or try another image.");
        if (classes.Length == 0 && !allowMissingClasses) throw new LicenceException("ocr_classes_missing", "No licence classes from 1 through 9 could be read. Select them before confirming.");
        return new(name, number, classes);
    }

    private static bool OcrSupportsExpectedName(string text, string expectedName)
    {
        static string[] Words(string value) => Regex.Matches(value.ToUpperInvariant(), "[A-Z]{2,}")
            .Select(match => match.Value).Where(value => value is not "MR" and not "MRS" and not "MS" and not "MISS" and not "DR").ToArray();
        var expected = Words(expectedName);
        var observed = Words(text);
        if (expected.Length < 2) return false;
        var matched = expected.Count(word => observed.Any(candidate =>
            Math.Abs(candidate.Length - word.Length) <= Math.Max(2, word.Length / 2) &&
            EditDistance(candidate, word) <= Math.Max(1, word.Length / 2)));
        return matched >= Math.Max(2, (int)Math.Ceiling(expected.Length * .6));
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1]; current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }

    public static bool NamesMatch(string expected, string extracted)
    {
        static string Normalize(string value) => value.Normalize(NormalizationForm.FormD).ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => Regex.Replace(part, "[^A-Z]", string.Empty))
            .Where(part => part.Length > 0).Order().Aggregate(string.Empty, (all, part) => all + part);
        var left = Normalize(expected); var right = Normalize(extracted);
        return left.Length >= 3 && (left == right || left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal));
    }

    public async Task<CustomerLicence?> CurrentAsync(Guid customerId, CancellationToken token) =>
        await db.CustomerLicences.AsNoTracking().Where(x => x.CustomerId == customerId && x.Status == LicenceVerificationStatus.Verified).OrderByDescending(x => x.ConfirmedAt).FirstOrDefaultAsync(token);

    public async Task RemoveAsync(Guid customerId, CancellationToken token)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, token)
            ?? throw new LicenceException("customer_not_found", "Customer account was not found.");
        var records = await db.CustomerLicences.Where(x => x.CustomerId == customerId).ToListAsync(token);
        var storedPaths = records.Select(item =>
        {
            try { return ResolvePath(item); }
            catch (FileNotFoundException) { return null; }
        }).Where(path => path is not null).Cast<string>().ToArray();

        db.CustomerLicences.RemoveRange(records);
        customer.IdentificationNumber = null;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        foreach (var path in storedPaths)
        {
            try { File.Delete(path); }
            catch (IOException) { /* Database removal remains authoritative. */ }
            catch (UnauthorizedAccessException) { /* Do not expose storage paths. */ }
        }
    }

    public string ResolvePath(CustomerLicence licence)
    {
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "customer-licences"));
        var path = Path.GetFullPath(Path.Combine(root, licence.StorageKey));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path)) throw new FileNotFoundException();
        return path;
    }

    private async Task<CustomerLicence> StoreAsync(Guid customerId, byte[] bytes, string contentType, LicenceUploadSource source, Guid? actorId, CancellationToken token)
    {
        var extension = contentType switch { "application/pdf" => ".pdf", "image/png" => ".png", _ => ".jpg" };
        var relative = Path.Combine(customerId.ToString("N"), $"{Guid.NewGuid():N}{extension}");
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "customer-licences", customerId.ToString("N"));
        Directory.CreateDirectory(root);
        try { await File.WriteAllBytesAsync(Path.Combine(root, Path.GetFileName(relative)), bytes, token); }
        catch { throw new LicenceException("storage_failed", "The licence could not be stored securely. Please try again."); }
        var record = new CustomerLicence { CustomerId = customerId, StorageKey = relative, ContentType = contentType, SizeBytes = bytes.LongLength, ContentHash = Convert.ToHexString(SHA256.HashData(bytes)), UploadSource = source, UploadedByUserId = actorId };
        db.CustomerLicences.Add(record); await db.SaveChangesAsync(token); return record;
    }

    private static async Task<(byte[] Bytes, string ContentType)> ReadAndValidateAsync(IFormFile file, CancellationToken token)
    {
        if (file.Length is <= 0 or > MaximumFileSize) throw new LicenceException("file_size", "Choose a PDF, JPEG or PNG file no larger than 5 MB.");
        await using var stream = new MemoryStream(); await file.CopyToAsync(stream, token); var bytes = stream.ToArray();
        var contentType = DetectContentType(bytes);
        if (contentType is null) throw new LicenceException("unsupported_file", "Only readable PDF, JPEG and PNG files are accepted.");
        ValidateReadable(bytes, contentType);
        return (bytes, contentType);
    }

    private static string? DetectContentType(byte[] bytes)
    {
        if (bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length > 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-") return "application/pdf";
        return null;
    }

    private static void ValidateReadable(byte[] bytes, string contentType)
    {
        if (contentType == "image/png" && (bytes.Length < 24 || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) <= 0 || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) <= 0))
            throw new LicenceException("unreadable_image", "The image could not be read. Choose a clear, undamaged image.");
        if (contentType == "image/jpeg" && !bytes.AsSpan(Math.Max(0, bytes.Length - 2)).SequenceEqual(new byte[] { 0xFF, 0xD9 }))
            throw new LicenceException("unreadable_image", "The image could not be read. Choose a clear, undamaged image.");
        if (contentType == "application/pdf" && !Encoding.ASCII.GetString(bytes).Contains("%%EOF", StringComparison.Ordinal))
            throw new LicenceException("unreadable_pdf", "The PDF could not be read. Choose a valid PDF document.");
    }

    private static int[] ValidateClasses(IReadOnlyList<int> classes)
    {
        var values = classes.Distinct().Order().ToArray();
        if (values.Length == 0) throw new LicenceException("licence_classes_required", "Select at least one licence class.");
        if (values.Any(x => x is < 1 or > 9)) throw new LicenceException("invalid_licence_class", "Licence classes must be numbers from 1 through 9.");
        return values;
    }
}

public sealed class LicenceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
