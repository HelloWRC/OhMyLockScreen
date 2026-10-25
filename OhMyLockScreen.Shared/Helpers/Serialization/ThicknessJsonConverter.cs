using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Helpers.Serialization;
/// <summary>UWP Thickness 使用字段保存分量，显式转换以保证布局能完整往返。</summary>
public sealed class ThicknessJsonConverter : JsonConverter<Thickness>
{
    public override Thickness Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new Thickness(ReadPart(root, "Left"), ReadPart(root, "Top"), ReadPart(root, "Right"), ReadPart(root, "Bottom"));
    }

    private static double ReadPart(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetDouble() : 0;
    public override void Write(Utf8JsonWriter writer, Thickness value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Left", value.Left);
        writer.WriteNumber("Top", value.Top);
        writer.WriteNumber("Right", value.Right);
        writer.WriteNumber("Bottom", value.Bottom);
        writer.WriteEndObject();
    }
}