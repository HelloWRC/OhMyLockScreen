using System.Buffers.Binary;
using OhMyLockScreen.Shared.Models.Audio;

namespace OhMyLockScreen.Services.Audio;

/// <summary>管道只传幅度平方谱；固定窗口限制帧大小，不传原始音频。</summary>
public static class AudioSpectrumProtocol
{
    public const string PipePrefix = @"LOCAL\OhMyLockScreen.Spectrum.";
    public const string RequestDirectory = "AudioSpectrumRequests";
    private const int Magic = 0x31534D4F;
    private const int HeaderSize = 20;
    private const int BinCount = SpectrumAnalyzer.WindowSize / 2 + 1;

    public static async Task WriteAsync(Stream stream, AudioSpectrumSnapshot state, CancellationToken cancellationToken)
    {
        var count = state.IsAvailable ? state.Power.Length : 0;
        if (count != 0 && count != BinCount)
        {
            throw new InvalidDataException("频谱窗口大小不正确。");
        }

        var bytes = new byte[HeaderSize + count * sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), count == 0 ? 0 : state.SampleRate);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(12), count == 0 ? 0 : state.Timestamp.UtcTicks);
        for (var index = 0; index < count; ++index)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(HeaderSize + index * sizeof(float)), state.Power.Span[index]);
        }

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<AudioSpectrumSnapshot> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var ticks = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12));
        if (BinaryPrimitives.ReadInt32LittleEndian(header) != Magic || count != 0 && count != BinCount
            || count != 0 && (sampleRate <= 0 || sampleRate > 768000 || ticks <= 0 || ticks > DateTimeOffset.MaxValue.UtcTicks))
        {
            throw new InvalidDataException("音频辅助进程发送了无效频谱帧。");
        }

        if (count == 0)
        {
            return AudioSpectrumSnapshot.Unavailable;
        }

        var bytes = new byte[count * sizeof(float)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var power = new float[count];
        for (var index = 0; index < count; ++index)
        {
            power[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float)));
            if (!float.IsFinite(power[index]) || power[index] < 0)
            {
                throw new InvalidDataException("频谱功率不是有效的非负数。");
            }
        }

        return new AudioSpectrumSnapshot
        {
            IsAvailable = true,
            Power = power,
            SampleRate = sampleRate,
            Timestamp = new DateTimeOffset(ticks, TimeSpan.Zero)
        };
    }
}
