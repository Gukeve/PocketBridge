using FFmpeg.AutoGen;
using PocketBridge.Core.Models;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PocketBridge.Infrastructure.Embedded;

internal sealed unsafe class FfmpegH264Decoder : IDisposable
{
    private AVCodecContext* _context;
    private AVPacket* _packet;
    private AVFrame* _frame;

    public FfmpegH264Decoder(string nativeLibraryDirectory)
    {
        ffmpeg.RootPath = nativeLibraryDirectory;
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec is null) throw new InvalidOperationException("FFmpeg H.264 decoder is unavailable.");
        _context = ffmpeg.avcodec_alloc_context3(codec);
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        if (_context is null || _packet is null || _frame is null) throw new OutOfMemoryException("FFmpeg decoder allocation failed.");
        _context->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        ThrowIfError(ffmpeg.avcodec_open2(_context, codec, null), "avcodec_open2");
    }

    public IReadOnlyList<VideoFrame> Decode(ReadOnlyMemory<byte> encoded, long pts, bool outputFrames = true)
    {
        var frames = new List<VideoFrame>();
        ThrowIfError(ffmpeg.av_new_packet(_packet, encoded.Length), "av_new_packet");
        encoded.Span.CopyTo(new Span<byte>(_packet->data, encoded.Length));
        _packet->pts = pts;
        _packet->dts = pts;
        var sendResult = ffmpeg.avcodec_send_packet(_context, _packet);
        ffmpeg.av_packet_unref(_packet);
        if (sendResult < 0 && sendResult != -11) ThrowIfError(sendResult, "avcodec_send_packet");

        while (true)
        {
            var result = ffmpeg.avcodec_receive_frame(_context, _frame);
            if (result is -11 || result == ffmpeg.AVERROR_EOF) return frames;
            ThrowIfError(result, "avcodec_receive_frame");
            if (outputFrames)
            {
                var framePts = _frame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
                    ? _frame->best_effort_timestamp
                    : _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : pts;
                frames.Add(ConvertFrame(_frame, framePts, pts));
            }
            ffmpeg.av_frame_unref(_frame);
        }
    }

    private static VideoFrame ConvertFrame(AVFrame* frame, long pts, long packetPts)
    {
        var width = frame->width;
        var height = frame->height;
        var stride = checked(width * 4);
        var bufferLength = checked(stride * height);
        var format = (AVPixelFormat)frame->format;
        if (format is not (AVPixelFormat.AV_PIX_FMT_YUV420P or AVPixelFormat.AV_PIX_FMT_YUVJ420P or AVPixelFormat.AV_PIX_FMT_NV12))
            throw new NotSupportedException($"FFmpeg returned unsupported pixel format {format}.");
        var output = ArrayPool<byte>.Shared.Rent(bufferLength);

        try
        {
            fixed (byte* destination = output)
            {
                for (var y = 0; y < height; y++)
                {
                    var yRow = frame->data[0] + y * frame->linesize[0];
                    var uRow = frame->data[1] + (y >> 1) * frame->linesize[1];
                    var vRow = format == AVPixelFormat.AV_PIX_FMT_NV12 ? null : frame->data[2] + (y >> 1) * frame->linesize[2];
                    var target = destination + y * stride;
                    for (var x = 0; x < width; x += 2)
                    {
                        var u = format == AVPixelFormat.AV_PIX_FMT_NV12 ? uRow[(x >> 1) * 2] : uRow[x >> 1];
                        var v = format == AVPixelFormat.AV_PIX_FMT_NV12 ? uRow[(x >> 1) * 2 + 1] : vRow![x >> 1];
                        var d = u - 128;
                        var e = v - 128;
                        WriteBgra(target + x * 4, yRow[x], d, e);
                        if (x + 1 < width) WriteBgra(target + (x + 1) * 4, yRow[x + 1], d, e);
                    }
                }
            }
            return new VideoFrame(width, height, stride, output, bufferLength, pts, packetPts);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(output);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBgra(byte* target, int luma, int d, int e)
    {
        var c = luma - 16;
        if (c < 0) c = 0;
        target[0] = Clamp((298 * c + 516 * d + 128) >> 8);
        target[1] = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
        target[2] = Clamp((298 * c + 409 * e + 128) >> 8);
        target[3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value) => (uint)value <= byte.MaxValue ? (byte)value : value < 0 ? (byte)0 : byte.MaxValue;

    private static void ThrowIfError(int error, string operation)
    {
        if (error >= 0) return;
        var buffer = stackalloc byte[1024];
        ffmpeg.av_strerror(error, buffer, 1024);
        throw new InvalidOperationException($"FFmpeg {operation} failed: {System.Text.Encoding.UTF8.GetString(buffer, 1024).TrimEnd('\0')}");
    }

    public void Dispose()
    {
        if (_frame is not null) { var frame = _frame; ffmpeg.av_frame_free(&frame); _frame = null; }
        if (_packet is not null) { var packet = _packet; ffmpeg.av_packet_free(&packet); _packet = null; }
        if (_context is not null) { var context = _context; ffmpeg.avcodec_free_context(&context); _context = null; }
    }
}
