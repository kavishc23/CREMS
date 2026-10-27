using System.Text.RegularExpressions;
using SkiaSharp;
using TesseractOCR;
using TesseractOCR.Enums;

namespace CREMS.Api.Services;

/// <summary>Locate the printed class label before reading its value; exclude Conditions and identity numbers.</summary>
internal static class FijiLicenceClassReader
{
    private sealed record Label(SKRectI Bounds);

    internal static string Read(Engine engine, SKBitmap source)
    {
        using var card = CropCard(source);
        using var normalized = Resize(card, 1600);
        using var channel = InkContrast(normalized, false);
        var labels = new List<Label>();
        engine.SetVariable("tessedit_char_whitelist", "");
        FindLabels(engine, channel, new SKRectI(0, 0, channel.Width, channel.Height), labels, PageSegMode.SparseText);
        var broad = new SKRectI(normalized.Width / 10, normalized.Height / 4, normalized.Width * 4 / 5, normalized.Height * 7 / 10);
        if (labels.Count == 0) FindLabels(engine, channel, broad, labels, PageSegMode.SingleBlock);
        if (labels.Count == 0) FindLabels(engine, normalized, broad, labels, PageSegMode.SingleBlock);
        if (labels.Count == 0)
        {
            using var luminance = ColourPlane(normalized, false);
            FindLabels(engine, luminance, broad, labels, PageSegMode.SingleBlock);
        }
        if (labels.Count == 0)
        {
            var centre = new SKRectI(normalized.Width * 22 / 100, normalized.Height * 35 / 100, normalized.Width * 68 / 100, normalized.Height * 60 / 100);
            FindLabels(engine, normalized, centre, labels, PageSegMode.SingleBlock);
        }
        var votes = new List<string>();
        engine.SetVariable("tessedit_char_whitelist", "123456789, ");
        try
        {
            foreach (var label in labels.DistinctBy(x => (x.Bounds.Left / 20, x.Bounds.Top / 20)).Take(3))
            {
                var bounds = label.Bounds;
                var region = new SKRectI(Math.Max(0, bounds.Left - bounds.Width / 12),
                    Math.Min(normalized.Height - 1, bounds.Bottom + Math.Max(2, bounds.Height / 8)),
                    Math.Min(normalized.Width, bounds.Right + bounds.Width / 12),
                    Math.Min(normalized.Height, bounds.Bottom + bounds.Height * 21 / 10));
                if (region.Width < 10 || region.Height < 5) continue;
                using var crop = new SKBitmap(region.Width, region.Height);
                if (!normalized.ExtractSubset(crop, region)) continue;
                using var enlarged = Resize(crop, 1200);
                foreach (var variant in new[] { 0, 1, 2, 3, 4 })
                {
                    using var plane = variant < 2 ? enlarged.Copy() : ColourPlane(enlarged, variant == 4);
                    using var contrast = variant is 2 ? plane.Copy() : InkContrast(plane, variant != 0);
                    using var padded = Pad(contrast);
                    using var image = Image(padded);
                    foreach (var mode in new[] { PageSegMode.SingleLine, PageSegMode.RawLine, PageSegMode.SingleWord })
                    {
                        using var page = engine.Process(image, mode);
                        var value = NormalizeValue(page.Text);
                        // The wrapper reports confidence on a 0..1 scale.
                        if (value is not null && page.MeanConfidence >= .35f) votes.Add(value);
                    }
                }
            }
        }
        finally { engine.SetVariable("tessedit_char_whitelist", ""); }
        // Require repeated spatially anchored readings. A single noisy mark is not a licence class.
        return Consensus(votes);
    }

    internal static string Consensus(IEnumerable<string> values)
    {
        var candidates = values.GroupBy(x => x).Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key.Length).ToArray();
        if (candidates.Length == 0) return string.Empty;
        var best = candidates[0];
        // Equal support for contradictory classes needs another scan, not an arbitrary tie-break.
        if (candidates.Skip(1).Any(other => other.Count() == best.Count() && other.Key.Any(c => c != ',' && !best.Key.Contains(c))))
            return string.Empty;
        return best.Key;
    }

    internal static string? NormalizeValue(string value)
    {
        var clean = Regex.Replace(value.Trim(), @"\s+", "").Trim(',');
        // Tesseract may omit a tiny comma: '23' is two class glyphs in this value-only crop.
        if (!Regex.IsMatch(clean, @"^[1-9](?:,?[1-9]){0,8}$")) return null;
        var classes = clean.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (classes.Distinct().Count() != classes.Length) return null;
        return string.Join(',', classes.Order());
    }

    private static void FindLabels(Engine engine, SKBitmap image, SKRectI region, List<Label> labels, PageSegMode mode)
    {
        using var cropped = new SKBitmap(region.Width, region.Height);
        if (!image.ExtractSubset(cropped, region)) return;
        using var pix = Image(cropped);
        using var page = engine.Process(pix, mode);
        foreach (var block in page.Layout)
        foreach (var paragraph in block.Paragraphs)
        foreach (var line in paragraph.TextLines)
        {
            var text = Regex.Replace((line.Text ?? string.Empty).ToLowerInvariant(), @"[^a-z]", "");
            if (!((text.Contains("licen") && (text.Contains("class") || text.Contains("closs"))) || (text.Contains("full") && text.Contains("lic")) || text.EndsWith("classes") || text.EndsWith("closses")) || line.BoundingBox is not { } box) continue;
            var bounds = new SKRectI((text is "classes" or "class" or "closses" ? Math.Max(0, box.X1 - box.Width * 2) : box.X1) + region.Left, box.Y1 + region.Top, box.X2 + region.Left, box.Y2 + region.Top);
            if (bounds.Top > image.Height / 5 && bounds.Bottom < image.Height * 3 / 4 && bounds.Left < image.Width * 4 / 5)
                labels.Add(new Label(bounds));
        }
    }

    internal static SKBitmap CropCard(SKBitmap source)
    {
        var stride = Math.Max(1, Math.Max(source.Width, source.Height) / 700);
        var rows = new int[source.Height]; var columns = new int[source.Width];
        for (var y = 0; y < source.Height; y += stride)
        for (var x = 0; x < source.Width; x += stride)
        {
            var c = source.GetPixel(x, y);
            if (c.Blue > 70 && c.Blue > c.Red * 1.18 && c.Blue > c.Green * .85)
            { rows[y]++; columns[x]++; }
        }
        var ys = Enumerable.Range(0, rows.Length).Where(y => rows[y] > source.Width / stride / 8).ToArray();
        var xs = Enumerable.Range(0, columns.Length).Where(x => columns[x] > source.Height / stride / 8).ToArray();
        if (xs.Length < 2 || ys.Length < 2) return source.Copy();
        var pad = Math.Max(source.Width, source.Height) / 100;
        var region = new SKRectI(Math.Max(0, xs[0] - pad), Math.Max(0, ys[0] - pad),
            Math.Min(source.Width, xs[^1] + pad), Math.Min(source.Height, ys[^1] + pad));
        if (region.Width < source.Width / 3 || region.Height < source.Height / 4) return source.Copy();
        var card = new SKBitmap(region.Width, region.Height);
        if (source.ExtractSubset(card, region)) return card;
        card.Dispose(); return source.Copy();
    }

    private static SKBitmap Resize(SKBitmap source, int width)
    {
        var scale = width / (float)source.Width;
        var result = new SKBitmap(width, Math.Max(1, (int)(source.Height * scale)));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(source, new SKRect(0, 0, result.Width, result.Height), new SKSamplingOptions(SKCubicResampler.Mitchell), null);
        return result;
    }

    private static SKBitmap InkContrast(SKBitmap source, bool binary)
    {
        var pixels = source.Pixels;
        var histogram = new int[256];
        foreach (var c in pixels) histogram[Math.Max(c.Blue, Math.Max(c.Red, c.Green))]++;
        var sum = histogram.Select((n, value) => (double)n * value).Sum();
        double sumBackground = 0, best = -1; var background = 0; var threshold = 125;
        for (var value = 0; value < 255; value++)
        {
            background += histogram[value]; if (background == 0) continue;
            var foreground = pixels.Length - background; if (foreground == 0) break;
            sumBackground += value * histogram[value];
            var difference = sumBackground / background - (sum - sumBackground) / foreground;
            var score = (double)background * foreground * difference * difference;
            if (score > best) { best = score; threshold = value; }
        }
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            // Preserve the bright blue card background instead of darkening it like luminance does.
            var light = Math.Max(c.Blue, Math.Max(c.Red, c.Green));
            var value = binary ? (byte)(light <= threshold ? 0 : 255) : (byte)Math.Clamp((light - 95) * 2.8, 0, 255);
            pixels[i] = new SKColor(value, value, value);
        }
        return new SKBitmap(source.Width, source.Height) { Pixels = pixels };
    }

    private static SKBitmap ColourPlane(SKBitmap source, bool red)
    {
        var pixels = source.Pixels; var values = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++) { var c = pixels[i]; values[i] = red ? c.Red : (byte)(.299 * c.Red + .587 * c.Green + .114 * c.Blue); }
        var low = values.Min(); var high = values.Max();
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = (byte)((values[i] - low) * 255 / Math.Max(1, high - low));
            pixels[i] = new SKColor(value, value, value);
        }
        return new SKBitmap(source.Width, source.Height) { Pixels = pixels };
    }

    private static SKBitmap Pad(SKBitmap source)
    {
        var result = new SKBitmap(source.Width + 60, source.Height + 60);
        using var canvas = new SKCanvas(result); canvas.Clear(SKColors.White); canvas.DrawBitmap(source, 30, 30, new SKSamplingOptions(SKFilterMode.Linear), null);
        return result;
    }

    private static TesseractOCR.Pix.Image Image(SKBitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
        return TesseractOCR.Pix.Image.LoadFromMemory(stream.ToArray());
    }
}
