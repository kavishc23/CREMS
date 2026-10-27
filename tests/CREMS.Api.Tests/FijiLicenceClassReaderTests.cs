using System.Reflection;
using CREMS.Api.Services;
using SkiaSharp;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class FijiLicenceClassReaderTests
{
    private static readonly Type Reader = typeof(HttpLicenceOcrProvider).Assembly.GetType("CREMS.Api.Services.FijiLicenceClassReader")!;

    [Theory]
    [InlineData("2", "2")]
    [InlineData("2,3", "2,3")]
    [InlineData("2, 3\n", "2,3")]
    [InlineData("23", "2,3")]
    [InlineData("2 3", "2,3")]
    [InlineData("3,2", "2,3")]
    [InlineData("0", null)]
    [InlineData("10", null)]
    [InlineData("2029", null)]
    [InlineData("22", null)]
    [InlineData("Conditions 7", null)]
    [InlineData("", null)]
    public void Reads_only_valid_class_glyphs_in_the_isolated_value_strip(string text, string? expected)
    {
        Assert.Equal(expected, Reader.GetMethod("NormalizeValue", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [text]));
    }

    [Theory]
    [InlineData(new[] { "2" }, "")]
    [InlineData(new[] { "2", "2", "7", "7" }, "")]
    [InlineData(new[] { "2", "2", "2,3", "2,3" }, "2,3")]
    [InlineData(new[] { "2,3", "2,3", "3" }, "2,3")]
    public void Requires_corroboration_and_rejects_conflicting_class_reads(string[] readings, string expected)
    {
        Assert.Equal(expected, Reader.GetMethod("Consensus", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [readings]));
    }

    [Fact]
    public void Card_location_excludes_a_desk_without_modifying_the_source_image()
    {
        using var source = new SKBitmap(1400, 1300);
        source.Erase(new SKColor(185, 140, 90));
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = new SKColor(25, 145, 220) })
            canvas.DrawRect(new SKRect(100, 350, 1300, 1100), paint);
        using var card = (SKBitmap)Reader.GetMethod("CropCard", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [source])!;
        Assert.InRange(card.Width, 1200, 1240);
        Assert.InRange(card.Height, 750, 790);
        Assert.Equal(1400, source.Width);
        Assert.Equal(new SKColor(185, 140, 90), source.GetPixel(0, 0));
    }

    [Fact]
    public void Does_not_crop_documents_without_a_recognizable_blue_card()
    {
        using var source = new SKBitmap(800, 1100);
        source.Erase(SKColors.White);
        using var card = (SKBitmap)Reader.GetMethod("CropCard", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [source])!;
        Assert.Equal(source.Width, card.Width);
        Assert.Equal(source.Height, card.Height);
    }

    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData("2,3", new[] { 2, 3 })]
    public void Label_anchored_consensus_preserves_all_classes_over_a_clipped_fallback(string value, int[] expected)
    {
        var result = CustomerLicenceService.Parse($"Name: SAMPLE CUSTOMER\nLicence No: DL-448821\nConditions 7\n[[CREMS_CLASS_VALUE_BEGIN]]\n3\n[[CREMS_CLASS_VALUE_END]]\n[[CREMS_LABEL_VALUE_BEGIN]]\n{value}\n[[CREMS_LABEL_VALUE_END]]");
        Assert.Equal(expected, result.Classes);
        Assert.Equal("DL-448821", result.LicenceNumber);
        Assert.Equal("SAMPLE CUSTOMER", result.FullName);
    }
}
