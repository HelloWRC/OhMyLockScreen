using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Helpers.Serialization;

/// <summary>
/// 为 Brush 及具体画刷声明提供静态值的 JSON 转换。配置读写自动使用此转换器；
/// 独立的 JsonSerializer 调用可将此工厂添加到 JsonSerializerOptions.Converters。
/// 画刷必须在所属 UI 线程访问；编辑后应整体赋回模型属性以触发自动保存。
/// 不保存绑定、动画、主题资源引用或 Composition 对象。
/// </summary>
public sealed class BrushJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeof(Brush).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!CanConvert(typeToConvert))
        {
            throw new ArgumentException("类型必须派生自 Brush。", nameof(typeToConvert));
        }

        return (JsonConverter)Activator.CreateInstance(typeof(BrushConverter<>).MakeGenericType(typeToConvert), nonPublic: true)!;
    }

    private sealed class BrushConverter<TBrush> : JsonConverter<TBrush> where TBrush : Brush
    {
        public override TBrush Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return (TBrush)BrushJsonSerialization.Read(document.RootElement, typeToConvert);
        }

        public override void Write(Utf8JsonWriter writer, TBrush value, JsonSerializerOptions options)
        {
            BrushJsonSerialization.Write(value).WriteTo(writer);
        }
    }
}
