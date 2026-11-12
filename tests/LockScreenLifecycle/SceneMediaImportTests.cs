using OhMyLockScreen.Services.Scene;

internal static class SceneMediaImportTests
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> run)
    {
        await run("Mixed file drops preserve order, isolate failures and use only the first video", async () =>
        {
            var imported = new List<string>();
            var result = await SceneMediaImport.RunAsync(
                new[] { "a.PNG", "first.MP4", "broken.jpg", "b.jpeg", "second.mkv", "notes.txt" }, name => name,
                name =>
                {
                    if (name == "broken.jpg")
                    {
                        throw new InvalidDataException("decode failed");
                    }

                    imported.Add(name);
                    return Task.FromResult(true);
                },
                name =>
                {
                    imported.Add(name);
                    return Task.FromResult(true);
                }, () => true);
            Check(imported.SequenceEqual(new[] { "a.PNG", "first.MP4", "b.jpeg" }), "Import order or video limit was lost.");
            Check(result.VideoName == "first.MP4" && result.SkippedVideos == 1
                && result.UnsupportedFiles.SequenceEqual(new[] { "notes.txt" })
                && result.FailedFiles.Single().Contains("broken.jpg"), "Import results did not report skipped or failed files.");

            var videos = 0;
            var images = 0;
            var failed = await SceneMediaImport.RunAsync(new[] { "broken.mp4", "second.mp4", "image.png" }, name => name,
                _ => Task.FromResult(++images > 0),
                _ =>
                {
                    videos++;
                    throw new IOException("copy failed");
                }, () => true);
            Check(videos == 1 && images == 1 && failed.VideoName == null && failed.SkippedVideos == 1 && failed.FailedFiles.Count == 1,
                "A failed first video selected a replacement or stopped image import.");
        });

        await run("Pending file drops stop after their scene or preview context is invalidated", async () =>
        {
            var current = true;
            var pending = new TaskCompletionSource<bool>();
            var imports = 0;
            var operation = SceneMediaImport.RunAsync(new[] { "first.png", "video.mp4", "last.png" }, name => name,
                async _ =>
                {
                    imports++;
                    return await pending.Task && current;
                },
                _ => throw new Exception("A canceled drop imported a video."), () => current);
            current = false;
            pending.SetResult(true);
            var result = await operation;
            Check(imports == 1 && result.VideoName == null && result.FailedFiles.Count == 0,
                "A canceled drop continued or reported a stale failure.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
