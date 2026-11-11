namespace OhMyLockScreen.Shared.Models.Audio;

public enum SpectrumDock
{
    Top,
    Bottom,
    Left,
    Right
}

public enum SpectrumColorSource
{
    MediaCover,
    Foreground
}

/// <summary>单边幅度平方谱；发布后不再修改底层数据，不包含视图资源。</summary>
public sealed record AudioSpectrumSnapshot
{
    public static AudioSpectrumSnapshot Unavailable { get; } = new();
    public bool IsAvailable
    {
        get; init;
    }
    public ReadOnlyMemory<float> Power
    {
        get; init;
    }
    public int SampleRate
    {
        get; init;
    }
    public DateTimeOffset Timestamp
    {
        get; init;
    }
}
