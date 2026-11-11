using System.Buffers.Binary;

namespace OhMyLockScreen.Services.Audio;

/// <summary>已验证的交错 PCM 格式；扩展 PCM 的有效位左对齐在容器内。</summary>
public readonly record struct AudioSampleFormat(int SampleRate, int Channels, int Bits, int BlockAlign, bool IsFloat)
{
    public static AudioSampleFormat Read(ReadOnlySpan<byte> format)
    {
        if (format.Length < 18)
        {
            throw new NotSupportedException("音频格式头不完整。");
        }

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
        var align = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
        if (tag == 0xFFFE)
        {
            if (format.Length < 40 || BinaryPrimitives.ReadUInt16LittleEndian(format[16..]) < 22)
            {
                throw new NotSupportedException("扩展音频格式头不完整。");
            }

            var subformat = new Guid(format.Slice(24, 16));
            tag = subformat == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1
                : subformat == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 : (ushort)0;
            var validBits = BinaryPrimitives.ReadUInt16LittleEndian(format[18..]);
            if (validBits > bits)
            {
                throw new NotSupportedException("PCM 有效位数超过容器位数。");
            }
        }

        var floating = tag == 3;
        if (channels is < 1 or > 32 || rate <= 0 || align < channels * ((bits + 7) / 8)
            || (floating ? bits is not (32 or 64) : tag != 1 || bits is not (8 or 16 or 24 or 32)))
        {
            throw new NotSupportedException("不支持当前输出设备的混音格式。");
        }

        return new AudioSampleFormat(rate, channels, bits, align, floating);
    }

    public int Decode(ReadOnlySpan<byte> data, Span<float> destination)
    {
        var frames = Math.Min(data.Length / BlockAlign, destination.Length / Channels);
        var bytes = Bits / 8;
        for (var frame = 0; frame < frames; ++frame)
        {
            for (var channel = 0; channel < Channels; ++channel)
            {
                var sample = data[(frame * BlockAlign + channel * bytes)..];
                var value = IsFloat
                    ? Bits == 32 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample))
                        : (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(sample))
                    : Bits switch
                    {
                        8 => (sample[0] - 128) / 128f,
                        16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                        24 => ((sample[0] << 8 | sample[1] << 16 | sample[2] << 24) >> 8) / 8388608f,
                        32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                        _ => 0
                    };
                destination[frame * Channels + channel] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
            }
        }

        return frames * Channels;
    }
}
