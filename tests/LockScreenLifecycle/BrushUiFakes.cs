// Value-only stand-ins for serialization tests. Native XAML matrices and rendering
// must also be verified with the actual UWP framework.
namespace Windows.Foundation.Metadata
{
    public static class ApiInformation
    {
        public static bool HasTintLuminosityOpacity { get; set; } = true;

        public static bool IsPropertyPresent(string typeName, string propertyName) => HasTintLuminosityOpacity;
    }
}

namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
}

namespace Windows.UI.Composition
{
    public enum CompositionColorSpace
    {
        Auto,
        Hsl,
        Rgb,
        HslLinear,
        RgbLinear
    }
}

namespace Windows.UI.Xaml.Media
{
    public class Brush : DependencyObject
    {
        public double Opacity { get; set; } = 1;
        public Transform? Transform
        {
            get; set;
        }
        public Transform? RelativeTransform
        {
            get; set;
        }
    }

    public sealed class SolidColorBrush : Brush
    {
        public SolidColorBrush()
        {
        }

        public SolidColorBrush(Color color) => Color = color;

        public Color Color
        {
            get; set;
        }
    }

    public enum BrushMappingMode
    {
        Absolute,
        RelativeToBoundingBox
    }

    public enum GradientSpreadMethod
    {
        Pad,
        Reflect,
        Repeat
    }

    public enum ColorInterpolationMode
    {
        ScRgbLinearInterpolation,
        SRgbLinearInterpolation
    }

    public enum AcrylicBackgroundSource
    {
        Backdrop,
        HostBackdrop
    }

    public enum AlignmentX
    {
        Left,
        Center,
        Right
    }

    public enum AlignmentY
    {
        Top,
        Center,
        Bottom
    }

    public enum Stretch
    {
        None,
        Fill,
        Uniform,
        UniformToFill
    }

    public sealed class GradientStop
    {
        public Color Color
        {
            get; set;
        }
        public double Offset
        {
            get; set;
        }
    }

    public sealed class GradientStopCollection : List<GradientStop>;

    public class GradientBrush : Brush
    {
        public BrushMappingMode MappingMode { get; set; } = BrushMappingMode.RelativeToBoundingBox;
        public GradientSpreadMethod SpreadMethod
        {
            get; set;
        }
        public ColorInterpolationMode ColorInterpolationMode { get; set; } = ColorInterpolationMode.SRgbLinearInterpolation;
        public GradientStopCollection GradientStops { get; } = [];
    }

    public sealed class LinearGradientBrush : GradientBrush
    {
        public Foundation.Point StartPoint
        {
            get; set;
        }
        public Foundation.Point EndPoint { get; set; } = new(1, 1);
    }

    public class XamlCompositionBrushBase : Brush
    {
        public Color FallbackColor
        {
            get; set;
        }
    }

    public sealed class AcrylicBrush : XamlCompositionBrushBase
    {
        public AcrylicBackgroundSource BackgroundSource
        {
            get; set;
        }
        public Color TintColor
        {
            get; set;
        }
        public double TintOpacity { get; set; } = 0.5;
        public bool AlwaysUseFallback
        {
            get; set;
        }
        public TimeSpan TintTransitionDuration { get; set; } = TimeSpan.FromMilliseconds(500);
        private double? _tintLuminosityOpacity;

        public double? TintLuminosityOpacity
        {
            get
            {
                EnsureLuminositySupported();
                return _tintLuminosityOpacity;
            }
            set
            {
                EnsureLuminositySupported();
                _tintLuminosityOpacity = value;
            }
        }

        private static void EnsureLuminositySupported()
        {
            if (!Foundation.Metadata.ApiInformation.HasTintLuminosityOpacity)
            {
                throw new InvalidOperationException("Unavailable native API was accessed.");
            }
        }
    }

    public class ImageSource;

    public sealed class ImageBrush : Brush
    {
        public ImageSource? ImageSource
        {
            get; set;
        }
        public Stretch Stretch { get; set; } = Stretch.Fill;
        public AlignmentX AlignmentX { get; set; } = AlignmentX.Center;
        public AlignmentY AlignmentY { get; set; } = AlignmentY.Center;
    }

    public readonly record struct Matrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

        internal Matrix Append(Matrix next) => new(
            M11 * next.M11 + M12 * next.M21,
            M11 * next.M12 + M12 * next.M22,
            M21 * next.M11 + M22 * next.M21,
            M21 * next.M12 + M22 * next.M22,
            OffsetX * next.M11 + OffsetY * next.M21 + next.OffsetX,
            OffsetX * next.M12 + OffsetY * next.M22 + next.OffsetY);
    }

    public sealed class MatrixTransform : Transform
    {
        public Matrix Matrix { get; set; } = Matrix.Identity;
    }

    public sealed class RotateTransform : Transform
    {
        public double Angle
        {
            get; set;
        }
        public double CenterX
        {
            get; set;
        }
        public double CenterY
        {
            get; set;
        }
    }

    public sealed class ScaleTransform : Transform
    {
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double CenterX
        {
            get; set;
        }
        public double CenterY
        {
            get; set;
        }
    }

    public sealed class SkewTransform : Transform
    {
        public double AngleX
        {
            get; set;
        }
        public double AngleY
        {
            get; set;
        }
        public double CenterX
        {
            get; set;
        }
        public double CenterY
        {
            get; set;
        }
    }

    public sealed class CompositeTransform : Transform
    {
        public double CenterX
        {
            get; set;
        }
        public double CenterY
        {
            get; set;
        }
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double SkewX
        {
            get; set;
        }
        public double SkewY
        {
            get; set;
        }
        public double Rotation
        {
            get; set;
        }
        public double TranslateX
        {
            get; set;
        }
        public double TranslateY
        {
            get; set;
        }
    }

    internal static class BrushFakeTransforms
    {
        internal static Matrix GetMatrix(Transform transform) => transform switch
        {
            MatrixTransform value => value.Matrix,
            TransformGroup value => value.Value,
            TranslateTransform value => Translation(value.X, value.Y),
            ScaleTransform value => Around(new Matrix(value.ScaleX, 0, 0, value.ScaleY, 0, 0), value.CenterX, value.CenterY),
            SkewTransform value => Around(Skew(value.AngleX, value.AngleY), value.CenterX, value.CenterY),
            RotateTransform value => Around(Rotation(value.Angle), value.CenterX, value.CenterY),
            CompositeTransform value => Around(new Matrix(value.ScaleX, 0, 0, value.ScaleY, 0, 0)
                .Append(Skew(value.SkewX, value.SkewY)).Append(Rotation(value.Rotation)), value.CenterX, value.CenterY)
                .Append(Translation(value.TranslateX, value.TranslateY)),
            _ => throw new NotSupportedException()
        };

        private static Matrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

        private static Matrix Skew(double x, double y) => new(1, Math.Tan(y * Math.PI / 180), Math.Tan(x * Math.PI / 180), 1, 0, 0);

        private static Matrix Rotation(double angle)
        {
            var radians = angle * Math.PI / 180;
            return new Matrix(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
        }

        private static Matrix Around(Matrix matrix, double x, double y) => Translation(-x, -y).Append(matrix).Append(Translation(x, y));
    }
}

namespace Windows.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage : Media.ImageSource
    {
        public BitmapImage()
        {
        }

        public BitmapImage(Uri uri) => UriSource = uri;

        public Uri? UriSource
        {
            get; set;
        }
    }

    public sealed class WriteableBitmap : Media.ImageSource;
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class RadialGradientBrush : Windows.UI.Xaml.Media.XamlCompositionBrushBase
    {
        public Windows.Foundation.Point Center { get; set; } = new(0.5, 0.5);
        public Windows.Foundation.Point GradientOrigin
        {
            get; set;
        }
        public double RadiusX { get; set; } = 0.5;
        public double RadiusY { get; set; } = 0.5;
        public Windows.UI.Xaml.Media.BrushMappingMode MappingMode { get; set; } = Windows.UI.Xaml.Media.BrushMappingMode.RelativeToBoundingBox;
        public Windows.UI.Xaml.Media.GradientSpreadMethod SpreadMethod
        {
            get; set;
        }
        public Windows.UI.Composition.CompositionColorSpace InterpolationSpace
        {
            get; set;
        }
        public IList<Windows.UI.Xaml.Media.GradientStop> GradientStops { get; } = new List<Windows.UI.Xaml.Media.GradientStop>();
    }
}
