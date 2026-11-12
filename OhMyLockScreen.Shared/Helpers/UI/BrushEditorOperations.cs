using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Xaml.Media;
using System.Runtime.Versioning;
using System.Diagnostics.CodeAnalysis;
using OhMyLockScreen.Shared.Helpers.Serialization;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

namespace OhMyLockScreen.Shared.Helpers.UI;

internal enum BrushEditorKind
{
    Unsupported = -1,
    None,
    Solid,
    Linear,
    Radial,
    Image,
    Acrylic
}

/// <summary>Value edits always return a new brush, so model setters can observe and save them.</summary>
internal static class BrushEditorOperations
{
    [SupportedOSPlatformGuard("windows10.0.18362")]
    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The runtime API guard is also exercised by simulated older-system tests.")]
    private static bool HasLuminosity => ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Media.AcrylicBrush", "TintLuminosityOpacity");
    internal static BrushEditorKind Kind(Brush? brush)
    {
        var type = brush?.GetType();
        return brush == null ? BrushEditorKind.None
            : type == typeof(SolidColorBrush) ? BrushEditorKind.Solid
            : type == typeof(LinearGradientBrush) ? BrushEditorKind.Linear
            : type == typeof(RadialGradientBrush) ? BrushEditorKind.Radial
            : type == typeof(ImageBrush) ? BrushEditorKind.Image
            : type == typeof(AcrylicBrush) ? BrushEditorKind.Acrylic
            : BrushEditorKind.Unsupported;
    }

    internal static Brush? Create(BrushEditorKind kind, double opacity = 1)
    {
        Brush? brush = kind switch
        {
            BrushEditorKind.None => null,
            BrushEditorKind.Solid => new SolidColorBrush(White),
            BrushEditorKind.Linear => new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) },
            BrushEditorKind.Radial => new RadialGradientBrush { Center = new(0.5, 0.5), GradientOrigin = new(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 },
            BrushEditorKind.Image => new ImageBrush { Stretch = Stretch.UniformToFill },
            BrushEditorKind.Acrylic => new AcrylicBrush { TintColor = White, FallbackColor = White, TintOpacity = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (brush != null)
        {
            brush.Opacity = Math.Clamp(opacity, 0, 1);
        }
        if (Stops(brush) is { } stops)
        {
            stops.Add(new GradientStop { Color = Color.FromArgb(255, 0, 0, 0), Offset = 0 });
            stops.Add(new GradientStop { Color = White, Offset = 1 });
        }
        return brush;
    }

    internal static Color White => Color.FromArgb(255, 255, 255, 255);

    internal static IList<GradientStop>? Stops(Brush? brush) => brush switch
    {
        LinearGradientBrush linear => linear.GradientStops,
        RadialGradientBrush radial => radial.GradientStops,
        _ => null
    };

    internal static Brush Edit(Brush source, Action<Brush> edit)
    {
        var copy = Copy(source);
        edit(copy);
        return copy;
    }

    internal static Brush Copy(Brush source)
    {
        if (Kind(source) == BrushEditorKind.Unsupported)
        {
            throw new NotSupportedException($"不支持编辑画刷 {source.GetType().Name}。");
        }
        Brush result = source switch
        {
            SolidColorBrush value => new SolidColorBrush(value.Color),
            LinearGradientBrush value => new LinearGradientBrush
            {
                StartPoint = value.StartPoint,
                EndPoint = value.EndPoint,
                MappingMode = value.MappingMode,
                SpreadMethod = value.SpreadMethod,
                ColorInterpolationMode = value.ColorInterpolationMode
            },
            RadialGradientBrush value => new RadialGradientBrush
            {
                Center = value.Center,
                GradientOrigin = value.GradientOrigin,
                RadiusX = value.RadiusX,
                RadiusY = value.RadiusY,
                MappingMode = value.MappingMode,
                SpreadMethod = value.SpreadMethod,
                InterpolationSpace = value.InterpolationSpace,
                FallbackColor = value.FallbackColor
            },
            AcrylicBrush value => CopyAcrylic(value),
            ImageBrush value => new ImageBrush
            {
                ImageSource = value.ImageSource,
                Stretch = value.Stretch,
                AlignmentX = value.AlignmentX,
                AlignmentY = value.AlignmentY
            },
            _ => throw new NotSupportedException()
        };
        result.Opacity = source.Opacity;
        if (source is ImageBrush image && result is ImageBrush imageCopy)
        {
            SceneImageBrush.SetBlobId(imageCopy, SceneImageBrush.GetBlobId(image));
        }

        // Composition brushes reject even assigning null to these inherited properties.
        if (result is not XamlCompositionBrushBase)
        {
            result.Transform = CopyTransform(source.Transform);
            result.RelativeTransform = CopyTransform(source.RelativeTransform);
        }
        if (Stops(source) is { } sourceStops && Stops(result) is { } resultStops)
        {
            foreach (var stop in sourceStops)
            {
                resultStops.Add(new GradientStop { Color = stop.Color, Offset = stop.Offset });
            }
        }
        return result;
    }

    private static AcrylicBrush CopyAcrylic(AcrylicBrush source)
    {
        var copy = new AcrylicBrush
        {
            BackgroundSource = source.BackgroundSource,
            TintColor = source.TintColor,
            TintOpacity = source.TintOpacity,
            FallbackColor = source.FallbackColor,
            AlwaysUseFallback = source.AlwaysUseFallback,
            TintTransitionDuration = source.TintTransitionDuration
        };
        if (HasLuminosity)
        {
            copy.TintLuminosityOpacity = source.TintLuminosityOpacity;
        }
        return copy;
    }

    private static Transform? CopyTransform(Transform? value) => value == null
        ? null : new MatrixTransform { Matrix = BrushJsonSerialization.GetMatrix(value) };

    internal static double RoundDraggedOffset(double offset) =>
        Math.Round(Math.Clamp(offset, 0, 1) * 100, 1, MidpointRounding.AwayFromZero) / 100;

    internal static Brush SetOffset(Brush source, int index, double offset) => Edit(source, copy =>
    {
        if (Stops(copy) is { } stops && index >= 0 && index < stops.Count && double.IsFinite(offset))
        {
            stops[index].Offset = Math.Clamp(offset, 0, 1);
        }
    });

    internal static Brush Reverse(Brush source) => Edit(source, copy =>
    {
        foreach (var stop in Stops(copy) ?? [])
        {
            stop.Offset = 1 - stop.Offset;
        }
    });

    internal static Brush AddStop(Brush source, double offset) => Edit(source, copy =>
    {
        if (Stops(copy) is { } stops)
        {
            offset = Math.Clamp(offset, 0, 1);
            var color = Sample(stops, offset);
            stops.Add(new GradientStop { Offset = offset, Color = color });
        }
    });

    internal static Brush RemoveStop(Brush source, int index) => Edit(source, copy =>
    {
        if (Stops(copy) is { } stops && index >= 0 && index < stops.Count)
        {
            stops.RemoveAt(index);
        }
    });

    internal static Color Sample(IEnumerable<GradientStop> stops, double offset)
    {
        var ordered = stops.OrderBy(stop => stop.Offset).ToArray();
        if (ordered.Length == 0)
        {
            return White;
        }
        var left = ordered.LastOrDefault(stop => stop.Offset <= offset) ?? ordered[0];
        var right = ordered.FirstOrDefault(stop => stop.Offset > offset) ?? ordered[^1];
        var t = right.Offset > left.Offset ? Math.Clamp((offset - left.Offset) / (right.Offset - left.Offset), 0, 1) : 0;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromArgb(Mix(left.Color.A, right.Color.A), Mix(left.Color.R, right.Color.R), Mix(left.Color.G, right.Color.G), Mix(left.Color.B, right.Color.B));
    }
}

/// <summary>Double precision affine coordinates, shared by panel, overlay and tests.</summary>
internal readonly record struct BrushEditorMatrix(double M11, double M12, double M21, double M22, double X, double Y)
{
    internal static BrushEditorMatrix Identity => new(1, 0, 0, 1, 0, 0);
    internal Point Map(Point point) => new(point.X * M11 + point.Y * M21 + X, point.X * M12 + point.Y * M22 + Y);

    internal BrushEditorMatrix Then(BrushEditorMatrix next) => new(
        M11 * next.M11 + M12 * next.M21, M11 * next.M12 + M12 * next.M22,
        M21 * next.M11 + M22 * next.M21, M21 * next.M12 + M22 * next.M22,
        X * next.M11 + Y * next.M21 + next.X, X * next.M12 + Y * next.M22 + next.Y);

    internal bool TryInverse(out BrushEditorMatrix inverse)
    {
        var determinant = M11 * M22 - M12 * M21;
        inverse = default;
        if (!double.IsFinite(determinant) || determinant == 0)
        {
            return false;
        }
        inverse = new(M22 / determinant, -M12 / determinant, -M21 / determinant, M11 / determinant,
            (Y * M21 - X * M22) / determinant, (X * M12 - Y * M11) / determinant);
        return new[] { inverse.M11, inverse.M12, inverse.M21, inverse.M22, inverse.X, inverse.Y }.All(double.IsFinite);
    }

    internal static BrushEditorMatrix From(Transform? transform)
    {
        if (transform == null)
        {
            return Identity;
        }
        var value = BrushJsonSerialization.GetMatrix(transform);
        return new(value.M11, value.M12, value.M21, value.M22, value.OffsetX, value.OffsetY);
    }

    internal static BrushEditorMatrix ForBrush(Brush brush, double width, double height)
    {
        var mapping = brush switch
        {
            LinearGradientBrush linear => linear.MappingMode,
            RadialGradientBrush radial => radial.MappingMode,
            _ => BrushMappingMode.RelativeToBoundingBox
        };
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return default;
        }
        var size = new BrushEditorMatrix(width, 0, 0, height, 0, 0);
        var normalize = new BrushEditorMatrix(1 / width, 0, 0, 1 / height, 0, 0);
        return (mapping == BrushMappingMode.Absolute ? normalize : Identity)
            .Then(From(brush.RelativeTransform)).Then(size).Then(From(brush.Transform));
    }

    internal static double Project(Point point, Point start, Point end)
    {
        var x = end.X - start.X;
        var y = end.Y - start.Y;
        var length = x * x + y * y;
        return length > 0 ? Math.Clamp(((point.X - start.X) * x + (point.Y - start.Y) * y) / length, 0, 1) : 0;
    }
}
