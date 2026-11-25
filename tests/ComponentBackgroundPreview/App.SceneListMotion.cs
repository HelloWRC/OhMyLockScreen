using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Controls.SceneManager;
using OhMyLockScreen.Helpers.UI;
using OhMyLockScreen.ViewModels;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchSceneListMotionPreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "scene-list-motion-report.txt");
        File.WriteAllText(report, "RUNNING");
        _report.Clear();

        try
        {
            var animationsEnabled = new UISettings().AnimationsEnabled;
            Check(true, $"System UISettings.AnimationsEnabled = {animationsEnabled}");

            var items = new ObservableCollection<SceneCardItem>();
            var template = (DataTemplate)XamlReader.Load(
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
                Margin = new Thickness(10, 10, 10, 16),
                ItemsSource = items,
                ItemTemplate = template,
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

            var scrollViewer = new ScrollViewer
            {
                Width = 1140,
                Height = 720,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = repeater
            };

            var windowRoot = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 24, 24, 28)),
                RequestedTheme = ElementTheme.Dark
            };
            windowRoot.Children.Add(scrollViewer);

            Window.Current.Content = windowRoot;
            Window.Current.Activate();
            await Task.Delay(150);

            var controller = new SceneListMotionController(repeater);
            var preparedRecords = new List<PreparedRecord>();
            var clearedRecords = new List<ClearedRecord>();

            controller.Attach(Window.Current.CoreWindow.Dispatcher);
            repeater.ElementPrepared += (_, args) =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(args.Element);
                var hasTranslation = visual.Properties.TryGetVector3("Translation", out var translation)
                    == CompositionGetValueStatus.Succeeded;
                preparedRecords.Add(new PreparedRecord(
                    args.Index,
                    visual.ImplicitAnimations != null,
                    hasTranslation,
                    visual.Opacity,
                    translation));
            };
            repeater.ElementClearing += (_, args) =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(args.Element);
                var hasTranslation = visual.Properties.TryGetVector3("Translation", out var translation)
                    == CompositionGetValueStatus.Succeeded;
                clearedRecords.Add(new ClearedRecord(
                    visual.ImplicitAnimations != null,
                    visual.Opacity,
                    hasTranslation ? translation : Vector3.Zero));
            };

            // 1. 初始加载与交错进入动画
            for (var index = 0; index < 6; index++)
            {
                items.Add(CreateSampleCard(index));
            }

            await Task.Delay(750);

            Check(preparedRecords.Count >= 6
                && preparedRecords.Select(record => record.Index).Distinct().Count() == 6
                && preparedRecords.All(record => !record.HasImplicitAnimations && record.HasTranslation),
                $"Initial ElementPrepared clears ImplicitAnimations and enables Translation (events={preparedRecords.Count})");
            if (animationsEnabled)
            {
                Check(preparedRecords.All(record =>
                        record.Opacity == 0f
                        && Math.Abs(record.Translation.Y - SceneListMotion.EntranceOffsetY) < 0.01f),
                    "Initial ElementPrepared stages cards at Opacity=0 and Translation.Y=40 when animations are enabled");
            }

            VerifySettledCards(repeater, 6, animationsEnabled, "Initial entrance");

            // 2. 窄屏重排与 Offset 隐式过渡
            var card2Before = (ScenePreviewCard)repeater.TryGetElement(2)!;
            var posBefore = GetCardPosition(card2Before, repeater);
            Check(posBefore.X > 600 && posBefore.Y < 20,
                $"Card 2 starts in 3-column first row (X={posBefore.X:F1}, Y={posBefore.Y:F1})");

            scrollViewer.Width = 750;
            windowRoot.UpdateLayout();
            await Task.Delay(450);

            var card2After = (ScenePreviewCard)repeater.TryGetElement(2)!;
            var posAfter = GetCardPosition(card2After, repeater);
            Check(posAfter.Y > 150 && posAfter.X < 20,
                $"Card 2 reflows to row 1 col 0 after narrowing width (X={posAfter.X:F1}, Y={posAfter.Y:F1})");
            VerifySettledCards(repeater, 6, animationsEnabled, "After reflow");

            // 3. 筛选移除（ElementClearing 清理）
            clearedRecords.Clear();
            var removed = new List<SceneCardItem>();
            for (var index = 5; index >= 3; index--)
            {
                removed.Insert(0, items[index]);
                items.RemoveAt(index);
            }

            await Task.Delay(450);

            Check(clearedRecords.Count == 3
                && clearedRecords.All(record =>
                    !record.HasImplicitAnimations
                    && Math.Abs(record.Opacity - 1f) < 0.01f
                    && record.Translation.Length() < 0.01f),
                $"ElementClearing resets ImplicitAnimations, Opacity, and Translation on removed cards (count={clearedRecords.Count})");
            VerifySettledCards(repeater, 3, animationsEnabled, "Surviving cards after filter remove");

            // 4. 筛选恢复与复用（ElementPrepared 重新入场）
            preparedRecords.Clear();
            foreach (var item in removed)
            {
                items.Add(item);
            }

            await Task.Delay(750);

            Check(preparedRecords.Count == 3
                && preparedRecords.All(record => !record.HasImplicitAnimations && record.HasTranslation),
                $"Restored/recycled cards disarm ImplicitAnimations before re-entering (count={preparedRecords.Count})");
            if (animationsEnabled)
            {
                Check(preparedRecords.All(record =>
                        record.Opacity == 0f
                        && Math.Abs(record.Translation.Y - SceneListMotion.EntranceOffsetY) < 0.01f),
                    "Restored/recycled cards start entrance from Opacity=0 and Translation.Y=40");
            }

            VerifySettledCards(repeater, 6, animationsEnabled, "After filter restore");

            // 5. Detach 与缓存页面无数据变更重新激活兜底入场
            controller.Detach();
            for (var index = 0; index < 6; index++)
            {
                var card = (ScenePreviewCard)repeater.TryGetElement(index)!;
                var visual = ElementCompositionPreview.GetElementVisual(card);
                var translationOk = visual.Properties.TryGetVector3("Translation", out var tr) == CompositionGetValueStatus.Succeeded
                    && tr.Length() < 0.01f;
                Check(visual.ImplicitAnimations == null && Math.Abs(visual.Opacity - 1f) < 0.01f && translationOk,
                    $"Detach resets card {index} visual state");
            }

            controller.Attach(Window.Current.CoreWindow.Dispatcher);
            controller.QueueRealizedCardsEntrance();

            if (animationsEnabled)
            {
                var queuedCard0 = ElementCompositionPreview.GetElementVisual(repeater.TryGetElement(0)!);
                var hasTr = queuedCard0.Properties.TryGetVector3("Translation", out var queuedTr) == CompositionGetValueStatus.Succeeded;
                Check(queuedCard0.ImplicitAnimations == null
                    && queuedCard0.Opacity == 0f
                    && hasTr
                    && Math.Abs(queuedTr.Y - SceneListMotion.EntranceOffsetY) < 0.01f,
                    "QueueRealizedCardsEntrance prepares cached cards even when items and layout are unchanged");
            }

            await Task.Delay(750);
            VerifySettledCards(repeater, 6, animationsEnabled, "Cached re-entry");

            // 6. 渲染截图确认非空输出
            await CaptureAsync(scrollViewer, "scene-list-motion.png");
            scrollViewer.UpdateLayout();
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(scrollViewer);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var nonEmpty = false;
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                if (pixels[offset] > 0)
                {
                    nonEmpty = true;
                    break;
                }
            }
            Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && nonEmpty,
                $"Rendered scene-list-motion preview is non-empty ({bitmap.PixelWidth}x{bitmap.PixelHeight})");

            controller.Detach();
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

    private void VerifySettledCards(ItemsRepeater repeater, int expectedCount, bool animationsEnabled, string stage)
    {
        for (var index = 0; index < expectedCount; index++)
        {
            var element = repeater.TryGetElement(index);
            Check(element is ScenePreviewCard, $"{stage}: card {index} is realized as ScenePreviewCard");
            if (element == null)
            {
                continue;
            }

            var visual = ElementCompositionPreview.GetElementVisual(element);
            var hasTranslation = visual.Properties.TryGetVector3("Translation", out var tr) == CompositionGetValueStatus.Succeeded;
            var settledOpacity = Math.Abs(visual.Opacity - 1f) < 0.01f;
            var settledTranslation = hasTranslation && tr.Length() < 0.01f;
            var armedOffset = !animationsEnabled
                || (visual.ImplicitAnimations != null
                    && visual.ImplicitAnimations.TryGetValue("Offset", out var offsetAnim)
                    && offsetAnim != null);

            Check(settledOpacity && settledTranslation && armedOffset,
                $"{stage}: card {index} settled (Opacity={visual.Opacity:F2}, TranslationY={tr.Y:F2}, OffsetArmed={armedOffset})");
        }
    }

    private static Point GetCardPosition(UIElement card, UIElement relativeTo)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            var offset = card.ActualOffset;
            return new Point(offset.X, offset.Y);
        }

        return card.TransformToVisual(relativeTo).TransformPoint(new Point(0, 0));
    }

    private static SceneCardItem CreateSampleCard(int index) => new(Guid.NewGuid())
    {
        Name = $"测试场景 {index + 1}",
        Description = index % 2 == 0 ? $"Fluent 动效验证卡片 #{index + 1}" : "",
        IsActive = index == 0
    };

    private sealed record PreparedRecord(
        int Index,
        bool HasImplicitAnimations,
        bool HasTranslation,
        float Opacity,
        Vector3 Translation);

    private sealed record ClearedRecord(
        bool HasImplicitAnimations,
        float Opacity,
        Vector3 Translation);
}
