using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Controls.SceneManager;
using OhMyLockScreen.Helpers.UI;
using OhMyLockScreen.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchScenePreviewCardPreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "scene-preview-card-report.txt");
        File.WriteAllText(report, "RUNNING");
        _report.Clear();

        try
        {
            var animationsEnabled = new UISettings().AnimationsEnabled;
            Check(true, $"System UISettings.AnimationsEnabled = {animationsEnabled}");

            var sampleImageUri = await CreateSyntheticPreviewImageAsync("scene-preview-sample.png", 712, 400);
            const string longDescription =
                "描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述" +
                "描描述描述描述述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述" +
                "描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述描述";

            var rootCanvas = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
                RequestedTheme = ElementTheme.Dark
            };

            var showcaseHost = new StackPanel
            {
                Spacing = 16,
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32))
            };

            // 2x2 对照网格：精确对应 Figma 286:1849 的四个变体；四周留白确保 -2px 上移不被截屏裁剪。
            var figmaMatrixGrid = new Grid
            {
                Padding = new Thickness(16),
                ColumnSpacing = 16,
                RowSpacing = 22,
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32))
            };
            figmaMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            figmaMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            figmaMatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            figmaMatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var defaultRestCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "场景标题 114514",
                Description = longDescription,
                Author = "HelloWRC",
                License = "CC BY 4.0",
                PreviewImageUri = sampleImageUri,
                IsActive = false
            });
            var defaultHoverCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "场景标题 114514",
                Description = longDescription,
                Author = "HelloWRC",
                License = "CC BY 4.0",
                PreviewImageUri = sampleImageUri,
                IsActive = false
            });
            var selectedRestCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "场景标题 114514",
                Description = longDescription,
                Author = "HelloWRC",
                License = "CC BY 4.0",
                PreviewImageUri = sampleImageUri,
                IsActive = true
            });
            var selectedHoverCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "场景标题 114514",
                Description = longDescription,
                Author = "HelloWRC",
                License = "CC BY 4.0",
                PreviewImageUri = sampleImageUri,
                IsActive = true
            });

            Grid.SetRow(defaultRestCard, 0);
            Grid.SetColumn(defaultRestCard, 0);
            Grid.SetRow(defaultHoverCard, 0);
            Grid.SetColumn(defaultHoverCard, 1);
            Grid.SetRow(selectedRestCard, 1);
            Grid.SetColumn(selectedRestCard, 0);
            Grid.SetRow(selectedHoverCard, 1);
            Grid.SetColumn(selectedHoverCard, 1);

            figmaMatrixGrid.Children.Add(defaultRestCard);
            figmaMatrixGrid.Children.Add(defaultHoverCard);
            figmaMatrixGrid.Children.Add(selectedRestCard);
            figmaMatrixGrid.Children.Add(selectedHoverCard);

            // 边界对照行：测试无图片渐底、仅作者、仅许可、超长换行/截断及全空字段。
            var edgeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16
            };

            var authorOnlyGradientCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "仅作者与渐变底色",
                Author = "Alice",
                IsActive = false
            });
            var licenseOnlyLongTitleCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "超长标题截断验证：这是一个非常非常非常长以至于必须单行省略的场景标题名称示例",
                Description = "单行短描述。",
                License = "GPL-3.0",
                PreviewImageUri = sampleImageUri,
                IsActive = false
            });
            var wrappingMetadataCard = CreateShowcaseCard(new SceneCardItem(Guid.NewGuid())
            {
                Name = "元信息换行与超长截断",
                Description = "测试作者与较长分发许可在 WrapPanel 内的自动换行与省略。",
                Author = "VeryLongAuthorNameSample_HelloWRC_Preview",
                License = "Creative Commons Attribution-ShareAlike 4.0 International",
                PreviewImageUri = sampleImageUri,
                IsActive = false
            });

            edgeRow.Children.Add(authorOnlyGradientCard);
            edgeRow.Children.Add(licenseOnlyLongTitleCard);
            edgeRow.Children.Add(wrappingMetadataCard);

            showcaseHost.Children.Add(figmaMatrixGrid);
            showcaseHost.Children.Add(edgeRow);

            // 同时挂载一个带 SceneListMotionController 的 ItemsRepeater 验证两者视觉树不冲突。
            var repeaterItems = new ObservableCollection<SceneCardItem>
            {
                new(Guid.NewGuid())
                {
                    Name = "交互弹簧测试卡片",
                    Description = "验证与 SceneListMotionController 共存及悬停/按压弹簧位移。",
                    Author = "HelloWRC",
                    License = "MIT",
                    PreviewImageUri = sampleImageUri,
                    IsActive = false
                }
            };
            var itemTemplate = (DataTemplate)XamlReader.Load(
                """
                <DataTemplate
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:manager="using:OhMyLockScreen.Controls.SceneManager">
                    <manager:ScenePreviewCard Item="{Binding}" />
                </DataTemplate>
                """);
            var repeater = new ItemsRepeater
            {
                ItemsSource = repeaterItems,
                ItemTemplate = itemTemplate,
                Layout = new UniformGridLayout
                {
                    MinItemWidth = 356,
                    MinItemHeight = 200,
                    MinColumnSpacing = 16,
                    MinRowSpacing = 16,
                    ItemsJustification = UniformGridLayoutItemsJustification.Start,
                    ItemsStretch = UniformGridLayoutItemsStretch.None
                }
            };
            repeater.ElementPrepared += (_, args) =>
            {
                if (args.Element is ScenePreviewCard card && args.Index >= 0 && args.Index < repeaterItems.Count)
                {
                    card.Item = repeaterItems[args.Index];
                }
            };
            showcaseHost.Children.Add(repeater);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = showcaseHost
            };
            rootCanvas.Children.Add(scroll);

            var motionController = new SceneListMotionController(repeater);
            Window.Current.Content = rootCanvas;
            Window.Current.Activate();
            motionController.Attach(Window.Current.CoreWindow.Dispatcher);

            await Task.Delay(750);

            // 静态将展开对比卡设为悬停态（先立即对齐以稳定截屏，也覆盖弹簧过渡测试）。
            defaultHoverCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: false);
            selectedHoverCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: false);
            authorOnlyGradientCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: false);
            licenseOnlyLongTitleCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: false);
            wrappingMetadataCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: false);

            await Task.Delay(250);
            showcaseHost.UpdateLayout();

            Check(!defaultRestCard.FooterOverlayElement.IsHitTestVisible
                && !selectedRestCard.FooterOverlayElement.IsHitTestVisible,
                "Rest cards keep FooterOverlay non-hit-testable");
            Check(defaultHoverCard.FooterOverlayElement.IsHitTestVisible
                && selectedHoverCard.FooterOverlayElement.IsHitTestVisible,
                "Expanded cards enable hit-testing on FooterOverlay");

            Check(Math.Abs(GetVisualOpacity(defaultRestCard.DescriptionElement) - 0f) < 0.01f
                && Math.Abs(GetVisualOpacity(defaultRestCard.FooterOverlayElement) - 0f) < 0.01f,
                "Rest cards hide description and footer at Opacity=0");
            Check(Math.Abs(GetVisualOpacity(defaultHoverCard.DescriptionElement) - 1f) < 0.01f
                && Math.Abs(GetVisualOpacity(defaultHoverCard.FooterOverlayElement) - 1f) < 0.01f,
                "Expanded cards reveal description and footer at Opacity=1");

            var restHeaderPos = GetVisualTranslation(defaultRestCard.HeaderOverlayElement);
            var hoverHeaderPos = GetVisualTranslation(defaultHoverCard.HeaderOverlayElement);
            Check(Math.Abs(restHeaderPos.Y - ScenePreviewCardMotion.GetRestHeaderOffsetY(200f)) < 0.5f
                && Math.Abs(hoverHeaderPos.Y - ScenePreviewCardMotion.HoverHeaderOffsetY) < 0.5f,
                $"Header translation aligns rest/hover positions (restY={restHeaderPos.Y:F1}, hoverY={hoverHeaderPos.Y:F1})");

            var expectedHoverLift = animationsEnabled ? ScenePreviewCardMotion.HoverLiftOffsetY : 0f;
            var expectedPressedLift = animationsEnabled ? ScenePreviewCardMotion.PressedLiftOffsetY : 0f;
            var hoverLiftNow = GetVisualTranslation(defaultHoverCard.CardRootElement);
            Check(Math.Abs(hoverLiftNow.Y - expectedHoverLift) < 0.1f,
                $"Immediate hover state sets CardRoot Translation.Y={hoverLiftNow.Y:F2} (expected={expectedHoverLift:F2})");

            // 验证实际像素：非悬停卡片顶部保持亮蓝色预览背景；悬停卡片全卡叠加暗化模糊遮罩；选中卡片带主题蓝描边。
            var restPixels = await RenderElementAsync(defaultRestCard);
            var hoverPixels = await RenderElementAsync(defaultHoverCard);
            var selectedPixels = await RenderElementAsync(selectedRestCard);

            var sampleX = restPixels.Width / 2;
            var topSampleY = Math.Clamp((int)Math.Round(restPixels.Height * 0.15), 0, restPixels.Height - 1);
            var bottomSampleY = Math.Clamp((int)Math.Round(restPixels.Height * 0.92), 0, restPixels.Height - 1);
            var restTopBright = PixelAt(restPixels, sampleX, topSampleY);
            var hoverTopDark = PixelAt(hoverPixels, sampleX, topSampleY);
            var restBottomDark = PixelAt(restPixels, sampleX, bottomSampleY);
            var selectedBorderEdge = PixelAt(selectedPixels, 1, selectedPixels.Height / 2);

            var restTopLum = restTopBright.R + restTopBright.G + restTopBright.B;
            var hoverTopLum = hoverTopDark.R + hoverTopDark.G + hoverTopDark.B;
            var restBottomLum = restBottomDark.R + restBottomDark.G + restBottomDark.B;

            Check(restTopLum > hoverTopLum + 40,
                $"Hover darkens upper card region via sliding mask (restTopRGB={restTopBright.R},{restTopBright.G},{restTopBright.B}; hoverTopRGB={hoverTopDark.R},{hoverTopDark.G},{hoverTopDark.B})");
            Check(restTopLum > restBottomLum + 40,
                $"Rest card applies dark bottom scrim while keeping top bright (restBottomRGB={restBottomDark.R},{restBottomDark.G},{restBottomDark.B})");
            Check(selectedBorderEdge.B > selectedBorderEdge.R + 40,
                $"Selected card renders accent blue outline border (edgeRGB={selectedBorderEdge.R},{selectedBorderEdge.G},{selectedBorderEdge.B})");

            // 验证 ItemsRepeater 内卡片的弹簧交互（悬停 -> 按压回落 -> 松开弹回 -> 离开复位）与外层 Visual 隔离。
            var liveCard = repeater.TryGetElement(0) as ScenePreviewCard;
            Check(liveCard != null, "ItemsRepeater realized interactive ScenePreviewCard");
            if (liveCard != null)
            {
                var outerStart = GetVisualTranslation(liveCard);
                Check(outerStart.Length() < 0.05f, $"Outer card Translation settled at zero (Y={outerStart.Y:F2})");

                liveCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: true);
                var hoverSettled = await WaitForLiftSettlementAsync(liveCard);
                var afterHover = GetVisualTranslation(liveCard.CardRootElement);
                var outerAfterHover = GetVisualTranslation(liveCard);
                Check(hoverSettled && Math.Abs(afterHover.Y - expectedHoverLift) < 0.15f && outerAfterHover.Length() < 0.05f,
                    $"Spring hover settles CardRoot.Y={afterHover.Y:F2} (expected={expectedHoverLift:F2}, settled={hoverSettled}) without mutating outer Visual (outerY={outerAfterHover.Y:F2})");

                liveCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: true, useTransitions: true);
                var pressSettled = await WaitForLiftSettlementAsync(liveCard);
                var afterPress = GetVisualTranslation(liveCard.CardRootElement);
                Check(pressSettled && Math.Abs(afterPress.Y - expectedPressedLift) < 0.15f,
                    $"Spring press lowers CardRoot.Y={afterPress.Y:F2} (expected={expectedPressedLift:F2}, settled={pressSettled})");

                liveCard.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: true);
                var releaseSettled = await WaitForLiftSettlementAsync(liveCard);
                var afterRelease = GetVisualTranslation(liveCard.CardRootElement);
                Check(releaseSettled && Math.Abs(afterRelease.Y - expectedHoverLift) < 0.15f,
                    $"Spring release rebounds CardRoot.Y={afterRelease.Y:F2} (expected={expectedHoverLift:F2}, settled={releaseSettled})");

                liveCard.SetInteractionStateForTesting(isPointerOver: false, isPressed: false, useTransitions: true);
                var exitSettled = await WaitForLiftSettlementAsync(liveCard);
                var afterExit = GetVisualTranslation(liveCard.CardRootElement);
                Check(exitSettled && Math.Abs(afterExit.Y) < 0.15f,
                    $"Spring exit settles CardRoot.Y={afterExit.Y:F2} back to 0 (settled={exitSettled})");

                liveCard.SetInteractionStateForTesting(isPointerOver: false, isPressed: true, useTransitions: true);
                var unhoveredSettled = await WaitForLiftSettlementAsync(liveCard);
                var unhoveredPress = GetVisualTranslation(liveCard.CardRootElement);
                Check(unhoveredSettled && Math.Abs(unhoveredPress.Y) < 0.15f,
                    $"Pressing without hover keeps CardRoot.Y={unhoveredPress.Y:F2} at 0 (settled={unhoveredSettled})");
            }

            // 捕获对比矩阵截图，并恢复默认布局确认非空输出。
            await CaptureAsync(figmaMatrixGrid, "scene-preview-card.png");
            await CaptureAsync(showcaseHost, "scene-preview-card-full.png");

            motionController.Detach();
            File.WriteAllLines(report, _report);
        }
        catch (Exception exception)
        {
            File.AppendAllText(report, "\nFAIL: " + exception);
        }
        finally
        {
            Exit();
        }
    }

    private static ScenePreviewCard CreateShowcaseCard(SceneCardItem item) => new()
    {
        Item = item,
        IsTabStop = false
    };

    private static async Task<bool> WaitForLiftSettlementAsync(ScenePreviewCard card, int timeoutMs = 1500)
    {
        var elapsed = 0;
        while (card.CompletedLiftAnimationToken != card.CurrentLiftAnimationToken && elapsed < timeoutMs)
        {
            await Task.Delay(50);
            elapsed += 50;
        }

        return card.CompletedLiftAnimationToken == card.CurrentLiftAnimationToken;
    }

    private static float GetVisualOpacity(UIElement element) =>
        ElementCompositionPreview.GetElementVisual(element).Opacity;

    private static Vector3 GetVisualTranslation(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        return visual.Properties.TryGetVector3("Translation", out var translation) == CompositionGetValueStatus.Succeeded
            ? translation
            : Vector3.Zero;
    }

    private static async Task<Uri> CreateSyntheticPreviewImageAsync(string fileName, int width, int height, bool dark = false)
    {
        var divisor = dark ? 5 : 1;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var ny = y / (float)height;
            for (var x = 0; x < width; x++)
            {
                var nx = x / (float)width;
                // 上半部明亮天蓝渐变 + 下半部柔和花海紫绿过渡，再叠加高对比数字条块用于验证高斯模糊与遮罩对比度。
                var r = (byte)Math.Clamp((int)(120 + 70 * nx + (ny > 0.55f ? 30 * MathF.Sin(nx * 14f) : 45 * (1f - ny))), 0, 255);
                var g = (byte)Math.Clamp((int)(185 + 35 * (1f - ny) + (ny > 0.6f ? 15 * MathF.Cos(nx * 18f) : 0f)), 0, 255);
                var b = (byte)Math.Clamp((int)(235 - 25 * ny + 15 * MathF.Sin(nx * 8f)), 0, 255);

                // 中央大号深灰时间色块，模拟截图里的 "23 56" 强烈明暗边缘，便于视觉核对模糊强度。
                var inClockBand = ny is >= 0.25f and <= 0.58f &&
                    ((nx is >= 0.14f and <= 0.26f) || (nx is >= 0.30f and <= 0.42f) ||
                     (nx is >= 0.56f and <= 0.68f) || (nx is >= 0.72f and <= 0.84f));
                if (inClockBand)
                {
                    r = 45;
                    g = 52;
                    b = 62;
                }

                // 中央垂直亮色人物轮廓块
                if (nx is >= 0.44f and <= 0.54f && ny is >= 0.14f and <= 0.88f)
                {
                    r = 225;
                    g = 215;
                    b = 240;
                }

                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(b / divisor);
                pixels[offset + 1] = (byte)(g / divisor);
                pixels[offset + 2] = (byte)(r / divisor);
                pixels[offset + 3] = 255;
            }
        }

        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        return new Uri($"ms-appdata:///local/{fileName}");
    }
}
