using OhMyLockScreen.Shared.Controls.Scene;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.UI;

public sealed partial class BlobPicker : UserControl
{
    public static readonly DependencyProperty BlobIdProperty = DependencyProperty.Register(
        nameof(BlobId), typeof(string), typeof(BlobPicker), new PropertyMetadata(null, OnBlobIdChanged));

    public static readonly DependencyProperty BlobUriConverterProperty = DependencyProperty.Register(
        nameof(BlobUriConverter), typeof(SceneBlobUriConverter), typeof(BlobPicker),
        new PropertyMetadata(null, OnContextChanged));

    private CancellationTokenSource? _selection;
    private bool _loaded;
    private bool _publishing;

    public BlobPicker()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            UpdateButton();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            InvalidateSelection();
        };
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled)
            {
                InvalidateSelection();
            }
        };
    }

    public string? BlobId
    {
        get => (string?)GetValue(BlobIdProperty);
        set => SetValue(BlobIdProperty, value);
    }

    public SceneBlobUriConverter? BlobUriConverter
    {
        get => (SceneBlobUriConverter?)GetValue(BlobUriConverterProperty);
        set => SetValue(BlobUriConverterProperty, value);
    }

    public IReadOnlyList<string> FileTypes { get; set; } = [];
    public PickerLocationId SuggestedStartLocation { get; set; } = PickerLocationId.DocumentsLibrary;
    internal Func<Uri, Task>? ValidateAsync { get; set; }
    internal Func<Task<StorageFile?>>? SelectFileAsync { get; set; }

    public event EventHandler<string>? BlobPicked;
    public event EventHandler<Exception>? PickFailed;

    public void InvalidateSelection() => _selection?.Cancel();

    private static void OnBlobIdChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (BlobPicker)sender;
        if (!picker._publishing)
        {
            picker.InvalidateSelection();
        }
    }

    private static void OnContextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (BlobPicker)sender;
        picker.InvalidateSelection();
        picker.UpdateButton();
    }

    private void UpdateButton() => BrowseButton.IsEnabled = _loaded && _selection == null && BlobUriConverter != null;

    private async void OnBrowseClick(object sender, RoutedEventArgs args)
    {
        if (!_loaded || !IsEnabled || _selection != null || BlobUriConverter is not { } context || FileTypes.Count == 0)
        {
            return;
        }

        var selection = new CancellationTokenSource();
        _selection = selection;
        UpdateButton();
        var dispatcher = Dispatcher;
        var validate = ValidateAsync;
        string? blobId = null;
        string? fileName = null;
        Exception? error = null;
        try
        {
            var file = await (SelectFileAsync?.Invoke() ?? PickFileAsync());
            if (file != null && !selection.IsCancellationRequested)
            {
                fileName = file.Name;
                using var stream = await file.OpenStreamForReadAsync();
                blobId = await context.ImportAsync(stream, selection.Token);
                if (!selection.IsCancellationRequested && validate != null)
                {
                    var uri = context.Resolve(blobId) ?? throw new FileNotFoundException("导入的场景资产不存在。");
                    await validate(uri);
                }
            }
        }
        catch (Exception exception)
        {
            error = exception;
        }

        void Complete()
        {
            var isCurrent = _loaded && IsEnabled && !selection.IsCancellationRequested;
            _selection = null;
            UpdateButton();
            if (!isCurrent)
            {
                return;
            }

            if (error != null)
            {
                PickFailed?.Invoke(this, error);
            }
            else if (blobId != null)
            {
                _publishing = true;
                try
                {
                    BlobId = blobId;
                    if (_loaded && IsEnabled && ReferenceEquals(BlobUriConverter, context))
                    {
                        BlobPicked?.Invoke(this, fileName!);
                    }
                }
                finally
                {
                    _publishing = false;
                }
            }
        }

        try
        {
            if (dispatcher.HasThreadAccess)
            {
                Complete();
            }
            else
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Complete);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"无法完成场景资产选取：{exception}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _selection, null, selection);
            selection.Dispose();
        }
    }

    private async Task<StorageFile?> PickFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = SuggestedStartLocation };
        foreach (var extension in FileTypes)
        {
            picker.FileTypeFilter.Add(extension);
        }

        return await picker.PickSingleFileAsync();
    }
}
