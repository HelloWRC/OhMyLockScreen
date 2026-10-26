namespace LibVLCSharp.Shared
{
    public enum FromType
    {
        FromPath
    }

    public enum Position
    {
        Disable
    }

    public enum VLCState
    {
        Opening,
        Buffering,
        Playing,
        Paused,
        Ended,
        Stopped
    }

    public sealed class LibVLC(params string[] options) : IDisposable
    {
        public string[] Options { get; } = options;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    public sealed class Media(LibVLC libVlc, string source, FromType type) : IDisposable
    {
        public LibVLC LibVlc { get; } = libVlc;
        public string Source { get; } = source;
        public FromType Type { get; } = type;
        public List<string> Options { get; } = [];
        public void AddOption(string option) => Options.Add(option);
        public void Dispose()
        {
        }
    }

    public sealed class MediaPlayerVoutEventArgs(uint count) : EventArgs
    {
        public uint Count { get; } = count;
    }

    public sealed class MediaPlayer(LibVLC libVlc) : IDisposable
    {
        public static bool FailOnPlay { get; set; }
        public LibVLC LibVlc { get; } = libVlc;
        public bool Mute { get; set; }
        public bool IsPlaying { get; private set; }
        public VLCState State { get; private set; } = VLCState.Stopped;
        public bool IsDisposed { get; private set; }
        public string? CropGeometry { get; set; }
        public Media? Media { get; private set; }
        public int PlayCalls { get; private set; }
        public int StopCalls { get; private set; }
        public uint VoutCount { get; private set; }
        public string? CropAtPlay { get; private set; }
        public bool InNativeCallback { get; private set; }
        public object EventSender { get; } = new();
        public event EventHandler<EventArgs>? EncounteredError;
        public event EventHandler<EventArgs>? EndReached;
        public event EventHandler<MediaPlayerVoutEventArgs>? Vout;
        public int Subscribers => (EncounteredError?.GetInvocationList().Length ?? 0)
            + (EndReached?.GetInvocationList().Length ?? 0)
            + (Vout?.GetInvocationList().Length ?? 0);

        public void SetVideoTitleDisplay(Position position, uint timeout)
        {
        }
        public bool Play(Media media)
        {
            Media = media;
            return Play();
        }

        public bool Play()
        {
            if (InNativeCallback)
            {
                throw new InvalidOperationException("Playback was restarted inside a native callback.");
            }

            CropAtPlay = CropGeometry;
            PlayCalls++;
            IsPlaying = !FailOnPlay;
            State = IsPlaying ? VLCState.Playing : VLCState.Stopped;
            return IsPlaying;
        }

        public void SetPause(bool pause)
        {
            IsPlaying = !pause;
            State = pause ? VLCState.Paused : VLCState.Playing;
        }
        public void Stop()
        {
            if (InNativeCallback)
            {
                throw new InvalidOperationException("Playback was stopped inside a native callback.");
            }

            StopCalls++;
            IsPlaying = false;
            State = VLCState.Stopped;
        }

        public void Fail() => EncounteredError?.Invoke(EventSender, EventArgs.Empty);
        public void OutputVideo(uint count = 1)
        {
            VoutCount = count;
            Vout?.Invoke(EventSender, new MediaPlayerVoutEventArgs(count));
        }

        public void Finish()
        {
            IsPlaying = false;
            State = VLCState.Ended;
            InNativeCallback = true;
            try
            {
                OutputVideo(0);
                EndReached?.Invoke(EventSender, EventArgs.Empty);
            }
            finally
            {
                InNativeCallback = false;
            }
        }
        public void Dispose() => IsDisposed = true;
    }
}

namespace LibVLCSharp.Platforms.Windows
{
    public sealed class InitializedEventArgs(string[] options) : EventArgs
    {
        public string[] SwapChainOptions { get; } = options;
    }

    public sealed class VideoView : global::Windows.UI.Xaml.Controls.Control
    {
        public bool IsHitTestVisible { get; set; }
        public bool IsTabStop { get; set; }
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer { get; set; }
        public event EventHandler<InitializedEventArgs>? Initialized;
        public void Initialize(params string[] options) => Initialized?.Invoke(this, new InitializedEventArgs(options));
    }
}
