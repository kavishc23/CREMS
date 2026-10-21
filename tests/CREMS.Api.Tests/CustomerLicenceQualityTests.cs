using System.Reflection;
using CREMS.Api.Services;
using SkiaSharp;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class CustomerLicenceQualityTests
{
    [Fact]
    public void Rejects_a_blurred_image_before_ocr()
    {
        using var bitmap = new SKBitmap(800, 500);
        bitmap.Erase(new SKColor(120, 170, 220));
        using var stream = new MemoryStream();
        Assert.True(bitmap.Encode(stream, SKEncodedImageFormat.Png, 100));
        var preflight = typeof(CustomerLicenceService).GetMethod("EnsureImageIsSharpEnough", BindingFlags.NonPublic | BindingFlags.Static)!;

        var failure = Assert.Throws<TargetInvocationException>(() => preflight.Invoke(null, [stream.ToArray(), "image/png"]));
        Assert.Equal("blurry_licence", Assert.IsType<LicenceException>(failure.InnerException).Code);
    }
}
