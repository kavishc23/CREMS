namespace CREMS.Api.Services;

public static class IncidentEvidence
{
    public static string? Validate(IReadOnlyList<string>? files)
    {
        if (files is null) return null;
        if (files.Count > 5) return "Attach up to 5 files.";
        foreach (var file in files)
        {
            if (file is null || file.Length > 2_800_000) return "Each attachment must be at most 2 MB.";
            var prefix = new[] { "data:image/jpeg;base64,", "data:image/png;base64,", "data:image/webp;base64,", "data:application/pdf;base64," }.FirstOrDefault(p => file.StartsWith(p, StringComparison.Ordinal));
            if (prefix is null) return "Use JPG, PNG, WebP or PDF attachments.";
            try {
                var bytes = Convert.FromBase64String(file[prefix.Length..]);
                if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return "Each attachment must be at most 2 MB.";
                var valid = prefix.Contains("pdf") ? bytes.AsSpan().StartsWith("%PDF-"u8)
                    : prefix.Contains("png") ? bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})
                    : prefix.Contains("jpeg") ? bytes.Length > 2 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
                    : bytes.Length >= 12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8);
                if (!valid) return "An attachment does not match its file type.";
            } catch (FormatException) { return "An attachment could not be read."; }
        }
        return null;
    }
}
