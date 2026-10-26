using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Helpers.Serialization;

/// <summary>显式保存 UWP CornerRadius 的四角分量。</summary>
public sealed class CornerRadiusJsonConverter : JsonConverter<CornerRadius>
{
    public override CornerRadius Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new CornerRadius(
            ReadPart(root, "TopLeft"), ReadPart(root, "TopRight"),
            ReadPart(root, "BottomRight"), ReadPart(root, "BottomLeft"));
    }

    private static double ReadPart(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetDouble() : 0;

    public override void Write(Utf8JsonWriter writer, CornerRadius value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("TopLeft", value.TopLeft);
        writer.WriteNumber("TopRight", value.TopRight);
        writer.WriteNumber("BottomRight", value.BottomRight);
        writer.WriteNumber("BottomLeft", value.BottomLeft);
        writer.WriteEndObject();
    }
}
