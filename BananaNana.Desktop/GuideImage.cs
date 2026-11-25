using SkiaSharp;
namespace BananaNana.Desktop;

// Pictures for the Guide maker: anything wider than 1600 px is scaled down (aspect ratio kept). The result is PNG,
// or JPEG at quality 90 when that PNG is larger than 1.5 MB. A PNG or JPEG that needs no scaling is kept as it is.
public static class GuideImage
{
    public const int MaxWidth = 1600;
    public const int LargePngBytes = 1_500_000;
    public const int JpegQuality = 90;

    // Returns the bytes and their type ("png" or "jpeg"). Throws InvalidDataException when the data is not a picture.
    public static (byte[] Data, string ImageType) Prepare(byte[] source)
    {
        using var bitmap = Decode(source);
        var sourceType = Detect(source);
        if (bitmap.Width <= MaxWidth && sourceType is not null) return (source, sourceType);

        int height = Math.Max(1, (int)Math.Round(bitmap.Height * (double)Math.Min(bitmap.Width, MaxWidth) / bitmap.Width));
        int width = Math.Min(bitmap.Width, MaxWidth);
        using var scaled = bitmap.Width > MaxWidth
            ? bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.High)
                ?? throw new InvalidDataException("The picture could not be scaled.")
            : bitmap.Copy();
        using var image = SKImage.FromBitmap(scaled);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        if (png.Size <= LargePngBytes) return (png.ToArray(), "png");
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return (jpeg.ToArray(), "jpeg");
    }

    // Skia throws for some non-picture data instead of returning null, so both outcomes count as "not a picture".
    static SKBitmap Decode(byte[] source)
    {
        SKBitmap? bitmap;
        try
        {
            bitmap = SKBitmap.Decode(source);
        }
        catch (ArgumentException)
        {
            bitmap = null;
        }
        return bitmap ?? throw new InvalidDataException("The file is not a picture.");
    }

    // The type from the file's first bytes; null for anything other than PNG or JPEG.
    static string? Detect(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "jpeg";
        return null;
    }
}
