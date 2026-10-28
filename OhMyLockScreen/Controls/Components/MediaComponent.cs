using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Controls.MediaControls;
using OhMyLockScreen.Shared.Models.Media;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services.Features.Layout;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "媒体", FluentIcons.Common.Icon.MusicNote1, "组合封面、媒体信息与播放控制，支持六种形态。")]
[ComponentDefaultFeatures(typeof(MediaDefaultFeaturesProvider))]
public sealed class MediaComponent : MediaComponentBase<MediaComponentSettings>
{
    public const string Id = "omls.components.media";
    private readonly Grid _root = new();
    private readonly List<MediaPartControl> _elements = [];
    private MediaVariant? _variant;
    private readonly Style _buttonStyle;

    public MediaComponent(IMediaStateService service) : base(service)
    {
        var resources = new ResourceDictionary { Source = new Uri("ms-appx:///OhMyLockScreen.Shared/Controls/Media/MediaResources.xaml") };
        _buttonStyle = (Style)resources["MediaTransportButtonStyle"];
        Content = _root;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Height)
            && _variant is MediaVariant.Immersive or MediaVariant.ImmersiveFull)
        {
            availableSize.Height = _variant == MediaVariant.ImmersiveFull ? 200 : 140;
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void UpdateMedia()
    {
        if (_variant != Settings.Variant)
        {
            _variant = Settings.Variant;
            BuildLayout(Settings.Variant);
        }

        foreach (var element in _elements)
        {
            switch (element)
            {
                case CoverImageControl cover:
                    cover.IsPlaceholder = IsPlaceholder;
                    break;
                case MediaController controller:
                    controller.IsPlaceholder = IsPlaceholder;
                    break;
                case MediaProgressBar progress:
                    progress.IsPlaceholder = IsPlaceholder;
                    break;
            }

            element.State = State;
        }
    }

    private void BuildLayout(MediaVariant variant)
    {
        _elements.Clear();
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        _root.CornerRadius = new CornerRadius(0);
        _root.MinWidth = 0;
        _root.Height = double.NaN;
        _root.Width = double.NaN;
        _root.HorizontalAlignment = HorizontalAlignment.Stretch;
        _root.Padding = new Thickness(16);
        _root.ColumnSpacing = 8;
        _root.RowSpacing = 0;

        if (variant == MediaVariant.IconOnly)
        {
            _root.Width = 48;
            _root.Height = 48;
            _root.HorizontalAlignment = HorizontalAlignment.Left;
            _root.Children.Add(new FluentIcons.Uwp.FluentIcon
            {
                Icon = FluentIcons.Common.Icon.MusicNote1,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        var immersive = variant is MediaVariant.Immersive or MediaVariant.ImmersiveFull;
        var full = variant is MediaVariant.Full or MediaVariant.ImmersiveFull;
        var body = _root;
        if (immersive)
        {
            _root.Padding = new Thickness(0);
            _root.CornerRadius = new CornerRadius(8);
            var cover = Add(new CoverImageControl());
            _root.Children.Add(cover);
            body = new Grid { Padding = new Thickness(16, 16, 14, full ? 16 : 14), ColumnSpacing = 8, RowSpacing = 4 };
            _root.Children.Add(body);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(body, Text(MediaTextField.Title, 18, true), 1, 0);
            Place(body, Text(MediaTextField.Artist, 14, false, 0.75), 2, 0);
            Place(body, Controllers(), 1, 1, rowSpan: 2);
            if (full)
            {
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Place(body, Timeline(), 3, 0, columnSpan: 2);
            }

            return;
        }

        body.ColumnSpacing = variant == MediaVariant.Full ? 12 : 8;
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var thumbnailSize = variant == MediaVariant.Full ? 64 : 52;
        var thumbnail = Add(new CoverImageControl { Width = thumbnailSize, Height = thumbnailSize, ShowSideDim = false });
        var thumbnailBorder = new Border { CornerRadius = new CornerRadius(8), Child = thumbnail };
        // Grid's corner radius clips its children; Border alone does not clip UWP content.
        var thumbnailClip = new Grid { CornerRadius = new CornerRadius(8) };
        thumbnailClip.Children.Add(thumbnailBorder);
        Place(body, thumbnailClip, 0, 0, rowSpan: 2);
        Place(body, Text(MediaTextField.Title, variant == MediaVariant.Full ? 20 : 18, true), 0, 1);
        Place(body, Text(MediaTextField.Artist, 14, false, 0.75), 1, 1);

        if (variant == MediaVariant.Lite)
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(body, Controllers(), 0, 2, rowSpan: 2);
        }
        else if (variant == MediaVariant.Full)
        {
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var controllers = Controllers();
            controllers.HorizontalAlignment = HorizontalAlignment.Center;
            Place(body, controllers, 2, 0, columnSpan: 2);
            Place(body, Timeline(), 3, 0, columnSpan: 2);
        }
    }

    private T Add<T>(T element) where T : MediaPartControl
    {
        _elements.Add(element);
        return element;
    }

    private MediaTextBlock Text(MediaTextField field, double size, bool bold = false, double opacity = 1)
    {
        var text = Add(new MediaTextBlock
        {
            Field = field,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Top
        });
        if (text.TextElement is TextBlock target)
        {
            target.LineHeight = size >= 18 ? 24 : size >= 14 ? 20 : 18;
            target.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        return text;
    }

    private StackPanel Controllers()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in new[] { MediaAction.Previous, MediaAction.PlayPause, MediaAction.Next })
        {
            var controller = Add(new MediaController { Action = action, Service = MediaService, Width = 40, Height = 40, FontSize = 16 });
            controller.SetButtonStyle(_buttonStyle);
            panel.Children.Add(controller);
        }

        return panel;
    }

    private Grid Timeline()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var elapsed = Text(MediaTextField.Position, 12, opacity: 0.78);
        elapsed.VerticalAlignment = VerticalAlignment.Center;
        Place(grid, elapsed, 0, 0);
        Place(grid, Add(new MediaProgressBar { Service = MediaService, Height = 32 }), 0, 1);
        var duration = Text(MediaTextField.Duration, 12, opacity: 0.78);
        duration.VerticalAlignment = VerticalAlignment.Center;
        Place(grid, duration, 0, 2);
        return grid;
    }

    private static void Place(Grid parent, FrameworkElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
        parent.Children.Add(element);
    }
}

public sealed class MediaDefaultFeaturesProvider : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() =>
    [
        new FeatureInfo
        {
            Id = "omls.features.layout.layout",
            Settings = new LayoutFeatureSettings
            {
                Width = 511,
                IsAutoWidth = false,
                IsAutoHeight = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        }
    ];
}
