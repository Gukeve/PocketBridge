using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App.Controls;

public partial class AndroidDisplayControl : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(IEmbeddedDisplaySession), typeof(AndroidDisplayControl),
        new PropertyMetadata(null, OnSessionChanged));
    public static readonly DependencyProperty MappingProfileProperty = DependencyProperty.Register(nameof(MappingProfile), typeof(InputProfile), typeof(AndroidDisplayControl), new PropertyMetadata(null, OnMappingChanged));
    public static readonly DependencyProperty IsMappingEditModeProperty = DependencyProperty.Register(nameof(IsMappingEditMode), typeof(bool), typeof(AndroidDisplayControl), new PropertyMetadata(false, OnMappingChanged));
    public event EventHandler<InputProfile>? MappingProfileEdited;

    private WriteableBitmap? _bitmap;
    private VideoFrame? _latestFrame;
    private IEmbeddedDisplaySession? _sessionReference;
    private int _renderPending;
    private bool _pointerDown;
    private long _lastRenderedPts = long.MinValue;
    private long _lastRenderedSequence;
    private long _decodedFrames;
    private long _renderedFrames;
    private long _droppedFrames;
    private long _metricsDecodedBase;
    private long _metricsRenderedBase;
    private long _metricsTimestamp = Stopwatch.GetTimestamp();
    private double _lastLatencyMs;
    private int _maximumPendingFrames;
    private long _lastDecodedPts;
    private long _lastPacketPts;
    private long _lastDecodedSequence;

    public AndroidDisplayControl()
    {
        InitializeComponent();
        InitializeEditor();
        SizeChanged += (_, _) => { UpdateVideoLayout(); RefreshOverlay(); };
#if DEBUG
        MetricsOverlay.Visibility = Visibility.Visible;
#endif
    }

    [Bindable(true)]
    public IEmbeddedDisplaySession? Session
    {
        get => (IEmbeddedDisplaySession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }
    public InputProfile? MappingProfile { get => (InputProfile?)GetValue(MappingProfileProperty); set => SetValue(MappingProfileProperty, value); }
    public bool IsMappingEditMode { get => (bool)GetValue(IsMappingEditModeProperty); set => SetValue(IsMappingEditModeProperty, value); }
    private static void OnMappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AndroidDisplayControl)d).RefreshOverlay();

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AndroidDisplayControl)d;
        if (e.OldValue is IEmbeddedDisplaySession oldSession) oldSession.FrameReady -= control.OnFrameReady;
        if (e.NewValue is IEmbeddedDisplaySession newSession) newSession.FrameReady += control.OnFrameReady;
        Volatile.Write(ref control._sessionReference, e.NewValue as IEmbeddedDisplaySession);
        Interlocked.Exchange(ref control._latestFrame, null)?.Dispose();
        control._bitmap = null;
        control._lastRenderedPts = long.MinValue;
        control._lastRenderedSequence = 0;
        control._decodedFrames = control._renderedFrames = control._droppedFrames = 0;
        control._maximumPendingFrames = 0;
        control._metricsDecodedBase = control._metricsRenderedBase = 0;
        control._metricsTimestamp = Stopwatch.GetTimestamp();
        control.VideoImage.Source = null;
        control.UpdateVideoLayout();
        control.EmptyState.Visibility = e.NewValue is IEmbeddedDisplaySession { IsRunning: true } ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnFrameReady(object? sender, VideoFrameEventArgs e)
    {
        if (!ReferenceEquals(sender, Volatile.Read(ref _sessionReference)))
        {
            e.Frame.Dispose();
            return;
        }
        Interlocked.Increment(ref _decodedFrames);
        Volatile.Write(ref _lastPacketPts, e.Frame.PacketPresentationTimestamp);
        Volatile.Write(ref _lastDecodedPts, e.Frame.PresentationTimestamp);
        Volatile.Write(ref _lastDecodedSequence, e.Frame.Sequence);
        var lastPts = Volatile.Read(ref _lastRenderedPts);
        var lastSequence = Volatile.Read(ref _lastRenderedSequence);
        if ((e.Frame.PresentationTimestamp >= 0 && e.Frame.PresentationTimestamp < lastPts) || e.Frame.Sequence < lastSequence)
        {
            Interlocked.Increment(ref _droppedFrames);
            e.Frame.Dispose();
            return;
        }

        while (true)
        {
            var pending = Volatile.Read(ref _latestFrame);
            if (pending is not null && IsOlder(e.Frame, pending))
            {
                Interlocked.Increment(ref _droppedFrames);
                e.Frame.Dispose();
                return;
            }
            if (Interlocked.CompareExchange(ref _latestFrame, e.Frame, pending) != pending) continue;
            if (pending is not null)
            {
                Interlocked.Increment(ref _droppedFrames);
                pending.Dispose();
            }
            Interlocked.Exchange(ref _maximumPendingFrames, 1);
            break;
        }
        ScheduleRender();
    }

    private static bool IsOlder(VideoFrame candidate, VideoFrame reference) =>
        candidate.PresentationTimestamp >= 0 && reference.PresentationTimestamp >= 0
            ? candidate.PresentationTimestamp < reference.PresentationTimestamp
            : candidate.Sequence < reference.Sequence;

    private void ScheduleRender()
    {
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
            _ = Dispatcher.BeginInvoke(RenderLatest, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void RenderLatest()
    {
        VideoFrame? frame = null;
        try
        {
            frame = Interlocked.Exchange(ref _latestFrame, null);
            if (frame is null) return;
            if ((frame.PresentationTimestamp >= 0 && frame.PresentationTimestamp < _lastRenderedPts) || frame.Sequence < _lastRenderedSequence)
            {
                Interlocked.Increment(ref _droppedFrames);
                return;
            }
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
                VideoImage.Source = _bitmap;
                UpdateVideoLayout();
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Buffer, frame.Stride, 0);
            _lastRenderedPts = frame.PresentationTimestamp;
            _lastRenderedSequence = frame.Sequence;
            Interlocked.Increment(ref _renderedFrames);
            _lastLatencyMs = Stopwatch.GetElapsedTime(frame.DecodedTimestamp).TotalMilliseconds;
            EmptyState.Visibility = Visibility.Collapsed;
            UpdateMetrics();
        }
        finally
        {
            frame?.Dispose();
            Interlocked.Exchange(ref _renderPending, 0);
            if (Volatile.Read(ref _latestFrame) is not null) ScheduleRender();
        }
    }

    private void UpdateMetrics()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_metricsTimestamp, now).TotalSeconds;
        if (elapsed < 1) return;
        var decoded = Interlocked.Read(ref _decodedFrames);
        var rendered = Interlocked.Read(ref _renderedFrames);
        var decodedFps = (decoded - _metricsDecodedBase) / elapsed;
        var renderedFps = (rendered - _metricsRenderedBase) / elapsed;
        var dropped = Interlocked.Read(ref _droppedFrames);
        var pendingCount = Volatile.Read(ref _latestFrame) is null ? 0 : 1;
        var text = string.Format(CultureInfo.InvariantCulture,
            "Decoded FPS: {0:F1}\nRendered FPS: {1:F1}\nDropped: {2}\nPending: {3} (max {4})\nLatency: {5:F1} ms\nPTS: {6} Seq: {7}",
            decodedFps, renderedFps, dropped, pendingCount, _maximumPendingFrames, _lastLatencyMs, _lastRenderedPts, _lastRenderedSequence);
        MetricsText.Text = text;
        _metricsDecodedBase = decoded;
        _metricsRenderedBase = rendered;
        _metricsTimestamp = now;

        if (Environment.GetEnvironmentVariable("POCKETBRIDGE_VIDEO_DIAGNOSTICS") == "1")
        {
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketBridge");
            var line = FormattableString.Invariant($"{DateTimeOffset.Now:O} serial={Session?.Serial} decoded_fps={decodedFps:F1} rendered_fps={renderedFps:F1} dropped={dropped} pending={pendingCount} max_pending={_maximumPendingFrames} latency_ms={_lastLatencyMs:F1} packet_pts={Volatile.Read(ref _lastPacketPts)} decoded_pts={Volatile.Read(ref _lastDecodedPts)} decoded_sequence={Volatile.Read(ref _lastDecodedSequence)} rendered_pts={_lastRenderedPts} rendered_sequence={_lastRenderedSequence}{Environment.NewLine}");
            _ = Task.Run(() => { Directory.CreateDirectory(logDirectory); File.AppendAllText(Path.Combine(logDirectory, "video-diagnostics.log"), line); });
        }
    }

    private void UpdateVideoLayout()
    {
        VideoImage.HorizontalAlignment = HorizontalAlignment.Stretch;
        VideoImage.VerticalAlignment = VerticalAlignment.Stretch;
        VideoImage.Margin = new Thickness(8);
        VideoImage.ClearValue(WidthProperty);
        VideoImage.ClearValue(HeightProperty);
    }

    private bool TryMap(Point point, out int x, out int y)
    {
        x = y = 0;
        var session = Session;
        if (session is not { IsRunning: true, VideoWidth: > 0, VideoHeight: > 0 }) return false;
        var imageOrigin = VideoImage.TranslatePoint(new Point(), this);
        var scale = Math.Min(VideoImage.ActualWidth / session.VideoWidth, VideoImage.ActualHeight / session.VideoHeight);
        var displayedWidth = session.VideoWidth * scale;
        var displayedHeight = session.VideoHeight * scale;
        var left = imageOrigin.X + (VideoImage.ActualWidth - displayedWidth) / 2;
        var top = imageOrigin.Y + (VideoImage.ActualHeight - displayedHeight) / 2;
        if (point.X < left || point.Y < top || point.X >= left + displayedWidth || point.Y >= top + displayedHeight) return false;
        x = Math.Clamp((int)((point.X - left) / scale), 0, session.VideoWidth - 1);
        y = Math.Clamp((int)((point.Y - top) / scale), 0, session.VideoHeight - 1);
        return true;
    }

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsMappingEditMode) return;
        Focus();
        var mapped = MappingProfile?.Bindings.FirstOrDefault(item => item.SourceKind == InputSourceKind.MouseButton && string.Equals(item.Input, "Left", StringComparison.OrdinalIgnoreCase));
        if (mapped is not null) { await ExecuteMappedAsync(mapped.Action, true); e.Handled = true; return; }
        if (!TryMap(e.GetPosition(this), out var x, out var y) || Session is null) return;
        CaptureMouse();
        _pointerDown = true;
        await Session.SendTouchAsync(AndroidTouchAction.Down, -2, x, y, 1, 0);
        e.Handled = true;
    }

    private async void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || !TryMap(e.GetPosition(this), out var x, out var y) || Session is null) return;
        await Session.SendTouchAsync(AndroidTouchAction.Move, -2, x, y, 1, 0);
        e.Handled = true;
    }

    private async void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mapped = MappingProfile?.Bindings.FirstOrDefault(item => item.SourceKind == InputSourceKind.MouseButton && string.Equals(item.Input, "Left", StringComparison.OrdinalIgnoreCase));
        if (mapped is not null) { await ExecuteMappedAsync(mapped.Action, false); e.Handled = true; return; }
        if (!_pointerDown || Session is null) return;
        if (TryMap(e.GetPosition(this), out var x, out var y)) await Session.SendTouchAsync(AndroidTouchAction.Up, -2, x, y, 0, 0);
        _pointerDown = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private async void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!TryMap(e.GetPosition(this), out var x, out var y) || Session is null) return;
        await Session.SendScrollAsync(x, y, 0, Math.Sign(e.Delta));
        e.Handled = true;
    }

    private async void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Session is null) return;
        var mapped = MappingProfile?.Bindings.FirstOrDefault(item => item.SourceKind == InputSourceKind.MouseButton && string.Equals(item.Input, "Right", StringComparison.OrdinalIgnoreCase));
        if (mapped is not null) { await ExecuteMappedAsync(mapped.Action, true); await ExecuteMappedAsync(mapped.Action, false); e.Handled = true; return; }
        await Session.SendKeyAsync(AndroidKeyAction.Down, 4);
        await Session.SendKeyAsync(AndroidKeyAction.Up, 4);
        e.Handled = true;
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleEditorKey(e)) { e.Handled = true; return; }
        if (Session is null) return;
        var mapped = MappingProfile?.Bindings.FirstOrDefault(item => item.SourceKind == InputSourceKind.KeyboardKey && string.Equals(item.Input, e.Key.ToString(), StringComparison.OrdinalIgnoreCase));
        if (mapped is not null) { await ExecuteMappedAsync(mapped.Action, true); e.Handled = true; return; }
        if (!TryAndroidKey(e.Key, out var keyCode)) return;
        await Session.SendKeyAsync(AndroidKeyAction.Down, keyCode, e.IsRepeat ? 1 : 0, MetaState());
        e.Handled = true;
    }

    private async void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (Session is null) return;
        var mapped = MappingProfile?.Bindings.FirstOrDefault(item => item.SourceKind == InputSourceKind.KeyboardKey && string.Equals(item.Input, e.Key.ToString(), StringComparison.OrdinalIgnoreCase));
        if (mapped is not null) { await ExecuteMappedAsync(mapped.Action, false); e.Handled = true; return; }
        if (!TryAndroidKey(e.Key, out var keyCode)) return;
        await Session.SendKeyAsync(AndroidKeyAction.Up, keyCode, 0, MetaState());
        e.Handled = true;
    }

    private async void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Session is null || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        await Session.SendTextAsync(e.Text);
        e.Handled = true;
    }

    private static int MetaState() =>
        (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 0) |
        (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 2 : 0) |
        (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 4096 : 0);

    private static bool TryAndroidKey(Key key, out int code)
    {
        code = key switch
        {
            Key.Escape => 4, Key.Enter => 66, Key.Back => 67, Key.Tab => 61, Key.Space => 62,
            Key.Left => 21, Key.Right => 22, Key.Up => 19, Key.Down => 20, Key.Delete => 112,
            Key.VolumeUp => 24, Key.VolumeDown => 25, Key.MediaPlayPause => 85,
            _ => 0
        };
        return code != 0;
    }

    private async Task ExecuteMappedAsync(InputAction action, bool pressed)
    {
        if (Session is null) return;
        if (action.Kind == InputActionKind.AndroidKey && int.TryParse(action.Value, out var keyCode)) await Session.SendKeyAsync(pressed ? AndroidKeyAction.Down : AndroidKeyAction.Up, keyCode);
        else if (action.Kind is InputActionKind.TouchPoint or InputActionKind.VirtualJoystick && action.Point is { } point)
        {
            var pixel = point.ToPixels(Session.VideoWidth, Session.VideoHeight); await Session.SendTouchAsync(pressed ? AndroidTouchAction.Down : AndroidTouchAction.Up, -10, pixel.X, pixel.Y, pressed ? 1 : 0, 0);
        }
        else if (action.Kind is InputActionKind.TouchRegion or InputActionKind.FreeLook && action.Region is { } region)
        {
            var center = new NormalizedPoint((region.Start.X + region.End.X) / 2, (region.Start.Y + region.End.Y) / 2).ToPixels(Session.VideoWidth, Session.VideoHeight); await Session.SendTouchAsync(pressed ? AndroidTouchAction.Down : AndroidTouchAction.Up, -12, center.X, center.Y, pressed ? 1 : 0, 0);
        }
        else if (action.Kind == InputActionKind.Swipe && pressed && action.Region is { } swipe)
        {
            var start = swipe.Start.ToPixels(Session.VideoWidth, Session.VideoHeight); var end = swipe.End.ToPixels(Session.VideoWidth, Session.VideoHeight); await Session.SendTouchAsync(AndroidTouchAction.Down, -13, start.X, start.Y); await Task.Delay(action.DurationMs); await Session.SendTouchAsync(AndroidTouchAction.Move, -13, end.X, end.Y); await Session.SendTouchAsync(AndroidTouchAction.Up, -13, end.X, end.Y, 0);
        }
    }

    private void OnOverlayClick(object sender, MouseButtonEventArgs e)
    {
        EditorCanvasDown(e);
    }

    private void RefreshOverlay()
    {
        EditorRefresh();
    }
}
