using System.Runtime.InteropServices.WindowsRuntime;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async Task VerifyReorderedForegroundAsync(StackPanel root)
    {
        Brush[] brushes =
        [
            new SolidColorBrush(Colors.Red),
            new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop { Color = Colors.Red },
                    new GradientStop { Color = Colors.Blue, Offset = 1 }
                }
            },
            new Microsoft.UI.Xaml.Media.RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop { Color = Colors.Red },
                    new GradientStop { Color = Colors.Blue, Offset = 1 }
                }
            },
            new AcrylicBrush { TintColor = Colors.Red, FallbackColor = Colors.Red, AlwaysUseFallback = true },
            new AcrylicBrush { TintColor = Colors.Red, FallbackColor = Colors.Red, TintOpacity = 0.75 }
        ];
        foreach (var brush in brushes)
        {
            await VerifyReorderedForegroundAsync(root, false, brush);
            await VerifyReorderedForegroundAsync(root, true, brush);
        }
    }

    private async Task VerifyReorderedForegroundAsync(StackPanel root, bool onLayer, Brush brush)
    {
        var foreground = new ForegroundFeatureSettings { Foreground = brush };
        var registry = IAppHost.GetRequiredService<ComponentRegistryService>();
        var children = new ContainerComponentSettings
        {
            Children = [registry.Find(TextComponent.Id)!.CreateSettings(), registry.Find(ClockComponent.Id)!.CreateSettings()]
        };
        var container = new ComponentSettings
        {
            Id = ContainerComponent.Id,
            Settings = children,
            Features = [new FeatureInfo { Id = "omls.features.appearance.foreground", Settings = foreground }]
        };
        var scene = new SceneSettings { Layers = [new LayerSettings { Children = [container] }] };
        if (onLayer)
        {
            scene.Layers[0].Features.Add(container.Features[0]);
            container.Features.Clear();
        }

        var surface = new ScenePresenter { Scene = scene, Width = 600, Height = 400 };
        var editor = (ForegroundSettingsControl)IAppHost.GetRequiredService<IFeatureService>()
            .CreateSettingsControl(onLayer ? scene.Layers[0].Features[0] : container.Features[0])!;
        root.Children.Insert(0, surface);
        root.Children.Add(editor);
        ComponentPresenter presenter = null!;
        try
        {
            root.UpdateLayout();
            await Task.Delay(80);
            presenter = FindDropVisual<ComponentPresenter>(surface)!;
            editor.TargetElement = presenter.Border;
            await VerifyAsync("before reorder");
            editor.TargetElement = null;
            root.Children.Remove(editor);
            children.Children.Move(0, 1);
            root.UpdateLayout();
            await Task.Delay(80);
            await VerifyAsync("after reorder");
            var child = children.Children[0];
            children.Children.RemoveAt(0);
            children.Children.Insert(1, child);
            root.UpdateLayout();
            await Task.Delay(80);
            await VerifyAsync("after remove and insert");
            root.Children.Add(editor);
            root.UpdateLayout();
            await Task.Delay(80);
            await VerifyAsync("after reopening the foreground inspector");
            brush = new SolidColorBrush(Colors.Green);
            foreground.Foreground = brush;
            root.UpdateLayout();
            await Task.Delay(80);
            await VerifyAsync("after replacing the foreground");
            (onLayer ? scene.Layers[0].Features : container.Features).Clear();
            brush = surface.Foreground;
            root.UpdateLayout();
            await Task.Delay(80);
            await VerifyAsync("after restoring native inheritance", checkSettings: false);
        }
        finally
        {
            root.Children.Remove(surface);
            root.Children.Remove(editor);
        }

        async Task VerifyAsync(string stage, bool checkSettings = true)
        {
            Check(ReferenceEquals(presenter.Foreground, brush), $"Container presenter retains {(onLayer ? "layer" : "container")} {brush.GetType().Name} foreground {stage}");
            var texts = Descendants(presenter).OfType<TextBlock>().ToArray();
            Check(texts.Length == 2 && texts.All(text => text.Foreground?.GetType() == brush.GetType()
                && (brush is XamlCompositionBrushBase ? !ReferenceEquals(text.Foreground, brush) : ReferenceEquals(text.Foreground, brush))),
                $"Both container children inherit {brush.GetType().Name} foreground {stage}; expected {(brush as SolidColorBrush)?.Color}, actual {string.Join(", ", texts.Select(text => (text.Foreground as SolidColorBrush)?.Color.ToString() ?? text.Foreground?.GetType().Name ?? "null"))}");
            foreach (var text in texts)
            {
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(text);
                var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                var painted = Enumerable.Range(0, pixels.Length / 4).Count(index => pixels[index * 4 + 3] > 0);
                Check(painted > 50, $"Text actually renders {stage}: {text.Text}, {painted} painted pixels");
            }
            if (checkSettings)
            {
                Check(ReferenceEquals(foreground.Foreground, brush), $"Container foreground settings survive {stage}");
            }
        }
    }
}
