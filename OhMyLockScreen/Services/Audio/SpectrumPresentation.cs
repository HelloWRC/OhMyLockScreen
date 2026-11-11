using OhMyLockScreen.Shared.Models.Audio;

namespace OhMyLockScreen.Services.Audio;

public readonly record struct SpectrumBar(double X, double Y, double Width, double Height);

/// <summary>每个视图独立进行频段映射与时间平滑，不修改共享快照。</summary>
public sealed class SpectrumPresentation
{
    private double[] _levels = [];
    public ReadOnlyMemory<double> Levels => _levels;
    public bool HasSignal => _levels.Any(level => level > 0);
    public void Reset() => _levels = [];

    public void Update(AudioSpectrumSnapshot state, int barCount, double sensitivity, TimeSpan elapsed, DateTimeOffset now)
    {
        barCount = Math.Clamp(barCount, 8, 256);
        sensitivity = FiniteClamp(sensitivity, 0.1, 5, 1);
        if (_levels.Length != barCount)
        {
            _levels = new double[barCount];
        }

        var powers = state.Power.Span;
        var valid = state.IsAvailable && state.SampleRate > 0 && powers.Length > 1
            && now - state.Timestamp < TimeSpan.FromMilliseconds(150);
        var maximum = Math.Min(16000, state.SampleRate / 2d);
        var binWidth = state.SampleRate / (double)Math.Max(1, (powers.Length - 1) * 2);
        for (var bar = 0; bar < barCount; ++bar)
        {
            var target = 0d;
            if (valid && maximum > 40)
            {
                var lower = 40 * Math.Pow(maximum / 40, bar / (double)barCount);
                var upper = 40 * Math.Pow(maximum / 40, (bar + 1d) / barCount);
                // Include the nearest bin for narrow low-frequency bands.
                var start = Math.Clamp((int)Math.Round(lower / binWidth), 1, powers.Length - 1);
                var end = Math.Clamp((int)Math.Round(upper / binWidth), start, powers.Length - 1);
                var power = 0d;
                for (var bin = start; bin <= end; ++bin)
                {
                    if (float.IsFinite(powers[bin]))
                    {
                        power = Math.Max(power, powers[bin]);
                    }
                }

                if (power > 0)
                {
                    var decibels = 10 * Math.Log10(power * sensitivity * sensitivity);
                    target = Math.Clamp((decibels + 70) / 70, 0, 1);
                }
            }

            var seconds = Math.Clamp(elapsed.TotalSeconds, 0, 1);
            var response = target > _levels[bar] ? 0.04 : 0.2;
            _levels[bar] += (target - _levels[bar]) * (1 - Math.Exp(-seconds / response));
            if (_levels[bar] < 0.001)
            {
                _levels[bar] = 0;
            }
        }
    }

    public static double[] Placeholder(int count) => Enumerable.Range(0, Math.Clamp(count, 8, 256))
        .Select(index => 0.15 + 0.55 * Math.Pow(Math.Sin((index + 1) * 0.37), 2)).ToArray();

    public static SpectrumBar[] Arrange(ReadOnlySpan<double> levels, double width, double height, SpectrumDock dock, double gap)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || levels.Length == 0)
        {
            return [];
        }

        var vertical = dock is SpectrumDock.Left or SpectrumDock.Right;
        var length = vertical ? height : width;
        // Preserve a positive bar width even when the requested gaps cannot fit.
        gap = Math.Min(FiniteClamp(gap, 0, 20, 2), length / levels.Length * 0.8);
        var thickness = (length - gap * (levels.Length - 1)) / levels.Length;
        var bars = new SpectrumBar[levels.Length];
        for (var index = 0; index < levels.Length; ++index)
        {
            var level = FiniteClamp(levels[index], 0, 1, 0);
            var offset = index * (thickness + gap);
            bars[index] = dock switch
            {
                SpectrumDock.Top => new(offset, 0, thickness, height * level),
                SpectrumDock.Left => new(0, offset, width * level, thickness),
                SpectrumDock.Right => new(width * (1 - level), offset, width * level, thickness),
                _ => new(offset, height * (1 - level), thickness, height * level)
            };
        }

        return bars;
    }

    private static double FiniteClamp(double value, double minimum, double maximum, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
