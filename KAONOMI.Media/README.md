# KAONOMI.Media

`kaonomi-media`: the videos of `/video` in the chat, with **AVFoundation** (the decoders of macOS, by the hardware:
H.264, HEVC, ProRes in mp4, mov, m4v…). One Swift file, no dependency; the chat builds it the first time it needs it
(and again when this source changes) into `~/kaonomi/video/bin/kaonomi-media`:

```bash
swiftc -O -swift-version 5 -suppress-warnings KAONOMI.Media/kaonomi-media.swift -o ~/kaonomi/video/bin/kaonomi-media
```

| Command | What it does |
|---|---|
| `probe <video>` | what the video is, as JSON: `duration`, `width`, `height` (turned as shown), `fps`, `hasVideo`, `hasAudio`, `playable` |
| `card <video> <out.jpg> <width>` | the same, and a thumbnail at 10 % of the video (the cards of the library) |
| `thumb <video> <seconds> <out.jpg> <width> [exact]` | the picture at that time (`width` 0: full size; `exact`: that very frame) |
| `strip <video> <count> <out.jpg> <height>` | a strip of pictures across the video (the timeline of the player) |
| `sheet <video> <start> <end> <count> <out.jpg> <cell width>` | the pictures of a segment on one sheet, in order, each numbered with its time (what the vision model reads for 🎬 ANALYSE SEGMENT) |
| `audio <video> <start> <end> <out.wav>` | the sound of a segment, 16 kHz mono 16 bits (the speech recognition, the voices) |
| `play <video> <start> <width>` | the video and its sound from `start`, on the standard output (the player) |

The stream of `play`: packets of a type (1 byte: `I` info, `V` video, `A` audio, `E` end), a time in seconds (Float64,
little endian), a length (UInt32, little endian) and the data: `I` is JSON, `V` a JPEG, `A` interleaved Float32 stereo
at 44100 Hz. At most 30 pictures per second, in the order of the time with the sound (about 14 times faster than the
video plays: the pipe holds it back while the chat plays or pauses).

**What macOS does not read** (webm, mkv, avi, wmv, flv… : VP8, VP9, AV1, Opus, Vorbis…) goes through **ffmpeg and
ffprobe**, when they are next to the tool (`~/kaonomi/video/bin/ffmpeg`, `~/kaonomi/video/bin/ffprobe`): the same
commands and the same stream (`"backend": "ffmpeg"` in what `probe` says, `"avfoundation"` otherwise). AVFoundation is
tried first (the decoders of the hardware). With ffmpeg: the pictures one by one (`-ss` before `-i`: exact and fast),
six at once for the strip and the sheet; the sound in WAV; for `play`, one ffmpeg for the pictures (raw RGB at 30 per
second, made JPEG by the tool) and one for the sound (Float32 stereo at 44100 Hz), merged in the order of the time.
Without ffmpeg, such videos are listed by `/video` but dimmed ("not readable").

The binaries in use are the static builds of FFmpeg 9.0.2 for Apple Silicon of
[martin-riedl.de](https://ffmpeg.martin-riedl.de) (signed, their SHA-256 checked against the ones published there).
To install them again:

```bash
B=https://ffmpeg.martin-riedl.de/redirect/latest/macos/arm64/release
curl -L -o /tmp/ffmpeg.zip $B/ffmpeg.zip && curl -L -o /tmp/ffprobe.zip $B/ffprobe.zip
unzip -o /tmp/ffmpeg.zip -d ~/kaonomi/video/bin && unzip -o /tmp/ffprobe.zip -d ~/kaonomi/video/bin
```
