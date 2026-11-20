namespace OhMyLockScreen.Shared.Models.Media;

public static class MediaTextFields
{
    public static (string Name, MediaTextField Value)[] Options =>
    [
        ("标题", MediaTextField.Title), ("艺术家", MediaTextField.Artist), ("副标题", MediaTextField.Subtitle),
        ("专辑", MediaTextField.AlbumTitle), ("专辑艺术家", MediaTextField.AlbumArtist), ("流派", MediaTextField.Genres),
        ("曲目编号", MediaTextField.TrackNumber), ("专辑曲目数", MediaTextField.AlbumTrackCount),
        ("媒体类型", MediaTextField.PlaybackType), ("来源应用", MediaTextField.SourceAppId),
        ("已播放时长", MediaTextField.Position), ("总时长", MediaTextField.Duration), ("剩余时长", MediaTextField.Remaining)
    ];
}
