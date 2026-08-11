# Embedded display architecture

## Current implementation

PocketBridge implements its own client for the official scrcpy-server 4.1 protocol. It does not embed or reparent the SDL window from `scrcpy.exe`.

For each selected Android serial, `ScrcpyTransport`:

1. pushes the unmodified official `scrcpy-server` to `/data/local/tmp/scrcpy-server.jar`;
2. creates a serial-scoped ADB forward tunnel to a random `scrcpy_<scid>` abstract socket;
3. starts `app_process ... com.genymobile.scrcpy.Server 4.1` with H.264 video, control enabled, and audio disabled;
4. validates the video connection with the protocol dummy byte before opening the control socket;
5. reads device, codec, session, and frame metadata using big-endian scrcpy 4.1 framing.

`FfmpegH264Decoder` decodes H.264 packets in the PocketBridge process through the FFmpeg libraries shipped in the official Windows release. H.26x configuration packets are retained and prepended to the following media packet, matching the official scrcpy packet merger. Decoded YUV420P/NV12 frames are converted to BGRA for WPF.

`AndroidDisplayControl` keeps one `WriteableBitmap` for the current dimensions and updates its pixels on the dispatcher. Only the newest pending frame is rendered, preventing dispatcher backlog. A new bitmap is allocated only when video dimensions change.

Decoded BGRA surfaces and encoded packet buffers use bounded `ArrayPool<byte>` ownership. Replacing a pending frame immediately returns the displaced buffer; `WritePixels` completes its copy before the rendered frame is returned. This avoids per-frame large-object-heap allocation and prevents FFmpeg-owned `AVFrame` memory from escaping the decoder call.

The renderer accepts at most one pending frame. It rejects a candidate whose decoded PTS is older than either the last rendered frame or a newer pending frame. Frame sequence and packet/decoded/rendered PTS are included in optional diagnostics, so a rollback cannot silently reach the WPF surface.

## Video diagnostics

Debug builds show an overlay with decoded/rendered FPS, dropped frames, pending count, decoder-to-render latency, PTS, and sequence. The overlay is compiled hidden in Release builds.

For sampled Release diagnostics, launch PocketBridge with:

```powershell
$env:POCKETBRIDGE_VIDEO_DIAGNOSTICS = '1'
```

One sample per second is written to `%LOCALAPPDATA%\PocketBridge\video-diagnostics.log`. Normal Release launches do not write this log.

## Input

The WPF surface maps letterboxed pointer coordinates back to current Android video coordinates and writes scrcpy 4.1 control messages for:

- touch down, move, and up;
- vertical/horizontal scroll;
- Android key down/up events;
- UTF-8 text injection;
- right-click Back.

Session and rotation metadata update the coordinate space without restarting the host window.

Portrait frames use almost all available height and align to the right with a small margin. Landscape frames remain centered with uniform scaling. Input mapping reads the actual arranged image bounds, so DPI, margins, and orientation changes use the same visible rectangle as rendering.

## Multi-device lifecycle

`EmbeddedSessionManager` owns a dictionary keyed by exact Android serial. Selecting another device changes only the displayed session reference; it does not stop inactive sessions. Stop and restart target only the selected serial. Every ADB operation and server process is explicitly serial-scoped.

## External fallback

The separate official `scrcpy.exe` client remains available only through **Open in a separate window**. Embedded Connect never launches `scrcpy.exe`. No HWND polling, `SetParent`, or window reparenting is used.

## Current scope

The first embedded milestone supports H.264 video and control input. Audio and clipboard synchronization are not implemented. FFmpeg software decoding is used; a hardware decoder may be added behind the same session boundary later.
