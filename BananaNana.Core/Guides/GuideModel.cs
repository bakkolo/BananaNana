using System.Text.Json;
using System.Text.Json.Serialization;
namespace BananaNana;

// One step of a guide. Image is base64 (PNG or JPEG, see ImageType) or null. Annotations and Crop are markup over the
// picture; the original picture is never changed by them (GuideRender.Flatten draws them). Files written before
// stage 2 have no crop and empty annotations, which read as no markup.
public sealed class GuideStep
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Image { get; set; }
    public string? ImageType { get; set; }
    public List<Annotation> Annotations { get; set; } = new();
    public CropRect? Crop { get; set; }
}

// The file format: <guid>.bnguide, version 1. The guide id is the file name, so it is not stored in the JSON.
public sealed class GuideDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    [JsonIgnore] public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Intro { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
    public List<GuideStep> Steps { get; set; } = new();
}

// What the guide list needs; read from the file without keeping the pictures in memory.
public sealed record GuideSummary(Guid Id, string Title, DateTimeOffset Modified);

// Build-time generated JSON code (like LibraryJson): no reflection, so it also works in trimmed builds.
[JsonSerializable(typeof(GuideDocument))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class GuideJson : JsonSerializerContext { }
