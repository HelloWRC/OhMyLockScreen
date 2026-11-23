using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Layout;

namespace OhMyLockScreen.ViewModels;

public sealed class TransformSettingsViewModel : ObservableObject, IDisposable
{
    private const double MinTranslate = -100000;
    private const double MaxTranslate = 100000;
    private const double MinRotation = -36000;
    private const double MaxRotation = 36000;
    private const double MinScale = -100;
    private const double MaxScale = 100;
    private const double MinSkew = -89;
    private const double MaxSkew = 89;
    private const double MinOrigin = -10;
    private const double MaxOrigin = 10;
    private const double OriginMatchTolerance = 0.0005;

    private readonly TransformFeatureSettings _settings;
    private CompositeDisposable? _subscriptions;

    public TransformSettingsViewModel(TransformFeatureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Rotate90Command = new RelayCommand(Rotate90);
        ResetCommand = new RelayCommand(Reset);
    }

    public IRelayCommand Rotate90Command { get; }

    public IRelayCommand ResetCommand { get; }

    public double TranslateX
    {
        get => _settings.TranslateX;
        set
        {
            if (IsInRange(value, MinTranslate, MaxTranslate))
            {
                _settings.TranslateX = Round(value);
            }
        }
    }

    public double TranslateY
    {
        get => _settings.TranslateY;
        set
        {
            if (IsInRange(value, MinTranslate, MaxTranslate))
            {
                _settings.TranslateY = Round(value);
            }
        }
    }

    public double Rotation
    {
        get => _settings.Rotation;
        set
        {
            if (IsInRange(value, MinRotation, MaxRotation))
            {
                _settings.Rotation = Round(value);
            }
        }
    }

    public double ScaleX
    {
        get => _settings.ScaleX;
        set
        {
            if (!IsInRange(value, MinScale, MaxScale))
            {
                return;
            }

            var nextX = RoundScale(value);
            if (_settings.IsScaleLocked)
            {
                var previousX = _settings.ScaleX;
                var nextY = Math.Abs(previousX) > 1e-6
                    ? RoundScale(Math.Clamp(_settings.ScaleY * (nextX / previousX), MinScale, MaxScale))
                    : nextX;
                _settings.ScaleX = nextX;
                _settings.ScaleY = nextY;
            }
            else
            {
                _settings.ScaleX = nextX;
            }
        }
    }

    public double ScaleY
    {
        get => _settings.ScaleY;
        set
        {
            if (!IsInRange(value, MinScale, MaxScale))
            {
                return;
            }

            var nextY = RoundScale(value);
            if (_settings.IsScaleLocked)
            {
                var previousY = _settings.ScaleY;
                var nextX = Math.Abs(previousY) > 1e-6
                    ? RoundScale(Math.Clamp(_settings.ScaleX * (nextY / previousY), MinScale, MaxScale))
                    : nextY;
                _settings.ScaleY = nextY;
                _settings.ScaleX = nextX;
            }
            else
            {
                _settings.ScaleY = nextY;
            }
        }
    }

    public bool FlipHorizontal
    {
        get => _settings.FlipHorizontal;
        set => _settings.FlipHorizontal = value;
    }

    public bool FlipVertical
    {
        get => _settings.FlipVertical;
        set => _settings.FlipVertical = value;
    }

    public bool IsScaleLocked
    {
        get => _settings.IsScaleLocked;
        set => _settings.IsScaleLocked = value;
    }

    public double SkewX
    {
        get => _settings.SkewX;
        set
        {
            if (IsInRange(value, MinSkew, MaxSkew))
            {
                _settings.SkewX = Round(value);
            }
        }
    }

    public double SkewY
    {
        get => _settings.SkewY;
        set
        {
            if (IsInRange(value, MinSkew, MaxSkew))
            {
                _settings.SkewY = Round(value);
            }
        }
    }

    public double OriginX
    {
        get => _settings.OriginX;
        set
        {
            if (IsInRange(value, MinOrigin, MaxOrigin))
            {
                _settings.OriginX = RoundScale(value);
            }
        }
    }

    public double OriginY
    {
        get => _settings.OriginY;
        set
        {
            if (IsInRange(value, MinOrigin, MaxOrigin))
            {
                _settings.OriginY = RoundScale(value);
            }
        }
    }

    public bool IsTopLeft
    {
        get => MatchesOrigin(0, 0);
        set => UpdateOriginAnchor(value, 0, 0, nameof(IsTopLeft));
    }

    public bool IsTopCenter
    {
        get => MatchesOrigin(0.5, 0);
        set => UpdateOriginAnchor(value, 0.5, 0, nameof(IsTopCenter));
    }

    public bool IsTopRight
    {
        get => MatchesOrigin(1, 0);
        set => UpdateOriginAnchor(value, 1, 0, nameof(IsTopRight));
    }

    public bool IsMiddleLeft
    {
        get => MatchesOrigin(0, 0.5);
        set => UpdateOriginAnchor(value, 0, 0.5, nameof(IsMiddleLeft));
    }

    public bool IsCenter
    {
        get => MatchesOrigin(0.5, 0.5);
        set => UpdateOriginAnchor(value, 0.5, 0.5, nameof(IsCenter));
    }

    public bool IsMiddleRight
    {
        get => MatchesOrigin(1, 0.5);
        set => UpdateOriginAnchor(value, 1, 0.5, nameof(IsMiddleRight));
    }

    public bool IsBottomLeft
    {
        get => MatchesOrigin(0, 1);
        set => UpdateOriginAnchor(value, 0, 1, nameof(IsBottomLeft));
    }

    public bool IsBottomCenter
    {
        get => MatchesOrigin(0.5, 1);
        set => UpdateOriginAnchor(value, 0.5, 1, nameof(IsBottomCenter));
    }

    public bool IsBottomRight
    {
        get => MatchesOrigin(1, 1);
        set => UpdateOriginAnchor(value, 1, 1, nameof(IsBottomRight));
    }

    private void UpdateOriginAnchor(bool isChecked, double x, double y, string propertyName)
    {
        if (isChecked)
        {
            SetOrigin(x, y);
        }
        else
        {
            OnPropertyChanged(propertyName);
        }
    }

    public void SetOrigin(double x, double y)
    {
        if (!IsInRange(x, MinOrigin, MaxOrigin) || !IsInRange(y, MinOrigin, MaxOrigin))
        {
            return;
        }

        _settings.OriginX = RoundScale(x);
        _settings.OriginY = RoundScale(y);
        NotifyOriginAnchors();
    }

    public void Rotate90()
    {
        var current = double.IsFinite(_settings.Rotation) ? _settings.Rotation : 0;
        var next = (current + 90) % 360;
        if (next < 0)
        {
            next += 360;
        }

        _settings.Rotation = Round(next);
    }

    public void Reset()
    {
        _settings.TranslateX = 0;
        _settings.TranslateY = 0;
        _settings.Rotation = 0;
        _settings.ScaleX = 1;
        _settings.ScaleY = 1;
        _settings.FlipHorizontal = false;
        _settings.FlipVertical = false;
        _settings.SkewX = 0;
        _settings.SkewY = 0;
        _settings.OriginX = 0.5;
        _settings.OriginY = 0.5;
    }

    public void Attach()
    {
        if (_subscriptions != null)
        {
            return;
        }

        _subscriptions = new CompositeDisposable
        {
            _settings.WhenValueChanged(source => source.TranslateX).Subscribe(_ => OnPropertyChanged(nameof(TranslateX))),
            _settings.WhenValueChanged(source => source.TranslateY).Subscribe(_ => OnPropertyChanged(nameof(TranslateY))),
            _settings.WhenValueChanged(source => source.Rotation).Subscribe(_ => OnPropertyChanged(nameof(Rotation))),
            _settings.WhenValueChanged(source => source.ScaleX).Subscribe(_ => OnPropertyChanged(nameof(ScaleX))),
            _settings.WhenValueChanged(source => source.ScaleY).Subscribe(_ => OnPropertyChanged(nameof(ScaleY))),
            _settings.WhenValueChanged(source => source.FlipHorizontal).Subscribe(_ => OnPropertyChanged(nameof(FlipHorizontal))),
            _settings.WhenValueChanged(source => source.FlipVertical).Subscribe(_ => OnPropertyChanged(nameof(FlipVertical))),
            _settings.WhenValueChanged(source => source.IsScaleLocked).Subscribe(_ => OnPropertyChanged(nameof(IsScaleLocked))),
            _settings.WhenValueChanged(source => source.SkewX).Subscribe(_ => OnPropertyChanged(nameof(SkewX))),
            _settings.WhenValueChanged(source => source.SkewY).Subscribe(_ => OnPropertyChanged(nameof(SkewY))),
            _settings.WhenValueChanged(source => source.OriginX).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(OriginX));
                NotifyOriginAnchors();
            }),
            _settings.WhenValueChanged(source => source.OriginY).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(OriginY));
                NotifyOriginAnchors();
            })
        };
    }

    public void Dispose()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    private bool MatchesOrigin(double x, double y)
        => Math.Abs(_settings.OriginX - x) <= OriginMatchTolerance
            && Math.Abs(_settings.OriginY - y) <= OriginMatchTolerance;

    private void NotifyOriginAnchors()
    {
        OnPropertyChanged(nameof(IsTopLeft));
        OnPropertyChanged(nameof(IsTopCenter));
        OnPropertyChanged(nameof(IsTopRight));
        OnPropertyChanged(nameof(IsMiddleLeft));
        OnPropertyChanged(nameof(IsCenter));
        OnPropertyChanged(nameof(IsMiddleRight));
        OnPropertyChanged(nameof(IsBottomLeft));
        OnPropertyChanged(nameof(IsBottomCenter));
        OnPropertyChanged(nameof(IsBottomRight));
    }

    private static bool IsInRange(double value, double min, double max)
        => double.IsFinite(value) && value >= min && value <= max;

    private static double Round(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    private static double RoundScale(double value)
    {
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }
}
