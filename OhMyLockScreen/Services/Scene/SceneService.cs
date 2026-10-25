using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Services.Scene;
/// <summary>只保留摘要与不可变的序列化快照，不跨视图共享可变模型或 UI 对象。</summary>
public sealed class SceneService : ISceneService, IDisposable
{
    private readonly object _sync = new();
    private readonly SettingsService _settings;
    private readonly ILogger<SceneService> _logger;
    private readonly Dictionary<Guid, SceneInfo> _scenes = [];
    private readonly Dictionary<Guid, byte[]> _pending = [];
    private readonly Dictionary<Guid, byte[]> _savedSnapshots = [];
    private readonly Dictionary<Guid, Exception> _saveErrors = [];
    private readonly List<string> _loadErrors = [];
    private readonly Timer _saveTimer;
    private bool _initialized;
    private bool _disposed;
    private bool _saveScheduled;
    public SceneService(SettingsService settings, ILogger<SceneService> logger, string dataPath)
    {
        _settings = settings;
        _logger = logger;
        ScenesPath = Path.Combine(dataPath, "Scenes");
        _saveTimer = new Timer(_ => FlushPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string ScenesPath
    {
        get;
    }

    public Guid ActiveSceneId
    {
        get
        {
            lock (_sync)
            {
                return _settings.Settings.ActiveSceneId ?? Guid.Empty;
            }
        }
    }

    public IReadOnlyList<string> LoadErrors
    {
        get
        {
            lock (_sync)
            {
                return _loadErrors.ToArray();
            }
        }
    }

    public event EventHandler<SceneChangedEventArgs>? SceneSaved;
    public event EventHandler<SceneChangedEventArgs>? ActiveSceneChanged;
    public event EventHandler<SceneChangedEventArgs>? SaveFailed;
    public void Initialize()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(ScenesPath);
            foreach (var directory in Directory.EnumerateDirectories(ScenesPath).Order(StringComparer.Ordinal))
            {
                var path = Path.Combine(directory, "scene.json");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException("场景目录不能是链接。");
                    }

                    var scene = ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(path);
                    if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) || id != scene.SceneId || id == Guid.Empty)
                    {
                        throw new InvalidDataException("场景标识与文件名不一致。");
                    }

                    _scenes.Add(id, new SceneInfo(id, scene.Name));
                    _savedSnapshots[id] = Serialize(scene);
                }
                catch (Exception exception)
                {
                    _loadErrors.Add($"{Path.GetFileName(directory)}/scene.json：{exception.Message}");
                    _logger.LogError(exception, "无法加载场景：{Path}", path);
                }
            }

            if (_scenes.Count == 0)
            {
                var scene = DefaultSceneFactory.Create("默认");
                WriteSnapshot(scene.SceneId, Serialize(scene));
                _scenes.Add(scene.SceneId, new SceneInfo(scene.SceneId, scene.Name));
            }

            if (!_scenes.ContainsKey(ActiveSceneId))
            {
                _settings.Settings.ActiveSceneId = _scenes.Keys.First();
            }

            if (_settings.Settings.LastEditedSceneId is not { } editedId || !_scenes.ContainsKey(editedId))
            {
                _settings.Settings.LastEditedSceneId = ActiveSceneId;
            }

            _initialized = true;
        }
    }

    public IReadOnlyList<SceneInfo> GetScenes()
    {
        lock (_sync)
        {
            EnsureReady();
            return _scenes.Values.OrderBy(scene => scene.Name, StringComparer.CurrentCulture).ThenBy(scene => scene.Id).ToArray();
        }
    }

    public void Reload()
    {
        FlushPending();
        lock (_sync)
        {
            EnsureReady();
            if (_pending.Count != 0)
            {
                throw new IOException("存在尚未保存的场景，请先解决保存失败问题。");
            }

            // The editor and lock screen can run in separate processes. Re-read
            // the active selection before rebuilding the scene index.
            _settings.ReloadSettings();
            _scenes.Clear();
            _savedSnapshots.Clear();
            _saveErrors.Clear();
            _loadErrors.Clear();
            _initialized = false;
            Initialize();
        }

        ActiveSceneChanged?.Invoke(this, new SceneChangedEventArgs(ActiveSceneId));
    }

    public bool HasPendingChanges(Guid id)
    {
        lock (_sync)
        {
            return _pending.ContainsKey(id);
        }
    }

    public Exception? GetSaveError(Guid id)
    {
        lock (_sync)
        {
            return _saveErrors.GetValueOrDefault(id);
        }
    }

    public SceneSettings Load(Guid id)
    {
        lock (_sync)
        {
            RequireScene(id);
            if (_pending.TryGetValue(id, out var snapshot))
            {
                using var stream = new MemoryStream(snapshot, writable: false);
                return ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(stream);
            }

            var path = GetPath(id);
            // The helper creates defaults for absent files; an existing scene must never be replaced that way.
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("场景文件已被移除。", path);
            }

            var scene = ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(path);
            if (scene.SceneId != id)
            {
                throw new InvalidDataException("场景文件的标识发生了变化。");
            }

            return scene;
        }
    }

    public SceneSettings Create(string name)
    {
        lock (_sync)
        {
            EnsureReady();
        }

        var scene = DefaultSceneFactory.Create(ValidateName(name));
        Add(scene);
        return scene;
    }

    public SceneSettings Duplicate(Guid id)
    {
        var scene = Load(id);
        var sourceDirectory = GetSceneDirectory(id);
        scene.SceneId = Guid.NewGuid();
        scene.Name += " 副本";
        // Relative resource paths keep the copied scene independent of the original.
        CopyResources(sourceDirectory, GetDirectory(scene.SceneId));
        Add(scene);
        return scene;
    }

    private void Add(SceneSettings scene)
    {
        lock (_sync)
        {
            EnsureReady();
            WriteSnapshot(scene.SceneId, Serialize(scene));
            _scenes.Add(scene.SceneId, new SceneInfo(scene.SceneId, scene.Name));
        }

        SceneSaved?.Invoke(this, new SceneChangedEventArgs(scene.SceneId));
    }

    public void Delete(Guid id)
    {
        Guid activeId;
        lock (_sync)
        {
            RequireScene(id);
            if (_scenes.Count == 1)
            {
                throw new InvalidOperationException("请至少保留一个场景。");
            }

            var directory = GetSceneDirectory(id);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("无法删除作为链接的场景目录。");
            }

            // GetSceneDirectory accepts only known GUIDs and verifies the resolved root.
            Directory.Delete(directory, recursive: true);
            _pending.Remove(id);
            _savedSnapshots.Remove(id);
            _saveErrors.Remove(id);
            _scenes.Remove(id);
            activeId = _scenes.Keys.First();
            if (ActiveSceneId == id)
            {
                _settings.Settings.ActiveSceneId = activeId;
            }
            else
            {
                activeId = ActiveSceneId;
            }

            if (_settings.Settings.LastEditedSceneId == id)
            {
                _settings.Settings.LastEditedSceneId = activeId;
            }
        }

        ActiveSceneChanged?.Invoke(this, new SceneChangedEventArgs(activeId));
        SceneSaved?.Invoke(this, new SceneChangedEventArgs(id));
    }

    public void SetActive(Guid id)
    {
        lock (_sync)
        {
            RequireScene(id);
            // Validate the selected file before changing the persisted choice.
            _ = Load(id);
            if (ActiveSceneId == id)
            {
                return;
            }

            _settings.Settings.ActiveSceneId = id;
        }

        ActiveSceneChanged?.Invoke(this, new SceneChangedEventArgs(id));
    }

    public void QueueSave(SceneSettings scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _ = ValidateName(scene.Name);
        // Serialize on the caller's view thread, before the background timer can run.
        var snapshot = Serialize(scene);
        lock (_sync)
        {
            RequireScene(scene.SceneId);
            if (_pending.TryGetValue(scene.SceneId, out var queued) && queued.AsSpan().SequenceEqual(snapshot))
            {
                return;
            }

            if (!_pending.ContainsKey(scene.SceneId) && _savedSnapshots.TryGetValue(scene.SceneId, out var saved) && saved.AsSpan().SequenceEqual(snapshot))
            {
                return;
            }

            _pending[scene.SceneId] = snapshot;
            _saveErrors.Remove(scene.SceneId);
            _scenes[scene.SceneId] = new SceneInfo(scene.SceneId, scene.Name);
            if (!_saveScheduled)
            {
                _saveScheduled = true;
                _saveTimer.Change(400, Timeout.Infinite);
            }
        }
    }

    public void FlushPending()
    {
        List<SceneChangedEventArgs> results = [];
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _saveScheduled = false;
            foreach (var (id, snapshot) in _pending.ToArray())
            {
                try
                {
                    WriteSnapshot(id, snapshot);
                    _pending.Remove(id);
                    _saveErrors.Remove(id);
                    results.Add(new SceneChangedEventArgs(id));
                }
                catch (Exception exception)
                {
                    _saveErrors[id] = exception;
                    _logger.LogError(exception, "自动保存场景失败：{SceneId}", id);
                    results.Add(new SceneChangedEventArgs(id, exception));
                }
            }
        }

        foreach (var result in results)
        {
            if (result.Error == null)
            {
                SceneSaved?.Invoke(this, result);
            }
            else
            {
                SaveFailed?.Invoke(this, result);
            }
        }
    }

    private void WriteSnapshot(Guid id, byte[] snapshot)
    {
        var path = GetPath(id);
        Directory.CreateDirectory(GetDirectory(id));
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(snapshot);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
            _savedSnapshots[id] = snapshot;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string GetSceneDirectory(Guid id)
    {
        lock (_sync)
        {
            RequireScene(id);
            return GetDirectory(id);
        }
    }

    private string GetDirectory(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("场景标识不能为空。", nameof(id));
        }

        var root = Path.GetFullPath(ScenesPath) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(ScenesPath, id.ToString("N")));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("场景目录超出数据目录。");
        }

        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("场景目录不能是链接。");
        }

        return path;
    }

    private string GetPath(Guid id) => Path.Combine(GetDirectory(id), "scene.json");
    private static void CopyResources(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (name is "scene.json" or "scene.json.bak" or "scene.json.tmp")
            {
                continue;
            }

            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("无法复制场景中的链接资源。");
            }

            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                CopyResources(entry, target);
            }
            else
            {
                File.Copy(entry, target, overwrite: false);
            }
        }
    }

    private static byte[] Serialize(SceneSettings scene)
    {
        using var stream = new MemoryStream();
        ConfigureFileHelper.SaveConfig(stream, scene, writeIndented: true);
        return stream.ToArray();
    }

    private static string ValidateName(string name) => string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("场景名称不能为空。", nameof(name)) : name.Trim();
    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("场景服务尚未初始化。");
        }
    }

    private void RequireScene(Guid id)
    {
        EnsureReady();
        if (!_scenes.ContainsKey(id))
        {
            throw new KeyNotFoundException("场景不存在。");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            FlushPending();
            _disposed = true;
            _saveTimer.Dispose();
        }
    }
}
