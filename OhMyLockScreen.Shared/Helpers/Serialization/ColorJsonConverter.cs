using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI;

namespace OhMyLockScreen.Shared.Helpers.Serialization;

/// <summary>以 #RRGGBB / #AARRGGBB 格式序列化和反序列化 UWP <see cref="Color"/>。</summary>
public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("颜色必须为字符串。");
        }

        var text = reader.GetString();
        if (text == null
            || text.Length is not (7 or 9)
            || text[0] != '#'
            || !IsAsciiHex(text.AsSpan(1))
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color))
        {
            throw new JsonException("颜色必须为 #RRGGBB 或 #AARRGGBB。");
        }

        return Color.FromArgb(
            text.Length == 7 ? (byte)255 : (byte)(color >> 24),
            (byte)(color >> 16),
            (byte)(color >> 8),
            (byte)color);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        => writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");

    private static bool IsAsciiHex(ReadOnlySpan<char> chars)
    {
        foreach (var ch in chars)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
