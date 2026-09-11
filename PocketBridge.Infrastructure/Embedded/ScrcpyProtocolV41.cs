// Protocol constants and wire layouts are adapted from scrcpy 4.1.
// Copyright (C) 2018 Genymobile
// Copyright (C) 2018-2026 Romain Vimont
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Text;
using PocketBridge.Core.Models;

namespace PocketBridge.Infrastructure.Embedded;

internal static class ScrcpyProtocolV41
{
    public const string Version = "4.1";
    public const uint H264CodecId = 0x68323634;
    public const ulong ConfigFlag = 1UL << 62;
    public const ulong KeyFrameFlag = 1UL << 61;
    public const ulong SessionFlag = 1UL << 63;
    public const ulong PtsMask = KeyFrameFlag - 1;
    public const long MousePointerId = -1;

    public static byte[] Touch(AndroidTouchAction action, long pointerId, int x, int y, int width, int height, float pressure, uint buttons)
    {
        var data = new byte[32];
        data[0] = 2;
        data[1] = (byte)action;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(2), unchecked((ulong)pointerId));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(10), x);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(14), y);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18), checked((ushort)width));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(20), checked((ushort)height));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22), ToFixedPoint16(pressure));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), buttons);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), buttons);
        return data;
    }

    public static byte[] Key(AndroidKeyAction action, int keyCode, int repeat, int metaState)
    {
        var data = new byte[14];
        data[0] = 0;
        data[1] = (byte)action;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(2), keyCode);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(6), repeat);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(10), metaState);
        return data;
    }

    public static byte[] Scroll(int x, int y, int width, int height, float horizontal, float vertical, uint buttons)
    {
        var data = new byte[21];
        data[0] = 3;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1), x);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(5), y);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(9), checked((ushort)width));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(11), checked((ushort)height));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(13), ToSignedFixedPoint16(horizontal));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(15), ToSignedFixedPoint16(vertical));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(17), buttons);
        return data;
    }

    public static byte[] Text(string text)
    {
        var utf8 = Utf8Prefix(text, 300);
        return TextPayload(utf8);
    }

    public static IReadOnlyList<byte[]> TextMessages(string text)
    {
        if (text.Length == 0) return Array.Empty<byte[]>();
        var messages = new List<byte[]>();
        var chunk = new List<byte>(300);
        Span<byte> encoded = stackalloc byte[4];
        foreach (var rune in text.EnumerateRunes())
        {
            var length = rune.EncodeToUtf8(encoded);
            if (chunk.Count > 0 && chunk.Count + length > 300)
            {
                messages.Add(TextPayload(chunk.ToArray()));
                chunk.Clear();
            }
            for (var index = 0; index < length; index++) chunk.Add(encoded[index]);
        }
        if (chunk.Count > 0) messages.Add(TextPayload(chunk.ToArray()));
        return messages;
    }

    private static byte[] TextPayload(byte[] utf8)
    {
        var data = new byte[5 + utf8.Length];
        data[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1), (uint)utf8.Length);
        utf8.CopyTo(data, 5);
        return data;
    }

    public static byte[] GetClipboard() => new byte[] { 8, 0 };

    public static byte[] SetClipboard(string text, long sequence, bool paste)
    {
        var utf8 = Utf8Prefix(text, 262_130);
        var data = new byte[14 + utf8.Length];
        data[0] = 9;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(1), unchecked((ulong)sequence));
        data[9] = paste ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(10), (uint)utf8.Length);
        utf8.CopyTo(data, 14);
        return data;
    }

    private static byte[] Utf8Prefix(string text, int maxBytes)
    {
        var buffer = new byte[Math.Min(Encoding.UTF8.GetByteCount(text), maxBytes)];
        Encoding.UTF8.GetEncoder().Convert(text.AsSpan(), buffer.AsSpan(), true, out _, out var bytesUsed, out _);
        if (bytesUsed != buffer.Length) Array.Resize(ref buffer, bytesUsed);
        return buffer;
    }

    private static ushort ToFixedPoint16(float value) => value >= 1 ? ushort.MaxValue : value <= 0 ? (ushort)0 : (ushort)(value * 65536f);
    private static ushort ToSignedFixedPoint16(float value) => unchecked((ushort)(short)Math.Clamp((int)(value / 16f * 32768f), short.MinValue, short.MaxValue));
}
