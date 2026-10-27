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

        await run("Video copies publish scene-relative assets and clean up uncommitted files", async () =>
        {
            var sceneDirectory = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Scene");
            var assets = Path.Combine(sceneDirectory, "Assets");
            var bytes = new byte[] { 4, 5, 6 };
            string? source = null;
            var imported = await SceneMediaImport.ImportVideoAsync(
                () => Task.FromResult<Stream>(new MemoryStream(bytes)), ".MP4", sceneDirectory, () => true, value => source = value);
            Check(imported && source is not null && source.StartsWith("Assets/") && source.EndsWith(".mp4")
                && File.ReadAllBytes(Path.Combine(sceneDirectory, source)).SequenceEqual(bytes), "The copied video was not persistent.");
            var previous = source;
            var current = true;
            var canceled = await SceneMediaImport.ImportVideoAsync(() =>
            {
                current = false;
                return Task.FromResult<Stream>(new MemoryStream(bytes));
            }, ".mp4", sceneDirectory, () => current, value => source = value);
            Check(!canceled && source == previous && Directory.GetFiles(assets).Length == 1,
                "A stale video copy was published or leaked.");
            try
            {
                await SceneMediaImport.ImportVideoAsync(
                    () => throw new IOException("read failed"), ".mp4", sceneDirectory, () => true, value => source = value);
                throw new Exception("A copy failure was ignored.");
            }
            catch (IOException)
            {
            }

            Check(source == previous && Directory.GetFiles(assets).Length == 1, "A failed video copy changed the background or leaked a file.");
            try
            {
                await SceneMediaImport.ImportVideoAsync(
                    () => Task.FromResult<Stream>(new MemoryStream(bytes)), ".mp4", sceneDirectory, () => true,
                    _ => throw new InvalidOperationException("target removed"));
                throw new Exception("A commit failure was ignored.");
            }
            catch (InvalidOperationException)
            {
            }

            Check(Directory.GetFiles(assets).Length == 1, "A failed video commit leaked a file.");
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
