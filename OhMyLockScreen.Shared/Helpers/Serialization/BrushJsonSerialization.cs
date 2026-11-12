using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml.Media;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

namespace OhMyLockScreen.Shared.Helpers.Serialization;

/// <summary>画刷 JSON 的固定字段契约；不遍历依赖对象或解析 CLR 类型名。</summary>
internal static class BrushJsonSerialization
{
    [SupportedOSPlatformGuard("windows10.0.18362")]
    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The UWP target requires Windows 10 17763; platform guard bodies are analyzed without the target's baseline.")]
    private static bool HasTintLuminosityOpacity => ApiInformation.IsPropertyPresent(
        "Windows.UI.Xaml.Media.AcrylicBrush", "TintLuminosityOpacity");

    internal static Brush Read(JsonElement json, Type declaredType)
    {
        var fields = new Fields(json);
        Brush brush = ReadString(fields.Required("$type"), "$type") switch
        {
            "solid" => new SolidColorBrush(),
            "linearGradient" => new LinearGradientBrush(),
            "radialGradient" => new RadialGradientBrush(),
            "acrylic" => new AcrylicBrush(),
            "image" => new ImageBrush(),
            _ => throw Error("$type", "未知画刷类型。")
        };

        if (!declaredType.IsInstanceOfType(brush))
        {
            throw Error("$type", $"画刷不能赋给 {declaredType.Name}。");
        }

        fields.Apply("Opacity", value => brush.Opacity = ReadNumber(value, "Opacity", 0, 1));
        fields.Apply("Transform", value =>
        {
            var transform = ReadTransform(value, "Transform");
            if (brush is XamlCompositionBrushBase)
            {
                EnsureCompositionIdentity(transform, "Transform");
            }
            else
            {
                brush.Transform = transform;
            }
        });
        fields.Apply("RelativeTransform", value =>
        {
            var transform = ReadTransform(value, "RelativeTransform");
            if (brush is XamlCompositionBrushBase)
            {
                EnsureCompositionIdentity(transform, "RelativeTransform");
            }
            else
            {
                brush.RelativeTransform = transform;
            }
        });

        switch (brush)
        {
            case SolidColorBrush solid:
                fields.Apply("Color", value => solid.Color = ReadColor(value, "Color"));
                break;
            case LinearGradientBrush linear:
                fields.Apply("StartPoint", value => linear.StartPoint = ReadPoint(value, "StartPoint"));
                fields.Apply("EndPoint", value => linear.EndPoint = ReadPoint(value, "EndPoint"));
                fields.Apply("MappingMode", value => linear.MappingMode = ReadEnum<BrushMappingMode>(value, "MappingMode"));
                fields.Apply("SpreadMethod", value => linear.SpreadMethod = ReadEnum<GradientSpreadMethod>(value, "SpreadMethod"));
                fields.Apply("ColorInterpolationMode", value => linear.ColorInterpolationMode = ReadEnum<ColorInterpolationMode>(value, "ColorInterpolationMode"));
                fields.Apply("GradientStops", value => ReadStops(value, linear.GradientStops));
                break;
            case RadialGradientBrush radial:
                fields.Apply("Center", value => radial.Center = ReadPoint(value, "Center"));
                fields.Apply("GradientOrigin", value => radial.GradientOrigin = ReadPoint(value, "GradientOrigin"));
                fields.Apply("RadiusX", value => radial.RadiusX = ReadNumber(value, "RadiusX", 0));
                fields.Apply("RadiusY", value => radial.RadiusY = ReadNumber(value, "RadiusY", 0));
                fields.Apply("MappingMode", value => radial.MappingMode = ReadEnum<BrushMappingMode>(value, "MappingMode"));
                fields.Apply("SpreadMethod", value => radial.SpreadMethod = ReadEnum<GradientSpreadMethod>(value, "SpreadMethod"));
                fields.Apply("InterpolationSpace", value => radial.InterpolationSpace = ReadEnum<CompositionColorSpace>(value, "InterpolationSpace"));
                fields.Apply("FallbackColor", value => radial.FallbackColor = ReadColor(value, "FallbackColor"));
                fields.Apply("GradientStops", value => ReadStops(value, radial.GradientStops));
                break;
            case AcrylicBrush acrylic:
                fields.Apply("BackgroundSource", value => acrylic.BackgroundSource = ReadEnum<AcrylicBackgroundSource>(value, "BackgroundSource"));
                fields.Apply("TintColor", value => acrylic.TintColor = ReadColor(value, "TintColor"));
                fields.Apply("TintOpacity", value => acrylic.TintOpacity = ReadNumber(value, "TintOpacity", 0, 1));
                fields.Apply("TintLuminosityOpacity", value => ReadLuminosity(value, acrylic));
                fields.Apply("FallbackColor", value => acrylic.FallbackColor = ReadColor(value, "FallbackColor"));
                fields.Apply("AlwaysUseFallback", value => acrylic.AlwaysUseFallback = ReadBoolean(value, "AlwaysUseFallback"));
                fields.Apply("TintTransitionDuration", value => acrylic.TintTransitionDuration = ReadDuration(value));
                break;
            case ImageBrush image:
                // An old scene can still open with its image brush styling intact.
                fields.Apply("ImageSource", _ => { });
                fields.Apply("BlobId", value => SceneImageBrush.SetBlobId(image,
                    value.ValueKind == JsonValueKind.Null ? null : ReadString(value, "BlobId")));
                fields.Apply("Stretch", value => image.Stretch = ReadEnum<Stretch>(value, "Stretch"));
                fields.Apply("AlignmentX", value => image.AlignmentX = ReadEnum<AlignmentX>(value, "AlignmentX"));
                fields.Apply("AlignmentY", value => image.AlignmentY = ReadEnum<AlignmentY>(value, "AlignmentY"));
                break;
        }

        fields.EnsureConsumed();
        return brush;
    }

    internal static JsonObject Write(Brush brush)
    {
        // Do not silently serialize only the base part of an unsupported custom brush.
        var type = brush.GetType();
        var discriminator = type == typeof(SolidColorBrush) ? "solid"
            : type == typeof(LinearGradientBrush) ? "linearGradient"
            : type == typeof(RadialGradientBrush) ? "radialGradient"
            : type == typeof(AcrylicBrush) ? "acrylic"
            : type == typeof(ImageBrush) ? "image"
            : throw new NotSupportedException($"不支持保存画刷 {type.FullName}。");
        var json = new JsonObject
        {
            ["$type"] = discriminator,
            ["Opacity"] = Number(brush.Opacity, "Opacity", 0, 1),
            ["Transform"] = WriteBrushTransform(brush, brush.Transform, "Transform"),
            ["RelativeTransform"] = WriteBrushTransform(brush, brush.RelativeTransform, "RelativeTransform")
        };

        switch (brush)
        {
            case SolidColorBrush solid:
                json["Color"] = WriteColor(solid.Color);
                break;
            case LinearGradientBrush linear:
                json["StartPoint"] = WritePoint(linear.StartPoint);
                json["EndPoint"] = WritePoint(linear.EndPoint);
                json["MappingMode"] = WriteEnum(linear.MappingMode);
                json["SpreadMethod"] = WriteEnum(linear.SpreadMethod);
                json["ColorInterpolationMode"] = WriteEnum(linear.ColorInterpolationMode);
                json["GradientStops"] = WriteStops(linear.GradientStops);
                break;
            case RadialGradientBrush radial:
                json["Center"] = WritePoint(radial.Center);
                json["GradientOrigin"] = WritePoint(radial.GradientOrigin);
                json["RadiusX"] = Number(radial.RadiusX, "RadiusX", 0);
                json["RadiusY"] = Number(radial.RadiusY, "RadiusY", 0);
                json["MappingMode"] = WriteEnum(radial.MappingMode);
                json["SpreadMethod"] = WriteEnum(radial.SpreadMethod);
                json["InterpolationSpace"] = WriteEnum(radial.InterpolationSpace);
                json["FallbackColor"] = WriteColor(radial.FallbackColor);
                json["GradientStops"] = WriteStops(radial.GradientStops);
                break;
            case AcrylicBrush acrylic:
                json["BackgroundSource"] = WriteEnum(acrylic.BackgroundSource);
                json["TintColor"] = WriteColor(acrylic.TintColor);
                json["TintOpacity"] = Number(acrylic.TintOpacity, "TintOpacity", 0, 1);
                var luminosity = HasTintLuminosityOpacity ? acrylic.TintLuminosityOpacity : null;
                json["TintLuminosityOpacity"] = luminosity is { } opacity ? Number(opacity, "TintLuminosityOpacity", 0, 1) : null;
                json["FallbackColor"] = WriteColor(acrylic.FallbackColor);
                json["AlwaysUseFallback"] = acrylic.AlwaysUseFallback;
                if (acrylic.TintTransitionDuration < TimeSpan.Zero)
                {
                    throw Error("TintTransitionDuration", "时间不能为负数。");
                }

                json["TintTransitionDuration"] = acrylic.TintTransitionDuration.ToString("c", CultureInfo.InvariantCulture);
                break;
            case ImageBrush image:
                var blobId = SceneImageBrush.GetBlobId(image);
                if (blobId == null && image.ImageSource != null)
                {
                    throw new NotSupportedException("图像画刷必须使用场景资产标识。");
                }

                json["BlobId"] = blobId;
                json["Stretch"] = WriteEnum(image.Stretch);
                json["AlignmentX"] = WriteEnum(image.AlignmentX);
                json["AlignmentY"] = WriteEnum(image.AlignmentY);
                break;
        }

        return json;
    }

    private static string ReadString(JsonElement value, string path)
        => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Error(path, "必须为字符串。");

    private static bool ReadBoolean(JsonElement value, string path)
        => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Error(path, "必须为布尔值。");

    private static double ReadNumber(JsonElement value, string path, double min = double.MinValue, double max = double.MaxValue)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? Number(number, path, min, max) : throw Error(path, "必须为有限数值。");

    private static double Number(double value, string path, double min = double.MinValue, double max = double.MaxValue)
        => double.IsFinite(value) && value >= min && value <= max ? value : throw Error(path, "数值非有限或超出范围。");

    private static T ReadEnum<T>(JsonElement value, string path) where T : struct, Enum
    {
        var name = ReadString(value, path);
        return Enum.TryParse<T>(name, out var result) && Enum.GetName(result) == name
            ? result : throw Error(path, "未知枚举成员。");
    }

    private static string WriteEnum<T>(T value) where T : struct, Enum
        => Enum.GetName(value) ?? throw Error(typeof(T).Name, "未知枚举成员。");

    private static Color ReadColor(JsonElement value, string path)
    {
        var text = ReadString(value, path);
        if (text.Length is not (7 or 9) || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color))
        {
            throw Error(path, "颜色必须为 #RRGGBB 或 #AARRGGBB。");
        }

        return Color.FromArgb(text.Length == 7 ? (byte)255 : (byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    private static string WriteColor(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Point ReadPoint(JsonElement value, string path)
    {
        var fields = new Fields(value, path);
        var point = new Point(ReadNumber(fields.Required("X"), $"{path}.X"), ReadNumber(fields.Required("Y"), $"{path}.Y"));
        fields.EnsureConsumed();
        return point;
    }

    private static JsonObject WritePoint(Point point) => new()
    {
        ["X"] = Number(point.X, "X"),
        ["Y"] = Number(point.Y, "Y")
    };

    private static void ReadStops(JsonElement value, IList<GradientStop> stops)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Error("GradientStops", "必须为数组。");
        }

        stops.Clear();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var path = $"GradientStops[{index++}]";
            var fields = new Fields(item, path);
            stops.Add(new GradientStop
            {
                Color = ReadColor(fields.Required("Color"), $"{path}.Color"),
                Offset = ReadNumber(fields.Required("Offset"), $"{path}.Offset")
            });
            fields.EnsureConsumed();
        }
    }

    private static JsonArray WriteStops(IEnumerable<GradientStop> stops)
    {
        var result = new JsonArray();
        foreach (var stop in stops)
        {
            result.Add(new JsonObject
            {
                ["Color"] = WriteColor(stop.Color),
                ["Offset"] = Number(stop.Offset, "GradientStops.Offset")
            });
        }

        return result;
    }

    private static void ReadLuminosity(JsonElement value, AcrylicBrush brush)
    {
        double? opacity = value.ValueKind == JsonValueKind.Null ? null : ReadNumber(value, "TintLuminosityOpacity", 0, 1);
        if (HasTintLuminosityOpacity)
        {
            brush.TintLuminosityOpacity = opacity;
        }
        else if (opacity != null)
        {
            throw new NotSupportedException("当前系统不支持 AcrylicBrush.TintLuminosityOpacity。");
        }
    }

    private static TimeSpan ReadDuration(JsonElement value)
    {
        return TimeSpan.TryParseExact(ReadString(value, "TintTransitionDuration"), "c", CultureInfo.InvariantCulture, out var duration)
               && duration >= TimeSpan.Zero
            ? duration : throw Error("TintTransitionDuration", "必须为非负的 TimeSpan c 格式时间。");
    }

    private static MatrixTransform? ReadTransform(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var fields = new Fields(value, path);
        var matrix = new Matrix(
            ReadNumber(fields.Required("M11"), $"{path}.M11"),
            ReadNumber(fields.Required("M12"), $"{path}.M12"),
            ReadNumber(fields.Required("M21"), $"{path}.M21"),
            ReadNumber(fields.Required("M22"), $"{path}.M22"),
            ReadNumber(fields.Required("OffsetX"), $"{path}.OffsetX"),
            ReadNumber(fields.Required("OffsetY"), $"{path}.OffsetY"));
        fields.EnsureConsumed();
        return new MatrixTransform { Matrix = matrix };
    }

    private static bool IsIdentity(Matrix matrix) => matrix.M11 == 1 && matrix.M12 == 0
        && matrix.M21 == 0 && matrix.M22 == 1 && matrix.OffsetX == 0 && matrix.OffsetY == 0;

    private static void EnsureCompositionIdentity(Transform? transform, string path)
    {
        if (transform != null && !IsIdentity(GetMatrix(transform)))
        {
            throw Error(path, "Composition 画刷不支持变换。");
        }
    }

    private static JsonObject? WriteBrushTransform(Brush brush, Transform? transform, string path)
    {
        if (brush is XamlCompositionBrushBase)
        {
            // Native composition brushes return identity transforms but reject all
            // assignments. Normalize them to null and accept legacy identity JSON.
            EnsureCompositionIdentity(transform, path);
            return null;
        }
        return WriteTransform(transform);
    }

    private static JsonObject? WriteTransform(Transform? transform)
    {
        if (transform == null)
        {
            return null;
        }

        var matrix = GetMatrix(transform);
        return new JsonObject
        {
            ["M11"] = Number(matrix.M11, "Transform.M11"),
            ["M12"] = Number(matrix.M12, "Transform.M12"),
            ["M21"] = Number(matrix.M21, "Transform.M21"),
            ["M22"] = Number(matrix.M22, "Transform.M22"),
            ["OffsetX"] = Number(matrix.OffsetX, "Transform.OffsetX"),
            ["OffsetY"] = Number(matrix.OffsetY, "Transform.OffsetY")
        };
    }

    internal static Matrix GetMatrix(Transform transform)
    {
        if (transform is MatrixTransform matrix)
        {
            return matrix.Matrix;
        }

        if (transform is TransformGroup group)
        {
            return group.Value;
        }

        // Let XAML calculate the matrix. Clone first so an existing transform is
        // never reparented, and avoid sampling points (which loses small scales
        // when a transform also contains a very large translation).
        Transform snapshot = transform switch
        {
            TranslateTransform value => new TranslateTransform { X = value.X, Y = value.Y },
            RotateTransform value => new RotateTransform { Angle = value.Angle, CenterX = value.CenterX, CenterY = value.CenterY },
            ScaleTransform value => new ScaleTransform { ScaleX = value.ScaleX, ScaleY = value.ScaleY, CenterX = value.CenterX, CenterY = value.CenterY },
            SkewTransform value => new SkewTransform { AngleX = value.AngleX, AngleY = value.AngleY, CenterX = value.CenterX, CenterY = value.CenterY },
            CompositeTransform value => new CompositeTransform
            {
                CenterX = value.CenterX,
                CenterY = value.CenterY,
                ScaleX = value.ScaleX,
                ScaleY = value.ScaleY,
                SkewX = value.SkewX,
                SkewY = value.SkewY,
                Rotation = value.Rotation,
                TranslateX = value.TranslateX,
                TranslateY = value.TranslateY
            },
            _ => throw new NotSupportedException($"不支持保存画刷变换 {transform.GetType().FullName}。")
        };
        return new TransformGroup { Children = { snapshot } }.Value;
    }

    private static JsonException Error(string path, string message) => new($"画刷字段 {path}：{message}");

    private sealed class Fields
    {
        private readonly Dictionary<string, JsonElement> _remaining = new(StringComparer.Ordinal);
        private readonly string _path;

        internal Fields(JsonElement json, string path = "$")
        {
            _path = path;
            if (json.ValueKind != JsonValueKind.Object)
            {
                throw Error(path, "必须为对象。");
            }

            foreach (var property in json.EnumerateObject())
            {
                if (!_remaining.TryAdd(property.Name, property.Value))
                {
                    throw Error($"{path}.{property.Name}", "字段重复。");
                }
            }
        }

        internal JsonElement Required(string name)
            => _remaining.Remove(name, out var value) ? value : throw Error($"{_path}.{name}", "缺少必需字段。");

        internal void Apply(string name, Action<JsonElement> apply)
        {
            if (!_remaining.Remove(name, out var value))
            {
                return;
            }

            try
            {
                apply(value);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException($"画刷字段 {_path}.{name}：参数不被框架接受。", exception);
            }
        }

        internal void EnsureConsumed()
        {
            if (_remaining.Count > 0)
            {
                throw Error($"{_path}.{_remaining.Keys.First()}", "未知字段。");
            }
        }
    }
}
