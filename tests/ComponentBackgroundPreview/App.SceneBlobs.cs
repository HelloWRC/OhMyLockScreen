using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private void LaunchSceneBlobPreview()
    {
        var root = new StackPanel { Children = { _status } };
        root.Loaded += VerifySceneBlobsAsync;
        Window.Current.Content = root;
        Window.Current.Activate();
    }

    private async void VerifySceneBlobsAsync(object sender, RoutedEventArgs args)
    {
        var root = (StackPanel)sender;
        root.Loaded -= VerifySceneBlobsAsync;
        var data = ApplicationData.Current.LocalFolder.Path;
        try
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);
            using var settings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(data, "BlobPreviewSettings.json"));
            using var scenes = new SceneService(settings, NullLogger<SceneService>.Instance, data);
            scenes.Initialize();
            var storage = new SceneBlobStorageService(scenes);
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<ISceneBlobStorageService>(storage);
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddFeature<BackgroundFeature>();
                services.AddComponent<ContainerComponent>();
                services.AddComponent<TextComponent>();
            }).Build();

            var scene = scenes.Load(scenes.ActiveSceneId);
            var other = scenes.Create("Blob isolation");
            var image = await StorageFile.GetFileFromApplicationUriAsync(
                new Uri("ms-appx:///OhMyLockScreen.Shared/Assets/Media/PreviewCover.png"));
            var converter = new SceneBlobUriConverter(storage, scene.SceneId);
            var picker = new BlobPicker
            {
                BlobUriConverter = converter,
                FileTypes = [".png"],
                SelectFileAsync = () => Task.FromResult<StorageFile?>(image),
                ValidateAsync = async uri => { await SceneImageDecoder.ReadSizeAsync(uri); }
            };
            picker.SetBinding(BlobPicker.BlobIdProperty, new Binding
            {
                Source = scene.Background,
                Path = new PropertyPath(nameof(BackgroundSettings.BlobId)),
                Mode = BindingMode.TwoWay
            });
            var picked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var publishCount = 0;
            picker.BlobPicked += (_, name) =>
            {
                publishCount++;
                picked.TrySetResult(name);
            };
            picker.PickFailed += (_, error) => picked.TrySetException(error);
            root.Children.Add(picker);
            await Task.Delay(100);
            var button = FindDropVisual<Button>(picker)!;
            var invoker = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
            invoker.Invoke();
            await picked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var blobId = picker.BlobId!;
            Check(blobId.Length == 64 && scene.Background.BlobId == blobId, "Blob picker writes through its native two-way binding");
            var blobUri = converter.Resolve(blobId)!;
            var size = await SceneImageDecoder.ReadSizeAsync(blobUri);
            Check(size.Width > 0 && size.Height > 0, "WinRT decodes the file at an extensionless app-data URI");

            var originalBrush = new ImageBrush { Stretch = Stretch.Fill };
            var editor = new BrushEditControl { Brush = originalBrush, BlobUriConverter = converter };
            root.Children.Add(editor);
            await Task.Delay(100);
            var imagePicker = FindDropVisual<BlobPicker>(editor)!;
            imagePicker.SelectFileAsync = () => Task.FromResult<StorageFile?>(image);
            var brushPicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            imagePicker.BlobPicked += (_, _) => brushPicked.TrySetResult();
            imagePicker.PickFailed += (_, error) => brushPicked.TrySetException(error);
            var imageButton = FindDropVisual<Button>(imagePicker)!;
            ((IInvokeProvider)new ButtonAutomationPeer(imageButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await brushPicked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var brush = (ImageBrush)editor.Brush!;
            Check(SceneImageBrush.GetBlobId(brush) == blobId && !ReferenceEquals(brush, originalBrush)
                && brush.Stretch == Stretch.Fill, "The brush editor replaces the configured brush and preserves its styling");
            root.Children.Remove(editor);
            var child = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings(),
                Features = [new FeatureInfo
                {
                    Id = "omls.features.appearance.background",
                    Settings = new BackgroundFeatureSettings { Background = brush }
                }]
            };
            scene.Layers = [new LayerSettings
            {
                Kind = LayoutKind.Grid,
                Children = [new ComponentSettings
                {
                    Id = ContainerComponent.Id,
                    Settings = new ContainerComponentSettings { Children = { child } }
                }]
            }];
            var surface = new ScenePresenter { Scene = scene, Width = 400, Height = 240 };
            root.Children.Add(surface);
            ComponentPresenter? nested = null;
            BitmapImage? bitmap = null;
            for (var attempt = 0; attempt < 40 && bitmap?.PixelWidth is null or 0; attempt++)
            {
                await Task.Delay(50);
                nested = Descendants(surface).OfType<ComponentPresenter>().SingleOrDefault(item => ReferenceEquals(item.Settings, child));
                bitmap = (nested?.Border?.Background as ImageBrush)?.ImageSource as BitmapImage;
            }
            Check(nested?.Content is TextComponent text && text.BlobUriConverter?.Resolve(blobId) == blobUri,
                "A nested component receives its scene converter");
            Check(bitmap?.PixelWidth > 0 && bitmap.UriSource == blobUri && brush.ImageSource == null,
                "XAML renders the extensionless URI from a runtime copy of the configured brush");
            root.Children.Remove(surface);

            var pending = new TaskCompletionSource<StorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var selecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.SelectFileAsync = () =>
            {
                selecting.TrySetResult();
                return pending.Task;
            };
            invoker.Invoke();
            await selecting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            picker.BlobUriConverter = new SceneBlobUriConverter(storage, other.SceneId);
            pending.SetResult(image);
            for (var attempt = 0; attempt < 40 && !button.IsEnabled; attempt++)
            {
                await Task.Delay(50);
            }
            Check(button.IsEnabled && publishCount == 1 && scene.Background.BlobId == blobId && storage.GetBlobUri(other.SceneId, blobId) == null,
                "A picker completion is discarded after changing scenes");
            root.Children.Remove(picker);
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        _status.Text = string.Join(Environment.NewLine, _report);
        File.WriteAllLines(Path.Combine(data, "scene-blobs-report.txt"), _report);
    }
}
