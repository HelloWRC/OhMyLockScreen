using System.ComponentModel;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.Serialization;
using Muxc = Microsoft.UI.Xaml.Controls;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

namespace BrushEditorPreview;

public sealed partial class App : Application
{
    private readonly TextBlock _status = new() { Text = "正在验证…", Margin = new Thickness(12) };
    private readonly StackPanel _examples = new() { Orientation = Orientation.Horizontal, Spacing = 20, Padding = new Thickness(12) };
    private readonly List<(string Name, BrushEditControl Editor, BrushSource Source, StackPanel Panel)> _cases = [];
    private readonly List<string> _report = [];

    public App()
    {
        InitializeComponent();
        UnhandledException += async (_, args) =>
        {
            args.Handled = true;
            _status.Text = "FAIL: " + args.Exception;
            await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting), _status.Text);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ApplicationView.PreferredLaunchViewSize = new Size(2080, 1020);
        ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
        ApplicationView.GetForCurrentView().Title = "Brush Editor Preview";
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        var theme = new Button { Content = "切换主题", Margin = new Thickness(8) };
        theme.Click += (_, _) => root.RequestedTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        toolbar.Children.Add(theme);
        toolbar.Children.Add(_status);
        root.Children.Add(toolbar);
        var scroll = new ScrollViewer { Content = _examples, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Add("Null", null);
        Add("Solid", new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)));
        Add("Linear", new LinearGradientBrush { StartPoint = new(0.1, 0.2), EndPoint = new(0.9, 0.8), GradientStops = { new GradientStop { Color = Color.FromArgb(255, 255, 220, 0), Offset = 0 }, new GradientStop { Color = Color.FromArgb(255, 200, 0, 255), Offset = 1 } } });
        Add("Radial", new RadialGradientBrush { Center = new(0.5, 0.5), GradientOrigin = new(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5, GradientStops = { new GradientStop { Color = Color.FromArgb(255, 255, 220, 0), Offset = 0 }, new GradientStop { Color = Color.FromArgb(255, 200, 0, 255), Offset = 1 } } });
        Add("Image", new ImageBrush { ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/Square150x150Logo.png")), Stretch = Stretch.UniformToFill });
        Add("Acrylic", new AcrylicBrush { TintColor = Color.FromArgb(255, 255, 255, 255), FallbackColor = Color.FromArgb(255, 255, 255, 255), TintOpacity = 1 });
        Window.Current.Content = root;
        Window.Current.Activate();
        root.Loaded += async (_, _) => await VerifyAndCapture(root);
    }

    private void Add(string name, Brush? brush)
    {
        var source = new BrushSource { Value = brush };
        var editor = new BrushEditControl { Width = 325 };
        editor.SetBinding(BrushEditControl.BrushProperty, new Binding { Source = source, Path = new PropertyPath(nameof(BrushSource.Value)), Mode = BindingMode.TwoWay });
        var panel = new StackPanel { Width = 325, Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = name, FontSize = 18 });
        panel.Children.Add(editor);
        if (brush is LinearGradientBrush or RadialGradientBrush)
        {
            var target = new Rectangle { Width = 309, Height = 160, HorizontalAlignment = HorizontalAlignment.Center };
            target.SetBinding(Shape.FillProperty, new Binding { Source = source, Path = new PropertyPath(nameof(BrushSource.Value)) });
            panel.Children.Add(target);
            editor.TargetElement = target;
        }
        _examples.Children.Add(panel);
        _cases.Add((name, editor, source, panel));
    }

    private async Task VerifyAndCapture(Grid root)
    {
        try
        {
            await Task.Delay(500);
            Check(_cases.Skip(1).All(item => item.Source.Value != null), "loading all brush types preserves the bound values");
            var empty = _cases[0];
            var unsupported = new UnsupportedBrush();
            empty.Source.Value = unsupported;
            Check(ReferenceEquals(empty.Editor.Brush, unsupported) && ((TextBlock)empty.Editor.FindName("ErrorText")).Visibility == Visibility.Visible,
                "unsupported brushes remain intact");
            ((CommunityToolkit.WinUI.Controls.Segmented)empty.Editor.FindName("TypeSelector")).SelectedIndex = 1;
            Check(empty.Source.Value is SolidColorBrush, "explicit type selection replaces an unsupported brush");
            ((CommunityToolkit.WinUI.Controls.Segmented)empty.Editor.FindName("TypeSelector")).SelectedIndex = 0;
            Check(empty.Source.Value == null, "selecting the null type writes null through TwoWay binding");
            var test = _cases[1];
            test.Source.Value = null;
            Check(test.Editor.Brush == null, "external null propagates");
            var white = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            test.Editor.Brush = white;
            Check(ReferenceEquals(test.Source.Value, white), "editor writes through native TwoWay binding");
            var initial = new SolidColorBrush(Color.FromArgb(100, 1, 2, 3));
            test.Source.Value = initial;
            Check(ReferenceEquals(test.Editor.Brush, initial), "native binding remains attached after editor write");
            var picker = (Muxc.ColorPicker)test.Editor.FindName("Picker");
            var red = Color.FromArgb(120, 255, 0, 0);
            picker.Color = red;
            Check(test.Source.Value is SolidColorBrush edited && !ReferenceEquals(edited, initial) && edited.Color == red && initial.Color.A == 100, "ColorPicker edits replace the brush and preserve original alpha");
            var saved = (SolidColorBrush)test.Source.Value!;
            saved.Color = Color.FromArgb(222, 0, 255, 0);
            Check(picker.Color == saved.Color, "in-place external color changes refresh the editor");
            var opacity = (Muxc.NumberBox)test.Editor.FindName("OpacityInput");
            opacity.Value = 0.42;
            Check(test.Source.Value is SolidColorBrush solid && Math.Abs(solid.Opacity - 0.42) < 0.00001 && solid.Color.A == 222,
                $"opacity and color alpha stay independent (opacity={test.Source.Value?.Opacity}, alpha={(test.Source.Value as SolidColorBrush)?.Color.A})");
            Check(opacity.Text.Contains('%'), "native opacity input displays percentages");
            var gradient = _cases[2];
            var original = gradient.Source.Value;
            ((Muxc.NumberBox)gradient.Editor.FindName("OffsetInput")).Value = 0.25;
            Check(gradient.Source.Value is LinearGradientBrush linear && Math.Abs(linear.GradientStops[0].Offset - 0.25) < 0.00001 && !ReferenceEquals(original, linear),
                $"gradient offset edits publish a new brush (type={gradient.Source.Value?.GetType().Name}, offset={(gradient.Source.Value as LinearGradientBrush)?.GradientStops[0].Offset}, same={ReferenceEquals(original, gradient.Source.Value)}, error={((TextBlock)gradient.Editor.FindName("ErrorText")).Text})");
            var liveGradient = (LinearGradientBrush)gradient.Source.Value!;
            liveGradient.GradientStops.Add(new GradientStop { Offset = 0.5, Color = red });
            await Task.Delay(250);
            Check(((Canvas)gradient.Editor.FindName("StopHandles")).Children.Count == 3, "external stop additions are observed");
            var reverse = (Button)gradient.Editor.FindName("ReverseButton");
            ((IInvokeProvider)new ButtonAutomationPeer(reverse).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(50);
            Check(gradient.Source.Value is LinearGradientBrush reversed && Math.Abs(reversed.GradientStops[0].Offset - 0.75) < 0.00001,
                "native reverse button retains selected stop identity");
            var image = _cases[4];
            ((ComboBox)image.Editor.FindName("StretchSelector")).SelectedIndex = (int)Stretch.Uniform;
            Check(image.Source.Value is ImageBrush { Stretch: Stretch.Uniform, ImageSource: not null }, "image stretch preserves the image source");
            var acrylic = _cases[5];
            ((CheckBox)acrylic.Editor.FindName("FallbackCheckBox")).IsChecked = true;
            await Task.Delay(50);
            Check(acrylic.Source.Value is AcrylicBrush { AlwaysUseFallback: true },
                $"acrylic fallback checkbox writes through binding (type={acrylic.Source.Value?.GetType().Name}, checked={((CheckBox)acrylic.Editor.FindName("FallbackCheckBox")).IsChecked}, fallback={(acrylic.Source.Value as AcrylicBrush)?.AlwaysUseFallback}, error={((TextBlock)acrylic.Editor.FindName("ErrorText")).Text})");
            var radial = _cases[3];
            ((Muxc.NumberBox)radial.Editor.FindName("OffsetInput")).Value = 0.3;
            Check(radial.Source.Value is RadialGradientBrush radialBrush && Math.Abs(radialBrush.GradientStops[0].Offset - 0.3) < 0.00001,
                "radial color-stop editing supports native composition brushes");
            var options = new JsonSerializerOptions();
            options.Converters.Add(new BrushJsonConverterFactory());
            foreach (var sample in new[] { radial, acrylic })
            {
                var json = JsonSerializer.Serialize(sample.Source.Value, options);
                var restored = JsonSerializer.Deserialize<Brush>(json, options);
                Check(restored?.GetType() == sample.Source.Value?.GetType(), $"native {sample.Name} serialization round trip");
            }
            var lastColor = picker.Color;
            test.Panel.Children.Remove(test.Editor);
            await Task.Delay(50);
            ((SolidColorBrush)test.Source.Value!).Color = red;
            Check(picker.Color == lastColor, "unload releases brush subscriptions");
            test.Panel.Children.Insert(1, test.Editor);
            await Task.Delay(50);
            Check(picker.Color == red, "reload refreshes external values");

            test.Source.Value = white;
            gradient.Source.Value = original;
            await VerifyStopSelectionAsync(gradient.Name, gradient.Editor, gradient.Source);
            await VerifyStopSelectionAsync(radial.Name, radial.Editor, radial.Source);
            root.UpdateLayout();
            Check(Math.Abs(empty.Editor.ActualHeight - 56) < 1, $"null layout matches the 56 DIP design (height={empty.Editor.ActualHeight})");
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                root.RequestedTheme = theme;
                root.UpdateLayout();
                await Task.Delay(250);
                foreach (var item in _cases)
                {
                    await Capture(item.Editor, $"{item.Name}-{theme}.png");
                }
            }
            root.RequestedTheme = ElementTheme.Dark;
            gradient.Editor.TargetElement!.StartBringIntoView();
            ((Muxc.NumberBox)gradient.Editor.FindName("OffsetInput")).Focus(FocusState.Programmatic);
            await Task.Delay(250);
            var popup = VisualTreeHelper.GetOpenPopups(Window.Current).FirstOrDefault(item => item.Child is Canvas canvas && canvas.Children.OfType<Button>().Any(button => Equals(button.Tag, "start")));
            Check(popup?.IsOpen == true, "focusing a gradient editor opens its adorner");
            if (popup?.Child is Canvas surface)
            {
                var handle = surface.Children.OfType<Button>().First(button => Equals(button.Tag, "start"));
                var expected = gradient.Editor.TargetElement.TransformToVisual(root).TransformPoint(new Point(309 * 0.1, 160 * 0.2));
                Check(Math.Abs(Canvas.GetLeft(handle) + 14 - expected.X) < 1 && Math.Abs(Canvas.GetTop(handle) + 14 - expected.Y) < 1,
                    "native adorner handles align with target coordinates");
                handle.Focus(FocusState.Programmatic);
                await Task.Delay(150);
                Check(popup.IsOpen, "focus can move from editor to adorner without closing it");
                await VerifyAdornerEditsAsync(gradient.Name, gradient.Editor, gradient.Source, surface);
                gradient.Editor.TargetElement.RenderTransform = new ScaleTransform { ScaleX = 0, ScaleY = 1 };
                await Task.Delay(150);
                Check(!popup.IsOpen, "singular target transforms hide the adorner");
                gradient.Editor.TargetElement.RenderTransform = null;
                ((Muxc.NumberBox)gradient.Editor.FindName("OffsetInput")).Focus(FocusState.Programmatic);
                await Task.Delay(150);
                Check(popup.IsOpen, "restoring a valid target restores the adorner");
                ((Button)((StackPanel)root.Children[0]).Children[0]).Focus(FocusState.Programmatic);
                await Task.Delay(150);
                Check(!popup.IsOpen, "leaving editor and adorner hides the popup");
                await VerifyTargetReplacementAsync(gradient.Editor, popup);
            }
            radial.Editor.TargetElement!.StartBringIntoView();
            ((Muxc.NumberBox)radial.Editor.FindName("OffsetInput")).Focus(FocusState.Programmatic);
            await Task.Delay(200);
            var radialPopup = VisualTreeHelper.GetOpenPopups(Window.Current).FirstOrDefault(item => item.Child is Canvas canvas
                && canvas.Children.OfType<Button>().Any(button => Equals(button.Tag, "radiusX")));
            Check(radialPopup?.IsOpen == true, "radial gradient editor opens its adorner");
            if (radialPopup?.Child is Canvas radialSurface)
            {
                await VerifyAdornerEditsAsync(radial.Name, radial.Editor, radial.Source, radialSurface);
            }
            _status.Text = $"{(_report.Any(line => line.StartsWith("FAIL:")) ? "FAIL" : "PASS")}: {_report.Count} native checks；已导出六种状态的深浅色截图。";
            await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting), string.Join(Environment.NewLine, _report));
        }
        catch (Exception exception)
        {
            _status.Text = "FAIL: " + exception;
            await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting), string.Join(Environment.NewLine, _report) + Environment.NewLine + _status.Text);
        }
    }

    private void Check(bool condition, string description)
    {
        _report.Add((condition ? "PASS: " : "FAIL: ") + description);
    }

    private async Task VerifyTargetReplacementAsync(BrushEditControl editor, Popup previousPopup)
    {
        var target = editor.TargetElement!;
        var panel = (Panel)VisualTreeHelper.GetParent(target);
        var replacement = new Rectangle { Width = target.Width, Height = target.Height };
        panel.Children.Add(replacement);

        ((Muxc.NumberBox)editor.FindName("OffsetInput")).Focus(FocusState.Programmatic);
        await Task.Delay(150);
        var previousSurface = (Canvas)previousPopup.Child;
        previousSurface.Children.OfType<Button>().First().Focus(FocusState.Programmatic);
        ((Muxc.NumberBox)editor.FindName("OffsetInput")).Focus(FocusState.Programmatic);
        editor.TargetElement = replacement;
        panel.Children.Remove(target);
        await Task.Delay(150);

        Check(!previousPopup.IsOpen && previousPopup.Child == null && previousSurface.Children.Count == 0,
            "queued focus updates leave the disposed gradient adorner closed after target replacement");
        Check(VisualTreeHelper.GetOpenPopups(Window.Current).Any(popup => popup != previousPopup
            && popup.Child is Canvas canvas && canvas.Children.OfType<Button>().Any(button => Equals(button.Tag, "start"))),
            "the gradient editor opens its adorner on the replacement target");
    }

    private async Task VerifyStopSelectionAsync(string name, BrushEditControl editor, BrushSource source)
    {
        var original = source.Value;
        var stops = GradientStops(original);
        var initialFirst = stops[0].Color;
        var initialSecond = stops[1].Color;
        var handles = (Canvas)editor.FindName("StopHandles");
        var second = (Button)handles.Children[1];
        await InvokeAsync(second);
        var picker = (Muxc.ColorPicker)editor.FindName("Picker");
        var position = (Muxc.NumberBox)editor.FindName("OffsetInput");
        Check(picker.Color == initialSecond && Math.Abs(position.Value - stops[1].Offset) < 0.00001,
            $"{name} strip activation selects the second stop and synchronizes its editors");
        var editedColor = Color.FromArgb(160, 10, 100, 210);
        picker.Color = editedColor;
        position.Value = 0.65;
        Check(GradientStops(source.Value)[0].Color == initialFirst && GradientStops(source.Value)[1].Color == editedColor
            && Math.Abs(GradientStops(source.Value)[1].Offset - 0.65) < 0.00001,
            $"{name} selected stop survives brush replacement and receives color and position edits");
        await InvokeAsync((Button)handles.Children[0]);
        Check(picker.Color == initialFirst, $"{name} strip can switch back to the first stop");
        var focused = second.Focus(FocusState.Programmatic);
        await Task.Delay(50);
        Check(focused && picker.Color == editedColor, $"{name} keyboard focus selects the corresponding stop (focused={focused}, tabStop={second.IsTabStop})");
        source.Value = original;
    }

    private async Task VerifyAdornerEditsAsync(string name, BrushEditControl editor, BrushSource source, Canvas surface)
    {
        var original = source.Value;
        var stop = surface.Children.OfType<Button>().First(button => Equals(button.Tag, "stop:1"));
        await InvokeAsync(stop);
        var picker = (Muxc.ColorPicker)editor.FindName("Picker");
        Check(picker.Color == GradientStops(source.Value)[1].Color, $"{name} adorner activation selects its stop in the panel");

        // Exercise the actual native adorner edit path with brush-space coordinates.
        // Physical pointer routing requires an interactive desktop test runner.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var adorner = typeof(BrushEditControl).GetField("_adorner", flags)!.GetValue(editor)!;
        var move = adorner.GetType().GetMethod("Move", flags)!;
        if (source.Value is LinearGradientBrush)
        {
            move.Invoke(adorner, ["start", new Point(0.23456, 0.34567)]);
            move.Invoke(adorner, ["end", new Point(0.78912, 0.654321)]);
            Check(source.Value is LinearGradientBrush linear && Near(linear.StartPoint, new Point(0.23456, 0.34567))
                && Near(linear.EndPoint, new Point(0.78912, 0.654321)), "linear adorner preserves both geometry endpoints without rounding through TwoWay binding");
        }
        else
        {
            move.Invoke(adorner, ["center", new Point(0.4444, 0.4444)]);
            move.Invoke(adorner, ["origin", new Point(0.3444, 0.4444)]);
            move.Invoke(adorner, ["radiusX", new Point(0.88971, 0.4)]);
            move.Invoke(adorner, ["radiusY", new Point(0.4, 0.81234)]);
            Check(source.Value is RadialGradientBrush radial && Near(radial.Center, new Point(0.4444, 0.4444))
                && Near(radial.GradientOrigin, new Point(0.3444, 0.4444)) && Math.Abs(radial.RadiusX - 0.44531) < 0.00001
                && Math.Abs(radial.RadiusY - 0.36794) < 0.00001, "radial adorner preserves center, origin and both radii without rounding through TwoWay binding");
        }
        var (start, end) = source.Value switch
        {
            LinearGradientBrush linear => (linear.StartPoint, linear.EndPoint),
            RadialGradientBrush radial => (radial.GradientOrigin, new Point(radial.Center.X + radial.RadiusX, radial.Center.Y)),
            _ => throw new InvalidOperationException("Expected a gradient brush.")
        };
        move.Invoke(adorner, ["stop:1", new Point(start.X + (end.X - start.X) * 0.3636, start.Y + (end.Y - start.Y) * 0.3636)]);
        var position = (Muxc.NumberBox)editor.FindName("OffsetInput");
        var parser = (Windows.Globalization.NumberFormatting.INumberParser)position.NumberFormatter;
        Check(parser.ParseDouble(position.NumberFormatter.FormatDouble(0.3636)) is { } formatted && Math.Abs(formatted - 0.364) < 0.00001,
            $"{name} percentage formatter displays only one decimal (actual={position.NumberFormatter.FormatDouble(0.3636)})");
        Check(Math.Abs(GradientStops(source.Value)[1].Offset - 0.364) < 0.00001
            && Math.Abs(position.Value - 0.364) < 0.00001
            && parser.ParseDouble(position.Text) is { } displayed && Math.Abs(displayed - 0.364) < 0.00001,
            $"{name} adorner rounds a 36.36% stop position to 36.4% in both the brush and input (offset={GradientStops(source.Value)[1].Offset}, value={position.Value}, text={position.Text}, expected={position.NumberFormatter.FormatDouble(0.364)})");
        var moveCore = adorner.GetType().GetMethod("MoveCore", flags)!;
        if (source.Value is LinearGradientBrush)
        {
            moveCore.Invoke(adorner, ["start", new Point(0.21, 0.31), false]);
            Check(source.Value is LinearGradientBrush linear && Near(linear.StartPoint, new Point(0.21, 0.31)),
                "linear keyboard edits retain their finer coordinate precision");
        }
        else
        {
            moveCore.Invoke(adorner, ["origin", new Point(0.31, 0.41), false]);
            Check(source.Value is RadialGradientBrush radial && Near(radial.GradientOrigin, new Point(0.31, 0.41)),
                "radial keyboard edits retain their finer coordinate precision");
        }
        Check(!ReferenceEquals(source.Value, original), $"{name} adorner edits replace the brush for automatic saving");
        source.Value = original;
    }

    private static bool Near(Point actual, Point expected) => Math.Abs(actual.X - expected.X) < 0.00001 && Math.Abs(actual.Y - expected.Y) < 0.00001;

    private static IList<GradientStop> GradientStops(Brush? brush) => brush switch
    {
        LinearGradientBrush linear => linear.GradientStops,
        RadialGradientBrush radial => radial.GradientStops,
        _ => throw new InvalidOperationException("Expected a gradient brush.")
    };

    private static async Task InvokeAsync(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(50);
    }

    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}

public sealed class BrushSource : INotifyPropertyChanged
{
    private Brush? _value;
    public Brush? Value
    {
        get => _value;
        set
        {
            if (!ReferenceEquals(_value, value))
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class UnsupportedBrush : XamlCompositionBrushBase;
