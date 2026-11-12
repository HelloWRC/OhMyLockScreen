using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Storage;

internal static class SceneBlobStorageTests
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> run)
    {
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
        await run("Concurrent scene imports publish one complete, portable blob per scene", async () =>
        {
            var root = ApplicationData.Current.LocalFolder.Path;
            using var settings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(root, "Settings.json"));
            using var scenes = OpenScenes(settings, root);
            var storage = new SceneBlobStorageService(scenes);
            var scene = scenes.Load(scenes.ActiveSceneId);
            var other = scenes.Create("Other");
            var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
            var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var directory = scenes.GetSceneDirectory(scene.SceneId);
            var relativePath = Path.Combine("Blobs", expected[..2], expected);
            var path = Path.Combine(directory, relativePath);

            async Task<string> ImportAsync()
            {
                using var stream = new MemoryStream(bytes);
                return await storage.ImportAsync(scene.SceneId, stream);
            }

            var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ImportAsync()));
            Check(ids.All(id => id == expected) && File.ReadAllBytes(path).SequenceEqual(bytes), "Concurrent imports changed the ID or content.");
            Check(Directory.GetFiles(Path.Combine(directory, "Blobs"), "*", SearchOption.AllDirectories).Length == 1
                && Directory.GetFiles(directory, ".blob-import-*.tmp").Length == 0,
                "An import left an extra file.");
            var converter = new SceneBlobUriConverter(storage, scene.SceneId);
            var uri = converter.Resolve(expected.ToUpperInvariant());
            Check(uri?.AbsoluteUri == $"ms-appdata:///local/Scenes/{scene.SceneId:N}/Blobs/{expected[..2]}/{expected}",
                "The converter did not resolve the current scene or normalize the ID.");
            Check(storage.GetBlobUri(other.SceneId, expected) == null && storage.GetBlobUri(Guid.NewGuid(), expected) == null
                && converter.Resolve("../outside") == null && converter.Resolve(new string('x', 64)) == null
                && converter.Resolve(null) == null, "An invalid or out-of-scene reference resolved.");

            scene.Background = new BackgroundSettings { Mode = BackgroundMode.Video, BlobId = expected };
            scenes.QueueSave(scene);
            scenes.FlushPending();
            var destinationRoot = Path.Combine(root, "destination");
            var destination = Path.Combine(destinationRoot, "Scenes", scene.SceneId.ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(destination, relativePath))!);
            File.Copy(path, Path.Combine(destination, relativePath));
            File.Copy(Path.Combine(directory, "scene.json"), Path.Combine(destination, "scene.json"));

            using var destinationSettings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(destinationRoot, "Settings.json"));
            using var movedScenes = OpenScenes(destinationSettings, destinationRoot);
            var movedStorage = new SceneBlobStorageService(movedScenes);
            Check(movedScenes.Load(scene.SceneId).Background.BlobId == expected
                && movedStorage.GetBlobUri(scene.SceneId, expected) == uri
                && File.ReadAllBytes(Path.Combine(destination, relativePath)).SequenceEqual(bytes), "A copied scene lost its blob.");
            var json = File.ReadAllText(Path.Combine(destination, "scene.json"));
            Check(json.Contains(expected) && !json.Contains("ms-appdata") && !json.Contains(relativePath), "The scene persisted a runtime location.");
        });

        await run("Canceled and failed blob imports clean staging files, and duplication skips pending files", async () =>
        {
            var root = ApplicationData.Current.LocalFolder.Path;
            using var settings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(root, "Settings.json"));
            using var scenes = OpenScenes(settings, root);
            var id = scenes.ActiveSceneId;
            var directory = scenes.GetSceneDirectory(id);
            var storage = new SceneBlobStorageService(scenes);
            using var cancellation = new CancellationTokenSource();
            using var waiting = new WaitingReadStream();
            var pending = storage.ImportAsync(id, waiting, cancellation.Token);
            await waiting.Started.Task;
            Check(Directory.GetFiles(directory, ".blob-import-*.tmp").Length == 1, "Import did not create a unique staged file.");
            var clone = scenes.Duplicate(id);
            Check(Directory.GetFiles(scenes.GetSceneDirectory(clone.SceneId), ".blob-import-*.tmp").Length == 0, "The copy included a staged file.");
            cancellation.Cancel();
            await ExpectFailureAsync<OperationCanceledException>(pending);

            using var failing = new FailedReadStream();
            await ExpectFailureAsync<IOException>(storage.ImportAsync(id, failing));
            Check(Directory.GetFiles(directory, ".blob-import-*.tmp").Length == 0
                && !Directory.Exists(Path.Combine(directory, "Blobs")), "An incomplete import left a file or shard.");
        });

        await run("A legacy video path is dropped when the scene is loaded and saved", () =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                """{"Name":"旧场景","Background":{"Mode":1,"Source":"Assets/movie.mp4","EnableSound":true}}"""));
            var scene = ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(stream);
            Check(scene.Name == "旧场景" && scene.Background.EnableSound && scene.Background.BlobId == null,
                "A legacy video path or non-asset setting was mishandled.");
            using var output = new MemoryStream();
            ConfigureFileHelper.SaveConfig(output, scene);
            Check(!Encoding.UTF8.GetString(output.ToArray()).Contains("Assets/movie.mp4"), "The legacy path was saved again.");
            return Task.CompletedTask;
        });
    }

    private static SceneService OpenScenes(SettingsService settings, string root)
    {
        var scenes = new SceneService(settings, NullLogger<SceneService>.Instance, root);
        scenes.Initialize();
        return scenes;
    }

    private static async Task ExpectFailureAsync<T>(Task task) where T : Exception
    {
        try
        {
            await task;
        }
        catch (T)
        {
            return;
        }

        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class WaitingReadStream : MemoryStream
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class FailedReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Simulated source failure."));
    }
}
