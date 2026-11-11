using MathNet.Numerics.IntegralTransforms;
using OhMyLockScreen.Shared.Models.Audio;
using System.Numerics;

namespace OhMyLockScreen.Services.Audio;

/// <summary>复用每声道采样窗口和 FFT 工作区；发布的功率数组独立拥有。</summary>
public sealed class SpectrumAnalyzer(int sampleRate, int channels)
{
    public const int WindowSize = 4096;
    private readonly float[][] _samples = Enumerable.Range(0, channels).Select(_ => new float[WindowSize]).ToArray();
    private readonly Complex[] _fft = new Complex[WindowSize];
    private readonly double[] _window = Enumerable.Range(0, WindowSize)
        .Select(index => 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (WindowSize - 1))).ToArray();
    private int _position;
    private int _filled;

    public void Append(ReadOnlySpan<float> interleaved)
    {
        for (var frame = 0; frame + channels <= interleaved.Length; frame += channels)
        {
            for (var channel = 0; channel < channels; ++channel)
            {
                var value = interleaved[frame + channel];
                _samples[channel][_position] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
            }

            _position = (_position + 1) % WindowSize;
            _filled = Math.Min(WindowSize, _filled + 1);
        }
    }

    public void Reset()
    {
        foreach (var samples in _samples)
        {
            Array.Clear(samples);
        }

        _position = _filled = 0;
    }

    public AudioSpectrumSnapshot Analyze(DateTimeOffset timestamp)
    {
        var power = new float[WindowSize / 2 + 1];
        if (_filled > 0)
        {
            var normalization = 2 / _window.Sum();
            for (var channel = 0; channel < channels; ++channel)
            {
                for (var index = 0; index < WindowSize; ++index)
                {
                    _fft[index] = _samples[channel][(_position + index) % WindowSize] * _window[index];
                }

                Fourier.Forward(_fft, FourierOptions.NoScaling);
                for (var bin = 1; bin < power.Length; ++bin)
                {
                    var magnitude = _fft[bin].Magnitude * normalization;
                    if (bin == WindowSize / 2)
                    {
                        magnitude /= 2;
                    }
                    power[bin] += (float)(magnitude * magnitude / channels);
                }
            }
        }

        return new AudioSpectrumSnapshot { IsAvailable = true, Power = power, SampleRate = sampleRate, Timestamp = timestamp };
    }
}
