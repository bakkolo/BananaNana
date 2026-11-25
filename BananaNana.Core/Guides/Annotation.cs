using System.Text.Json.Serialization;
namespace BananaNana;

// One piece of markup on a guide step's picture. Coordinates are in image pixels (the original picture, before any
// crop), so the zoom and the window size of the editor do not matter. Only the fields a kind uses are meaningful:
// Arrow and Box/Highlight/Blur use X1..Y2 (arrow tail and tip, or two opposite corners); Number and Text use X1, Y1
// (number centre, or the top-left of the text).
public sealed class Annotation
{
    public AnnotationKind Kind { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string Color { get; set; } = "#E5484D";
    public AnnotationThickness Thickness { get; set; } = AnnotationThickness.M;
    public int Number { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 32;

    public Annotation Clone() => (Annotation)MemberwiseClone();
}

[JsonConverter(typeof(JsonStringEnumConverter<AnnotationKind>))]
public enum AnnotationKind { Arrow, Box, Highlight, Number, Text, Blur }

// Line width: 3, 6 or 10 px on an 800 px wide picture, scaled with the picture size (see MarkupMath in the desktop app).
[JsonConverter(typeof(JsonStringEnumConverter<AnnotationThickness>))]
public enum AnnotationThickness { S, M, L }

// The step's crop, in image pixels of the original picture. Null means the whole picture.
public sealed class CropRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public CropRect Clone() => (CropRect)MemberwiseClone();
}
