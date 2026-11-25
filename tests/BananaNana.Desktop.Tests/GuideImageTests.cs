using System.Text;
using BananaNana.Desktop;
using SkiaSharp;
using Xunit;

namespace BananaNana.Desktop.Tests;

// Guide maker pictures: scaled to at most 1600 px wide, PNG or JPEG, and the texts it needs exist in every language.
public class GuideImageTests
{
    static byte[] Png(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKBitmap Decode(byte[] data) => SKBitmap.Decode(data) ?? throw new InvalidDataException("not decodable");

    [Fact]
    public void A_wide_picture_is_scaled_to_1600_px_keeping_the_aspect_ratio()
    {
        var (data, type) = GuideImage.Prepare(Png(3200, 1600, SKColors.Teal));

        using var result = Decode(data);
        Assert.Equal("png", type);
        Assert.Equal(1600, result.Width);
        Assert.Equal(800, result.Height);
    }

    [Fact]
    public void A_picture_no_wider_than_1600_px_is_kept_unchanged()
    {
        var source = Png(300, 200, SKColors.Orange);

        var (data, type) = GuideImage.Prepare(source);

        Assert.Equal("png", type);
        Assert.Equal(source, data);
    }

    [Fact]
    public void A_picture_exactly_1600_px_wide_is_not_scaled()
    {
        var source = Png(1600, 900, SKColors.Gray);

        var (data, _) = GuideImage.Prepare(source);

        Assert.Equal(source, data);
    }

    [Fact]
    public void A_large_noisy_picture_is_saved_as_jpeg()
    {
        // Random pixels do not compress: the 2000 px picture is scaled to 1600 px and its PNG stays above 1.5 MB, so JPEG is used.
        using var bitmap = new SKBitmap(2000, 2000);
        var noise = new byte[2000 * 2000 * 4];
        Random.Shared.NextBytes(noise);
        System.Runtime.InteropServices.Marshal.Copy(noise, 0, bitmap.GetPixels(), noise.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var (data, type) = GuideImage.Prepare(encoded.ToArray());

        Assert.Equal("jpeg", type);
        using var result = Decode(data);
        Assert.Equal(1600, result.Width);
    }

    [Fact]
    public void A_jpeg_source_that_needs_no_scaling_keeps_its_bytes()
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(SKColors.Navy);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var source = encoded.ToArray();

        var (data, type) = GuideImage.Prepare(source);

        Assert.Equal("jpeg", type);
        Assert.Equal(source, data);
    }

    [Fact]
    public void Text_that_is_not_a_picture_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => GuideImage.Prepare(Encoding.UTF8.GetBytes("not a picture")));
    }

    [Fact]
    public void Every_guide_text_exists_in_every_language()
    {
        string[] keys = ["Guide.Title", "Guide.New", "Guide.Open", "Guide.Delete", "Guide.Saved", "Guide.SaveFailed", "Guide.DropHint", "Guide.AddStep", "Guide.Choose"];
        foreach (var (code, _) in Loc.Languages)
        {
            var strings = Loc.Load(code);
            foreach (var key in keys) Assert.Contains(key, strings.Keys);
        }
    }
}
