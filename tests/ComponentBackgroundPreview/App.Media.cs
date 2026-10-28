using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Media;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.MediaControls;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.Media;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchMediaPreview()
    {
        try
        {
            var source = new PreviewMediaSource();
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<IMediaSessionSource>(source);
                services.AddSingleton<MediaStateService>();
                services.AddSingleton<IMediaStateService>(provider => provider.GetRequiredService<MediaStateService>());
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<FeatureRegistryService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<ContainerComponent>();
                services.AddComponent<MediaComponent, MediaSettingsControl>();
                services.AddComponent<MediaTextComponent, MediaTextSettingsControl>();
                services.AddComponent<MediaControllerComponent, MediaControllerSettingsControl>();
                services.AddComponent<MediaCoverComponent, MediaCoverSettingsControl>();
                services.AddComponent<MediaProgressComponent, MediaProgressSettingsControl>();
            }).Build();
            var service = IAppHost.GetRequiredService<MediaStateService>();
            await service.StartAsync(default);
            source.Current = service.Placeholder with
            {
                CanPlayPause = true, CanPrevious = true, CanNext = true, CanSeek = true
            };
            source.Notify();
            await Task.Delay(100);

            var variants = new StackPanel
            {
                Width = 553,
                Padding = new Thickness(21, 20, 21, 20),
                Spacing = 10,
                Background = new SolidColorBrush(Color.FromArgb(255, 68, 68, 68)),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var root = new StackPanel { Spacing = 12 };
            root.Children.Add(variants);
            var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            var scene = new ScenePresenter
            {
                IsEditing = true,
                Width = 511,
                Height = 100,
                Scene = new SceneSettings
                {
                    Layers =
                    [
                        new LayerSettings
                        {
                            Children =
                            [
                                new ComponentSettings
                                {
                                    Id = ContainerComponent.Id,
                                    Settings = new ContainerComponentSettings
                                    {
                                        Children = [new ComponentSettings { Id = MediaTextComponent.Id }]
                                    }
                                }
                            ]
                        }
                    ]
                }
            };
            foreach (var variant in new[] { MediaVariant.IconOnly, MediaVariant.Mini, MediaVariant.Lite, MediaVariant.Immersive, MediaVariant.Full, MediaVariant.ImmersiveFull })
            {
                var presenter = new ComponentPresenter
                {
                    IsEditing = true,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    Settings = new ComponentSettings { Id = MediaComponent.Id, Settings = new MediaComponentSettings { Variant = variant } }
                };
                variants.Children.Add(presenter);
            }

            root.Children.Add(scene);
            Window.Current.Content = scroll;
            Window.Current.Activate();
            await Task.Delay(1500);
            Check(variants.Children.OfType<ComponentPresenter>().All(item => item.Content is MediaComponent && item.ActualHeight > 0),
                "All six native media variants create and measure reusable controls");
            var nested = Descendants(scene).OfType<ComponentPresenter>().ToArray();
            Check(nested.Length == 2 && nested.All(item => item.IsEditing)
                && nested.All(item => item.Content is not OhMyLockScreen.Shared.Abstractions.Controls.Scene.ComponentBase component || component.IsEditing),
                "Editing context propagates through real data templates and nested containers");
            var inputs = Descendants(variants).OfType<Slider>().ToArray();
            Check(inputs.Length == 2 && inputs.All(item => Math.Abs(item.Value - 14) < 1),
                "Full variants display the same native timeline position");
            await CaptureAsync(variants, "media-variants.png");
            foreach (var variantIndex in new[] { 3, 5 })
            {
                var presenter = (ComponentPresenter)variants.Children[variantIndex];
                var defaultHeight = variantIndex == 5 ? 200 : 140;
                Check(Math.Abs(presenter.ActualHeight - defaultHeight) < 1,
                    $"Immersive variant {variantIndex} retains its automatic default height");
                foreach (var height in new[] { 300, 120 })
                {
                    presenter.Height = height;
                    await Task.Delay(100);
                    var backgroundCover = Descendants(presenter).OfType<CoverImageControl>().Single();
                    Check(Math.Abs(backgroundCover.ActualHeight - presenter.ActualHeight) < 1
                        && Math.Abs(presenter.ActualHeight - height) < 1,
                        $"Immersive variant {variantIndex} fills the component height at {height} logical pixels");
                }
                presenter.Height = double.NaN;
            }
            await Task.Delay(100);
            Check(source.SeekOperations == 0, "Programmatic timeline updates never request a seek");
            var nextButton = Descendants(variants).OfType<Button>().First(item => AutomationProperties.GetName(item) == "下一首");
            ((IInvokeProvider)new ButtonAutomationPeer(nextButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            Check(source.Operations == 1, "Native transport buttons invoke the shared media service");
            ((IRangeValueProvider)new SliderAutomationPeer(inputs[0]).GetPattern(PatternInterface.RangeValue)).SetValue(40);
            await Task.Delay(100);
            Check(source.SeekOperations == 1 && source.LastSeek == TimeSpan.FromSeconds(40),
                "Keyboard and accessibility changes commit the selected native slider position once");
            source.Current = source.Current with { IsPlaying = true, LastUpdatedTime = DateTimeOffset.UtcNow };
            source.Notify();
            await Task.Delay(100);
            var positionBeforeTick = inputs[0].Value;
            await Task.Delay(1200);
            Check(inputs[0].Value > positionBeforeTick && source.SeekOperations == 1,
                "The shared timer advances native timelines without issuing control requests");
            source.Current = source.Current with { IsPlaying = false };

            var ordinaryState = source.Current;
            var longTitle = string.Concat(Enumerable.Repeat("长标题与媒体元数据 ", 20));
            source.Current = ordinaryState with { Title = longTitle, Artist = longTitle };
            source.Notify();
            foreach (var presenter in variants.Children.OfType<ComponentPresenter>())
            {
                presenter.Width = 320;
            }
            await Task.Delay(100);
            Check(Descendants(variants).OfType<TextBlock>().Where(item => item.Text == longTitle)
                .All(item => item.TextTrimming == TextTrimming.CharacterEllipsis && item.ActualWidth <= 320),
                "Long metadata trims within narrow native media layouts");
            await CaptureAsync((FrameworkElement)variants.Children[5], "media-narrow.png");
            foreach (var presenter in variants.Children.OfType<ComponentPresenter>())
            {
                presenter.Width = double.NaN;
            }
            source.Current = ordinaryState;

            source.Current = MediaState.Empty;
            source.Notify();
            await Task.Delay(100);
            Check(nested.All(item => item.Visibility == Visibility.Visible), "Editing keeps empty media visible");
            scene.IsEditing = false;
            await Task.Delay(100);
            Check(nested.All(item => !item.IsEditing)
                && nested.Last().Visibility == Visibility.Collapsed, "Preview restores live visibility for nested media");
            source.Current = service.Placeholder;
            source.Notify();
            await Task.Delay(100);
            Check(nested.Last().Visibility == Visibility.Visible, "Hidden native media recovers after a session reappears");
            root.Children.Remove(scene);

            var parts = new StackPanel { Width = 511, Orientation = Orientation.Horizontal, Spacing = 12 };
            foreach (var id in new[] { MediaTextComponent.Id, MediaControllerComponent.Id, MediaCoverComponent.Id, MediaProgressComponent.Id })
            {
                parts.Children.Add(new ComponentPresenter
                {
                    IsEditing = true,
                    Width = 110,
                    Height = 110,
                    Settings = new ComponentSettings { Id = id }
                });
            }

            root.Children.Add(parts);
            await Task.Delay(200);
            Check(parts.Children.OfType<ComponentPresenter>().All(item => item.Content != null && item.Settings!.Features.Count == 0),
                "Independent media parts have no preinstalled decorative features");
            var textPart = (MediaTextComponent)((ComponentPresenter)parts.Children[0]).Content;
            textPart.FontSize = 32;
            textPart.Foreground = new SolidColorBrush(Colors.Red);
            await Task.Delay(50);
            Check(textPart.TextElement is TextBlock { FontSize: 32, Foreground: SolidColorBrush { Color: var textColor } }
                && textColor == Colors.Red, "Independent media text preserves native font and foreground inheritance");
            var textSettings = new MediaTextComponentSettings { Field = MediaTextField.Remaining, HideWhenNoMedia = false };
            var settingsPanel = IAppHost.GetRequiredService<IComponentsService>().CreateSettingsControl(new ComponentSettings
            {
                Id = MediaTextComponent.Id,
                Settings = textSettings
            }) ?? throw new InvalidOperationException("Media text settings were not registered.");
            root.Children.Add(settingsPanel);
            await Task.Delay(50);
            Check(textSettings.Field == MediaTextField.Remaining && !textSettings.HideWhenNoMedia,
                "Native settings initialization preserves the saved field and hide option");
            var fieldSelector = Descendants(settingsPanel).OfType<ComboBox>().Single();
            Check(fieldSelector.SelectedIndex == (int)MediaTextField.Remaining,
                "The native field selector displays the persisted enum value");
            fieldSelector.SelectedIndex = (int)MediaTextField.Artist;
            await Task.Delay(50);
            Check(textSettings.Field == MediaTextField.Artist, "Native field selection writes back to the existing settings model");
            textSettings.Field = MediaTextField.Duration;
            textSettings.HideWhenNoMedia = true;
            await Task.Delay(50);
            Check(fieldSelector.SelectedIndex == (int)MediaTextField.Duration
                && Descendants(settingsPanel).OfType<CheckBox>().Single().IsChecked == true,
                "External settings changes refresh the native selector and hide checkbox");
            root.Children.Remove(settingsPanel);
            root.Children.Remove(parts);

            var cover = new CoverImageControl { State = service.Placeholder, Width = 160, Height = 240 };
            root.Children.Add(cover);
            await Task.Delay(200);
            await CaptureAsync(cover, "media-portrait-cover.png");
            root.Children.Remove(cover);

            var intrinsicCover = new CoverImageControl { State = service.Placeholder, Width = 100 };
            root.Children.Add(intrinsicCover);
            await Task.Delay(100);
            Check(Math.Abs(intrinsicCover.ActualHeight - 100.0 * service.Placeholder.CoverHeight / service.Placeholder.CoverWidth) < 1,
                "An unstyled independent cover measures using the image's native aspect ratio");
            root.Children.Remove(intrinsicCover);
            root.Children.Add(cover);
            await Task.Delay(200);
            Check(cover.ActualWidth == 160 && cover.ActualHeight == 240, "Cover recreates its drawing resources after unloading");
            cover.State = service.Placeholder with { Cover = ReadOnlyMemory<byte>.Empty, CoverWidth = 0, CoverHeight = 0, ThemeColor = 0xFF606060 };
            await Task.Delay(100);
            var emptyCover = new RenderTargetBitmap();
            await emptyCover.RenderAsync(cover);
            var emptyPixel = PixelAt(((await emptyCover.GetPixelsAsync()).ToArray(), emptyCover.PixelWidth, emptyCover.PixelHeight), 5, 5);
            Check(emptyPixel.A == 255 && emptyPixel.R == emptyPixel.G && emptyPixel.G == emptyPixel.B,
                "Missing artwork clears the previous image and uses the neutral themed background");
            root.Children.Remove(cover);

            using (var transparentStream = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, transparentStream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 4, 4, 96, 96, new byte[64]);
                await encoder.FlushAsync();
                transparentStream.Seek(0);
                var artwork = await WindowsMediaSessionSource.ReadArtworkAsync(transparentStream, default);
                Check(artwork.ThemeColor == 0xFF606060 && artwork.CoverWidth == 4 && artwork.CoverHeight == 4,
                    "Native transparent artwork decoding retains intrinsic dimensions and a neutral fallback color");
            }

            using (var corruptStream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(corruptStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes([1, 2, 3, 4]);
                    await writer.StoreAsync();
                }
                corruptStream.Seek(0);
                var rejected = false;
                try
                {
                    await WindowsMediaSessionSource.ReadArtworkAsync(corruptStream, default);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    rejected = true;
                }
                Check(rejected, "Native artwork decoding rejects corrupt image data");
            }

            var count = source.Operations;
            Check(!await service.ExecuteAsync(MediaAction.Next) && count == source.Operations,
                "Unsupported controls do not reach the simulated session");
            await VerifyMediaThreadingAsync(root, service.Placeholder);
            await service.StopAsync(default);
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        var report = await ApplicationData.Current.LocalFolder.CreateFileAsync("media-report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteLinesAsync(report, _report);
    }

    private sealed class PreviewMediaSource : IMediaSessionSource
    {
        public MediaState Current { get; set; } = MediaState.Empty;
        public int Operations { get; private set; }
        public int SeekOperations { get; private set; }
        public TimeSpan LastSeek { get; private set; }
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MediaState> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public async Task<MediaState> ReadPlaceholderAsync(CancellationToken cancellationToken)
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///OhMyLockScreen.Shared/Assets/Media/PreviewCover.png"));
            using var stream = await file.OpenReadAsync();
            return await WindowsMediaSessionSource.ReadArtworkAsync(stream, cancellationToken);
        }
        public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken)
        {
            Operations++;
            return Task.FromResult(true);
        }
        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
        {
            SeekOperations++;
            LastSeek = position;
            return Task.FromResult(true);
        }
        public void Dispose() { }
    }
}
