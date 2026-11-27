using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

internal sealed class TestEnvironment : IDisposable
{
    private static readonly string DefaultSceneJson = BuildDefaultSceneJson();
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("OhMyLockScreen.Tests.");

    internal TestEnvironment()
    {
        Application.Current = null;
        IAppHost.Host = null;
        ApplicationData.Current.LocalFolder.Path = directory.FullName;
        var packagePath = Path.Combine(directory.FullName, "Package");
        Windows.ApplicationModel.Package.Current.InstalledLocation.Path = packagePath;
        var assets = Directory.CreateDirectory(Path.Combine(packagePath, "Assets"));
        File.WriteAllText(Path.Combine(assets.FullName, "DefaultScene.json"), DefaultSceneJson);
        Window.Current = new Window();
        Windows.System.UserProfile.LockScreen.OpenImage =
            () => throw new InvalidOperationException("No simulated lock screen image was configured.");
        LockScreenHosting.IsHosted = () => true;
        WindowShapeProbe.GetShape = _ => (WindowShapeProbe.ApplicationFrameClass, 0);
        Windows.ApplicationModel.FullTrustProcessLauncher.Launch = _ => Task.CompletedTask;
        CoreApplication.ExitCalls = 0;
        Windows.UI.ViewManagement.ApplicationView.Reset();
        Windows.UI.Core.SystemNavigationManager.Reset();
        Storyboard.LastCreated = null;
        Storyboard.FailOnBegin = false;
        LibVLCSharp.Shared.MediaPlayer.FailOnPlay = false;
        OhMyLockScreen.Shared.Controls.Scene.VideoSwapChain.Created.Clear();
        OhMyLockScreen.Shared.Controls.Scene.VideoSwapChain.FailOnCreate = false;
        DispatcherTimer.Created.Clear();
        Windows.UI.Xaml.Hosting.ElementCompositionPreview.Reset();
        Windows.UI.Composition.Compositor.Reset();
        ConfigureFileHelper.Errors.Clear();
        ConfigureFileHelper.IsBackupEnabled = true;
    }

    public void Dispose()
    {
        var host = IAppHost.Host;
        IAppHost.Host = null;
        Application.Current = null;
        if (host != null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                host.StopAsync(timeout.Token).WaitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            host.Dispose();
        }

        if (!directory.FullName.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean up a directory outside the test temp root.");
        directory.Delete(recursive: true);
    }

    private static string BuildDefaultSceneJson()
    {
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(LayoutFeatureSettings).Assembly);

        var clock = CreateDefaultTextComponent(ClockComponent.Id, new ClockComponentSettings(), new ClockDefaultFeaturesProvider());
        var date = CreateDefaultTextComponent(DateComponent.Id, new DateComponentSettings(), new DateDefaultFeaturesProvider());
        var scene = new SceneSettings
        {
            Layers =
            {
                new LayerSettings
                {
                    Name = "ContentLayer",
                    Kind = LayoutKind.Grid,
                    Children =
                    {
                        new ComponentSettings
                        {
                            Id = ContainerComponent.Id,
                            Name = "Clock",
                            Settings = new ContainerComponentSettings
                            {
                                Children = { clock, date }
                            },
                            Features =
                            {
                                CreateLayoutFeature(HorizontalAlignment.Center, new Thickness(0, 64, 0, 0))
                            }
                        }
                    }
                }
            }
        };

        using var stream = new MemoryStream();
        ConfigureFileHelper.SaveConfig(stream, scene);
        stream.Position = 0;
        var node = JsonNode.Parse(stream)!.AsObject();
        node.Remove(nameof(SceneSettings.SceneId));
        node.Remove(nameof(SceneSettings.Name));
        return node.ToJsonString();

        static ComponentSettings CreateDefaultTextComponent(
            string id,
            IComponentSettings settings,
            IComponentDefaultFeaturesProvider defaults)
        {
            var component = new ComponentSettings
            {
                Id = id,
                Settings = settings
            };
            foreach (var feature in defaults.CreateDefaultFeatures())
            {
                component.Features.Add(feature);
            }

            component.Features.Add(CreateLayoutFeature(HorizontalAlignment.Center));
            return component;
        }

        static FeatureInfo CreateLayoutFeature(HorizontalAlignment alignment, Thickness margin = default) => new()
        {
            Id = "omls.features.layout.layout",
            Settings = new LayoutFeatureSettings
            {
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = margin
            }
        };
    }
}
