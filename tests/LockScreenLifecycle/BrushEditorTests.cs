using OhMyLockScreen.Shared.Helpers.UI;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;
using System.Text.Json;
using OhMyLockScreen.Shared.Helpers.Serialization;

internal static class BrushEditorTests
{
    internal static async Task RunImportAsync(Func<string, Func<Task>, Task> run)
    {
        await run("Image import commits a validated persistent URI and retains copied bytes", async () =>
        {
            var directory = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "BrushAssets");
            Uri? published = null;
            var bytes = new byte[] { 10, 20, 30 };
            var result = await BrushImageImport.RunAsync(
                () => Task.FromResult<Stream>(new MemoryStream(bytes)), ".PNG", directory,
                path =>
                {
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Validation received incorrect bytes.");
                    return Task.CompletedTask;
                },
                () => true, uri => published = uri);
            Check(result && published is { Scheme: "ms-appdata" } && published.AbsolutePath.StartsWith("/local/BrushAssets/"), "Persistent URI was not published.");
            Check(Directory.GetFiles(directory).Length == 1 && File.ReadAllBytes(Directory.GetFiles(directory)[0]).SequenceEqual(bytes), "Committed file was lost.");
        });

        await run("Image import cancellation, validation failure and superseded requests preserve the current brush", async () =>
        {
            var directory = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "BrushAssets");
            var published = false;
            var opened = false;
            Task<Stream> Open()
            {
                opened = true;
                return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
            }
            var canceled = await BrushImageImport.RunAsync(Open, ".png", directory, _ => Task.CompletedTask, () => false, _ => published = true);
            Check(!canceled && !opened && !Directory.Exists(directory), "Canceled selection imported a file.");
            try
            {
                await BrushImageImport.RunAsync(Open, ".png", directory, _ => throw new InvalidDataException("invalid image"), () => true, _ => published = true);
                throw new Exception("Decode failure was ignored.");
            }
            catch (InvalidDataException)
            {
            }
            Check(!published && Directory.GetFiles(directory).Length == 0, "Failed image import published or leaked a file.");
            var current = true;
            var stale = await BrushImageImport.RunAsync(Open, ".png", directory, _ =>
            {
                current = false;
                return Task.CompletedTask;
            }, () => current, _ => published = true);
            Check(!stale && !published && Directory.GetFiles(directory).Length == 0, "Superseded image import published or leaked a file.");
        });
    }

    internal static void Run(Action<string, Action> run)
    {
        run("Gradient dragging rounds stop percentages without changing non-drag edits", () =>
        {
            Check(BrushEditorOperations.RoundDraggedOffset(0.3636) == 0.364,
                "A dragged 36.36% offset was not rounded to 36.4%.");
            Check(BrushEditorOperations.RoundDraggedOffset(0.3635) == 0.364,
                "Percentage midpoint rounding was not away from zero.");
            Check(BrushEditorOperations.RoundDraggedOffset(-0.1) == 0 && BrushEditorOperations.RoundDraggedOffset(1.1) == 1,
                "Dragged offsets escaped the valid range.");
            var source = BrushEditorOperations.Create(BrushEditorKind.Linear)!;
            var edited = BrushEditorOperations.SetOffset(source, 0, 0.3636);
            Check(BrushEditorOperations.Stops(edited)![0].Offset == 0.3636,
                "The drag precision policy unexpectedly changed non-drag edits.");
        });

        run("Brush editor recognizes all six states and preserves unsupported brushes", () =>
        {
            foreach (var kind in Enum.GetValues<BrushEditorKind>().Where(kind => kind != BrushEditorKind.Unsupported))
            {
                var brush = BrushEditorOperations.Create(kind, 0.42);
                Check(BrushEditorOperations.Kind(brush) == kind, $"Wrong kind for {kind}.");
                Check(brush == null || brush.Opacity == 0.42, "Type switching lost opacity.");
            }
            var custom = new CustomBrush();
            Check(BrushEditorOperations.Kind(custom) == BrushEditorKind.Unsupported, "Custom brush was misidentified.");
            try
            {
                BrushEditorOperations.Copy(custom);
                throw new Exception("Custom brush was silently converted.");
            }
            catch (NotSupportedException)
            {
            }
        });

        run("Brush edits isolate stops and transforms while retaining unedited properties", () =>
        {
            var source = (LinearGradientBrush)BrushEditorOperations.Create(BrushEditorKind.Linear)!;
            source.Transform = new TranslateTransform { X = 12, Y = 24 };
            source.RelativeTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 2 };
            source.MappingMode = BrushMappingMode.Absolute;
            source.SpreadMethod = GradientSpreadMethod.Reflect;
            source.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
            var edited = (LinearGradientBrush)BrushEditorOperations.Edit(source, copy => copy.Opacity = 0.25);
            Check(!ReferenceEquals(source, edited) && source.Opacity == 1 && edited.Opacity == 0.25, "Original brush changed.");
            Check(edited.MappingMode == source.MappingMode && edited.SpreadMethod == source.SpreadMethod && edited.ColorInterpolationMode == source.ColorInterpolationMode, "Hidden gradient properties were lost.");
            Check(!ReferenceEquals(source.GradientStops[0], edited.GradientStops[0]), "Stops were shared.");
            edited.GradientStops[0].Offset = 0.3;
            Check(source.GradientStops[0].Offset == 0, "Editing a cloned stop changed the source.");
            Check(edited.Transform is MatrixTransform { Matrix.OffsetX: 12, Matrix.OffsetY: 24 }, "Transform snapshot is wrong.");
            Check(!ReferenceEquals(edited.Transform, source.Transform) && !ReferenceEquals(edited.RelativeTransform, source.RelativeTransform), "Transforms were shared.");
        });

        run("Brush editor preserves radial, image and acrylic state including old-system guards", () =>
        {
            var radial = new RadialGradientBrush
            {
                Center = new(0.3, 0.4),
                GradientOrigin = new(0.2, 0.1),
                RadiusX = 0.7,
                RadiusY = 0.8,
                FallbackColor = Color.FromArgb(23, 45, 67, 89),
                InterpolationSpace = Windows.UI.Composition.CompositionColorSpace.Hsl
            };
            var radialCopy = (RadialGradientBrush)BrushEditorOperations.Copy(radial);
            Check(radialCopy.Center == radial.Center && radialCopy.GradientOrigin == radial.GradientOrigin && radialCopy.RadiusX == radial.RadiusX && radialCopy.RadiusY == radial.RadiusY && radialCopy.FallbackColor == radial.FallbackColor && radialCopy.InterpolationSpace == radial.InterpolationSpace, "Radial settings changed.");
            var image = new ImageBrush { ImageSource = new WriteableBitmap(), AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Bottom, Stretch = Stretch.None };
            var imageCopy = (ImageBrush)BrushEditorOperations.Copy(image);
            Check(ReferenceEquals(image.ImageSource, imageCopy.ImageSource) && imageCopy.AlignmentX == image.AlignmentX && imageCopy.AlignmentY == image.AlignmentY && imageCopy.Stretch == image.Stretch, "An in-memory image was lost or image settings changed.");
            var acrylic = new AcrylicBrush { BackgroundSource = AcrylicBackgroundSource.HostBackdrop, TintColor = radial.FallbackColor, TintOpacity = 0.3, TintLuminosityOpacity = 0.4, AlwaysUseFallback = true, TintTransitionDuration = TimeSpan.FromSeconds(2) };
            var acrylicCopy = (AcrylicBrush)BrushEditorOperations.Copy(acrylic);
            Check(acrylicCopy.BackgroundSource == acrylic.BackgroundSource && acrylicCopy.TintColor == acrylic.TintColor && acrylicCopy.TintOpacity == acrylic.TintOpacity && acrylicCopy.TintLuminosityOpacity == 0.4 && acrylicCopy.AlwaysUseFallback && acrylicCopy.TintTransitionDuration == acrylic.TintTransitionDuration, "Acrylic properties changed.");
            ApiInformation.HasTintLuminosityOpacity = false;
            try
            {
                Check(BrushEditorOperations.Copy(new AcrylicBrush()) is AcrylicBrush, "Old-system acrylic copy failed.");
            }
            finally
            {
                ApiInformation.HasTintLuminosityOpacity = true;
            }
        });

        run("Gradient operations preserve selection indices, alpha and empty collections", () =>
        {
            var gradient = (LinearGradientBrush)BrushEditorOperations.Create(BrushEditorKind.Linear)!;
            gradient.GradientStops[0].Color = Color.FromArgb(0, 0, 0, 0);
            var added = (LinearGradientBrush)BrushEditorOperations.AddStop(gradient, 0.5);
            Check(added.GradientStops.Count == 3 && added.GradientStops[2].Color == Color.FromArgb(128, 128, 128, 128), "New stop interpolation is wrong.");
            var moved = (LinearGradientBrush)BrushEditorOperations.SetOffset(added, 2, 3);
            Check(moved.GradientStops[2].Offset == 1 && added.GradientStops[2].Offset == 0.5, "Offset was not clamped or source was changed.");
            var reversed = (LinearGradientBrush)BrushEditorOperations.Reverse(added);
            Check(reversed.GradientStops[0].Offset == 1 && reversed.GradientStops[1].Offset == 0 && reversed.GradientStops[2].Color == added.GradientStops[2].Color, "Reverse broke stop identity or color.");
            Brush empty = new LinearGradientBrush();
            empty = BrushEditorOperations.AddStop(empty, 0.2);
            Check(BrushEditorOperations.Stops(empty)![0].Color == BrushEditorOperations.White, "Empty gradient could not acquire its first stop.");
            empty = BrushEditorOperations.RemoveStop(empty, 0);
            Check(BrushEditorOperations.Stops(empty)!.Count == 0 && BrushEditorOperations.Stops(BrushEditorOperations.Reverse(empty))!.Count == 0, "Empty gradient operations failed.");
        });

        run("Composition brush editing and deserialization never assign unsupported null transforms", () =>
        {
            Brush.RejectCompositionTransforms = true;
            try
            {
                var options = new JsonSerializerOptions();
                options.Converters.Add(new BrushJsonConverterFactory());
                foreach (var source in new Brush[] { new AcrylicBrush(), new RadialGradientBrush() })
                {
                    var edited = BrushEditorOperations.Edit(source, copy => copy.Opacity = 0.3);
                    Check(edited.GetType() == source.GetType() && edited.Opacity == 0.3, "Composition brush edit failed.");
                    var restored = JsonSerializer.Deserialize<Brush>(JsonSerializer.Serialize(source, options), options);
                    Check(restored?.GetType() == source.GetType(), "Composition brush round trip assigned unsupported transforms.");
                }
                var legacy = JsonSerializer.Deserialize<Brush>("""{"$type":"acrylic","Transform":{"M11":1,"M12":0,"M21":0,"M22":1,"OffsetX":0,"OffsetY":0}}""", options);
                Check(legacy is AcrylicBrush, "Legacy identity transforms were not accepted.");
            }
            finally
            {
                Brush.RejectCompositionTransforms = false;
            }
        });

        run("Gradient coordinate maps round trip relative and absolute positions with transforms", () =>
        {
            var gradient = new LinearGradientBrush
            {
                RelativeTransform = new TranslateTransform { X = 0.1, Y = 0.2 },
                Transform = new TranslateTransform { X = 5, Y = 7 }
            };
            var map = BrushEditorMatrix.ForBrush(gradient, 200, 100);
            var point = map.Map(new Point(0.5, 0.5));
            Near(point, new Point(125, 77));
            Check(map.TryInverse(out var inverse), "Valid transform was considered singular.");
            Near(inverse.Map(point), new Point(0.5, 0.5));
            gradient.MappingMode = BrushMappingMode.Absolute;
            var absolute = BrushEditorMatrix.ForBrush(gradient, 200, 100);
            Near(absolute.Map(new Point(100, 50)), point);
            var target = new BrushEditorMatrix(0, 2, -2, 0, 300, 100);
            var composed = absolute.Then(target);
            Check(composed.TryInverse(out inverse), "Rotated target was considered singular.");
            Near(inverse.Map(composed.Map(new Point(67, 89))), new Point(67, 89));
            Check(!BrushEditorMatrix.ForBrush(gradient, 0, 100).TryInverse(out _), "Zero-sized target was accepted.");
            gradient.Transform = new ScaleTransform { ScaleX = 0 };
            Check(!BrushEditorMatrix.ForBrush(gradient, 200, 100).TryInverse(out _), "Singular brush transform was accepted.");
            Check(BrushEditorMatrix.Project(new Point(15, 10), new Point(0, 0), new Point(20, 0)) == 0.75, "Stop projection is wrong.");
            Check(BrushEditorMatrix.Project(new Point(1, 2), new Point(), new Point()) == 0, "Collapsed gradient axis failed.");
        });
    }

    private static void Near(Point actual, Point expected) => Check(Math.Abs(actual.X - expected.X) < 0.000001 && Math.Abs(actual.Y - expected.Y) < 0.000001, $"Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
    private sealed class CustomBrush : Brush;
}
