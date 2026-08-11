using System.Buffers;
using System.Diagnostics;

namespace PocketBridge.Core.Models;

public sealed class VideoFrame : IDisposable
{
    private byte[]? _buffer;

    public VideoFrame(int width, int height, int stride, byte[] buffer, int bufferLength, long presentationTimestamp, long packetPresentationTimestamp)
    {
        Width = width;
        Height = height;
        Stride = stride;
        _buffer = buffer;
        BufferLength = bufferLength;
        PresentationTimestamp = presentationTimestamp;
        PacketPresentationTimestamp = packetPresentationTimestamp;
        DecodedTimestamp = Stopwatch.GetTimestamp();
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(VideoFrame));
    public int BufferLength { get; }
    public long PresentationTimestamp { get; }
    public long PacketPresentationTimestamp { get; }
    public long DecodedTimestamp { get; }
    public long Sequence { get; set; }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
    }
}

public sealed class VideoFrameEventArgs(VideoFrame frame) : EventArgs
{
    public VideoFrame Frame { get; } = frame;
}

public enum AndroidTouchAction : byte
{
    Down = 0,
    Up = 1,
    Move = 2,
    Cancel = 3,
    HoverMove = 7
}

public enum AndroidKeyAction : byte
{
    Down = 0,
    Up = 1
}
