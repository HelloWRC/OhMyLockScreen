using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

internal static class BrushSerializationTests
{
    private static readonly Color Blue = Color.FromArgb(128, 12, 34, 56);
    private static readonly Color Red = Color.FromArgb(255, 255, 0, 0);

    internal static void Run(Action<string, Action> run)
    {
        run("Brush properties and collection elements round trip without attributes before and after option rebuilds", () =>
        {
            var defaults = JsonSerializerOptions.Default;
            var config = new AppearanceConfig { Background = new SolidColorBrush(Blue) };
            config.Brushes.AddRange([.. Samples(), null]);
            VerifyConfig(config);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(AppearanceConfig).Assembly);
            VerifyConfig(config);
            Check(ReferenceEquals(defaults, JsonSerializerOptions.Default), "Global serializer defaults changed.");
        });

        run("Each concrete brush and Brush root uses the same converter and null handling", () =>
        {
            var solid = Copy(new SolidColorBrush(Blue));
            Check(solid.Color == Blue, "Concrete solid root lost its color.");
            var linear = Copy((LinearGradientBrush)Samples()[1]);
            Check(linear.StartPoint == new Point(-0.2, 0.3) && linear.EndPoint == new Point(1.4, 0.8)
                && linear.MappingMode == BrushMappingMode.Absolute && linear.SpreadMethod == GradientSpreadMethod.Reflect
                && linear.ColorInterpolationMode == ColorInterpolationMode.ScRgbLinearInterpolation, "Linear parameters changed.");
            VerifyStops(linear.GradientStops);
            var radial = Copy((RadialGradientBrush)Samples()[2]);
            Check(radial.Center == new Point(0.4, 0.6) && radial.GradientOrigin == new Point(-0.1, 0.2)
                && radial.RadiusX == 0.8 && radial.RadiusY == 0.3 && radial.MappingMode == BrushMappingMode.Absolute
                && radial.SpreadMethod == GradientSpreadMethod.Repeat && radial.InterpolationSpace == CompositionColorSpace.RgbLinear
                && radial.FallbackColor == Blue, "Radial parameters changed.");
            VerifyStops(radial.GradientStops);
            var acrylic = Copy((AcrylicBrush)Samples()[3]);
            Check(acrylic.BackgroundSource == AcrylicBackgroundSource.HostBackdrop && acrylic.TintColor == Blue
                && acrylic.TintOpacity == 0.4 && acrylic.TintLuminosityOpacity == 0.7 && acrylic.FallbackColor == Red
                && acrylic.AlwaysUseFallback && acrylic.TintTransitionDuration.Ticks == 1234567, "Acrylic parameters changed.");
            var image = Copy((ImageBrush)Samples()[4]);
            Check(image.ImageSource is BitmapImage { UriSource.AbsoluteUri: "ms-appx:///Assets/photo.png" }
                && image.Stretch == Stretch.UniformToFill && image.AlignmentX == AlignmentX.Right && image.AlignmentY == AlignmentY.Bottom,
                "Image parameters changed.");
            foreach (Brush brush in Samples())
            {
                Check(Copy(brush).GetType() == brush.GetType(), "Brush root changed type.");
            }

            Check(Copy<Brush?>(null) == null && Copy<SolidColorBrush?>(null) == null, "Null brush did not round trip.");
            Check(ConfigureFileHelper.CopyObject<Brush?>(null) == null, "Configuration copy replaced a null brush.");
            Check(Copy<GradientBrush>(linear) is LinearGradientBrush, "Intermediate base declaration failed.");
        });

        run("Default values, empty brushes and omitted fields retain framework defaults", () =>
        {
            Brush[] brushes = [new SolidColorBrush(), new LinearGradientBrush(), new RadialGradientBrush(), new AcrylicBrush(), new ImageBrush()];
            foreach (var brush in brushes)
            {
                Check(Json(brush) == Json(Copy(brush)), "Default values did not round trip.");
            }

            Check(Read<Brush>("""{"$type":"solid"}""") is SolidColorBrush { Opacity: 1 }, "Missing properties did not retain defaults.");
            Check(Read<Brush>("""{"$type":"linearGradient","GradientStops":[]}""") is LinearGradientBrush { GradientStops.Count: 0 }, "Empty stops changed.");
            Check(Read<Brush>("""{"$type":"solid","Color":"#0c2238"}""") is SolidColorBrush { Color.A: 255, Color.R: 12, Color.G: 34, Color.B: 56 }, "RGB color parsing failed.");
            Check(Json(Copy(new SolidColorBrush(Blue))).Contains("\"Color\":\"#800C2238\""), "ARGB output was not canonical.");
        });

        run("Configuration deep copies isolate brushes, gradient stops and transforms", () =>
        {
            var source = new AppearanceConfig { Background = Samples()[1] };
            source.Background.Transform = new MatrixTransform { Matrix = new Matrix(2, 3, 4, 5, 6, 7) };
            var copy = ConfigureFileHelper.CopyObject(source);
            var original = (LinearGradientBrush)source.Background;
            var cloned = (LinearGradientBrush)copy.Background!;
            cloned.GradientStops[0].Color = Red;
            ((MatrixTransform)cloned.Transform!).Matrix = Matrix.Identity;
            Check(!ReferenceEquals(original, cloned) && !ReferenceEquals(original.GradientStops, cloned.GradientStops)
                && !ReferenceEquals(original.GradientStops[0], cloned.GradientStops[0]) && original.GradientStops[0].Color == Blue,
                "Deep copy shares gradient state.");
            Check(((MatrixTransform)original.Transform!).Matrix.M12 == 3, "Deep copy shares transforms.");
        });

        run("All standard transforms persist matrices without altering the original transform tree", () =>
        {
            Transform[] transforms =
            [
                new MatrixTransform { Matrix = new Matrix(2, 3, 4, 5, 6, 7) },
                new TranslateTransform { X = 1e20, Y = -20 },
                new ScaleTransform { ScaleX = 2, ScaleY = 3, CenterX = 4, CenterY = 5 },
                new RotateTransform { Angle = 30, CenterX = 4, CenterY = 5 },
                new SkewTransform { AngleX = 10, AngleY = 20, CenterX = 4, CenterY = 5 },
                new CompositeTransform { CenterX = 4, CenterY = 5, ScaleX = 2, ScaleY = 3, SkewX = 10, SkewY = 20, Rotation = 30, TranslateX = 1e20, TranslateY = 7 },
                new TransformGroup { Children = { new ScaleTransform { ScaleX = 2 }, new TranslateTransform { X = 10 } } }
            ];
            foreach (var transform in transforms)
            {
                var brush = new SolidColorBrush(Blue) { Transform = transform, RelativeTransform = transform, Opacity = 0.3 };
                var copied = Copy(brush);
                var expected = BrushFakeTransforms.GetMatrix(transform);
                Check(copied.Transform is MatrixTransform { Matrix: var matrix } && matrix == expected
                    && copied.RelativeTransform is MatrixTransform { Matrix: var relative } && relative == expected
                    && copied.Opacity == 0.3, "Matrix snapshot changed static effects.");
                Check(ReferenceEquals(brush.Transform, transform) && ReferenceEquals(brush.RelativeTransform, transform), "Serialization replaced source transforms.");
            }

            var group = (TransformGroup)transforms[^1];
            Check(group.Children.Count == 2 && group.Value.M11 == 2 && group.Value.OffsetX == 10, "Source transform group was modified.");
        });

        run("Invalid brush JSON is rejected with field diagnostics", () =>
        {
            string[] invalid =
            [
                "[]", "42", "\"red\"", "{}", """{"$type":null}""", """{"$type":"unknown"}""",
                """{"$type":"solid","Color":"red"}""", """{"$type":"solid","Color":"#GG1234"}""",
                """{"$type":"solid","Color":null}""", """{"$type":"solid","Colour":"#FFFFFF"}""",
                """{"$type":"solid","Opacity":2}""", """{"$type":"solid","Opacity":"0.5"}""",
                """{"$type":"solid","Opacity":1e400}""", """{"$type":"solid","Opacity":null}""",
                """{"$type":"solid","Color":"#FFFFFF","Color":"#000000"}""",
                """{"$type":"linearGradient","StartPoint":{"X":0,"Y":1,"Z":2}}""",
                """{"$type":"linearGradient","StartPoint":{"X":0}}""",
                """{"$type":"linearGradient","SpreadMethod":"0"}""",
                """{"$type":"linearGradient","SpreadMethod":0}""",
                """{"$type":"linearGradient","SpreadMethod":"pad"}""",
                """{"$type":"linearGradient","MappingMode":"Unknown"}""",
                """{"$type":"linearGradient","GradientStops":{}}""",
                """{"$type":"linearGradient","GradientStops":[{"Color":"#FFFFFF","Offset":"bad"}]}""",
                """{"$type":"radialGradient","RadiusX":-1}""",
                """{"$type":"acrylic","TintOpacity":-0.1}""",
                """{"$type":"acrylic","TintLuminosityOpacity":2}""",
                """{"$type":"acrylic","AlwaysUseFallback":1}""",
                """{"$type":"acrylic","TintTransitionDuration":"-00:00:01"}""",
                """{"$type":"acrylic","TintTransitionDuration":"tomorrow"}""",
                """{"$type":"image","ImageSource":"Assets/photo.png"}""",
                """{"$type":"solid","Transform":{"M11":1}}"""
            ];
            foreach (var json in invalid)
            {
                var exception = Throws<JsonException>(() => Read<Brush>(json));
                Check(exception.Message.Contains("画刷字段"), "Failure did not identify the brush field.");
            }

            Throws<JsonException>(() => Read<SolidColorBrush>("""{"$type":"image"}"""));
            Throws<JsonException>(() => Read<CustomBrush>("""{"$type":"solid"}"""));
        });

        run("Unsupported brush state and invalid output cannot silently lose values", () =>
        {
            Throws<NotSupportedException>(() => Json<Brush>(new CustomBrush()));
            Throws<NotSupportedException>(() => Json(new CustomBrush()));
            Throws<NotSupportedException>(() => Json(new ImageBrush { ImageSource = new BitmapImage() }));
            Throws<NotSupportedException>(() => Json(new ImageBrush { ImageSource = new WriteableBitmap() }));
            Throws<NotSupportedException>(() => Json(new SolidColorBrush { Transform = new Transform() }));
            Throws<JsonException>(() => Json(new SolidColorBrush { Opacity = double.NaN }));
            Throws<JsonException>(() => Json(new LinearGradientBrush { SpreadMethod = (GradientSpreadMethod)99 }));
            Throws<JsonException>(() => Json(new SolidColorBrush { Transform = new MatrixTransform { Matrix = new Matrix(1, 0, 0, 1, double.PositiveInfinity, 0) } }));
        });

        run("Old systems never access the unavailable acrylic luminosity API", () =>
        {
            ApiInformation.HasTintLuminosityOpacity = false;
            try
            {
                Check(Copy(new AcrylicBrush()).TintOpacity == 0.5, "Acrylic fallback round trip failed.");
                Check(Read<AcrylicBrush>("""{"$type":"acrylic","TintLuminosityOpacity":null}""") != null, "Null luminosity was rejected.");
                Throws<NotSupportedException>(() => Read<AcrylicBrush>("""{"$type":"acrylic","TintLuminosityOpacity":0.5}"""));
            }
            finally
            {
                ApiInformation.HasTintLuminosityOpacity = true;
            }
        });

        run("Brush JSON failures use existing file backup recovery", () =>
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Brushes.json");
            ConfigureFileHelper.SaveConfig(path, new AppearanceConfig { Background = new SolidColorBrush(Blue) }, true);
            ConfigureFileHelper.LoadConfigUnWrapped<AppearanceConfig>(path);
            var backup = File.ReadAllText(path + ".bak");
            File.WriteAllText(path, """{"Background":{"$type":"unknown"}}""");
            var loaded = ConfigureFileHelper.LoadConfigUnWrapped<AppearanceConfig>(path);
            Check(loaded.Background is SolidColorBrush { Color: var color } && color == Blue
                && File.ReadAllText(path) == backup && ConfigureFileHelper.Errors is [{ Critical: false }], "Valid backup was not restored.");
            File.WriteAllText(path, """{"Background":{"$type":"solid","Color":"bad"}}""");
            Throws<JsonException>(() => ConfigureFileHelper.LoadConfigUnWrapped<AppearanceConfig>(path, false));
        });

        run("Replacing a model brush triggers existing graph saves without per-property converters", () =>
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Brushes.json");
            var model = new AppearanceConfig();
            var saves = 0;
            using var graph = new ObservableSettingsGraph(model, (_, _, _) =>
            {
                saves++;
                ConfigureFileHelper.SaveConfig(path, model);
            });
            model.Background = new SolidColorBrush(Blue);
            Check(saves == 1 && ConfigureFileHelper.LoadConfigUnWrapped<AppearanceConfig>(path, false).Background is SolidColorBrush { Color: var color } && color == Blue,
                "Model assignment was not saved automatically.");
            ((SolidColorBrush)model.Background).Color = Red;
            Check(saves == 1, "In-place brush editing unexpectedly changed graph behavior.");
            model.Background = new LinearGradientBrush();
            Check(saves == 2 && ConfigureFileHelper.LoadConfigUnWrapped<AppearanceConfig>(path, false).Background is LinearGradientBrush, "Brush type replacement was not saved.");
        });

        run("Independent JsonSerializer callers can opt into the brush converter factory", () =>
        {
            var options = new JsonSerializerOptions { Converters = { new BrushJsonConverterFactory() } };
            var json = JsonSerializer.Serialize<Brush>(new SolidColorBrush(Blue), options);
            Check(JsonSerializer.Deserialize<Brush>(json, options) is SolidColorBrush { Color: var color } && color == Blue, "Independent registration failed.");
            Throws<ArgumentException>(() => new BrushJsonConverterFactory().CreateConverter(typeof(string), options));
        });
    }

    private static Brush[] Samples() =>
    [
        new SolidColorBrush(Blue),
        new LinearGradientBrush
        {
            StartPoint = new Point(-0.2, 0.3),
            EndPoint = new Point(1.4, 0.8),
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Reflect,
            ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
            GradientStops =
            {
                new GradientStop { Color = Blue, Offset = 0.8 },
                new GradientStop { Color = Red, Offset = 0.2 },
                new GradientStop { Color = Blue, Offset = 0.2 }
            }
        },
        new RadialGradientBrush
        {
            Center = new Point(0.4, 0.6),
            GradientOrigin = new Point(-0.1, 0.2),
            RadiusX = 0.8,
            RadiusY = 0.3,
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Repeat,
            InterpolationSpace = CompositionColorSpace.RgbLinear,
            FallbackColor = Blue,
            GradientStops =
            {
                new GradientStop { Color = Blue, Offset = 0.8 },
                new GradientStop { Color = Red, Offset = 0.2 },
                new GradientStop { Color = Blue, Offset = 0.2 }
            }
        },
        new AcrylicBrush
        {
            BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
            TintColor = Blue,
            TintOpacity = 0.4,
            TintLuminosityOpacity = 0.7,
            FallbackColor = Red,
            AlwaysUseFallback = true,
            TintTransitionDuration = TimeSpan.FromTicks(1234567)
        },
        new ImageBrush
        {
            ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/photo.png")),
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Right,
            AlignmentY = AlignmentY.Bottom
        }
    ];

    private static void VerifyConfig(AppearanceConfig config)
    {
        foreach (var indented in new[] { false, true })
        {
            using var stream = new MemoryStream();
            ConfigureFileHelper.SaveConfig(stream, config, indented);
            stream.Position = 0;
            var copy = ConfigureFileHelper.LoadConfigUnWrapped<AppearanceConfig>(stream);
            Check(copy.Background is SolidColorBrush { Color: var color } && color == Blue && copy.Brushes.Count == 6
                && copy.Brushes[^1] == null, "Brush configuration did not round trip.");
            for (var index = 0; index < 5; index++)
            {
                Check(copy.Brushes[index]!.GetType() == config.Brushes[index]!.GetType(), "Collection brush type changed.");
            }
        }
    }

    private static void VerifyStops(IList<GradientStop> stops)
    {
        Check(stops.Count == 3 && stops[0].Color == Blue && stops[0].Offset == 0.8
            && stops[1].Color == Red && stops[1].Offset == 0.2 && stops[2].Color == Blue && stops[2].Offset == 0.2,
            "Stop order or duplicate offsets changed.");
    }

    private static string Json<T>(T value)
    {
        using var stream = new MemoryStream();
        ConfigureFileHelper.SaveConfig(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static T Copy<T>(T value) => Read<T>(Json(value));

    private static T Read<T>(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ConfigureFileHelper.LoadConfigUnWrapped<T>(stream);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public sealed class CustomBrush : Brush;
}

internal partial class AppearanceConfig : ObservableObject
{
    [ObservableProperty]
    private Brush? _background;

    public List<Brush?> Brushes { get; set; } = [];
}
