# Native Recorder (ffmpeg backend)

This WinUI implementation uses `ffmpeg` as the recording backend to provide reliable
screen + webcam + microphone capture and webcam overlay. `ffmpeg` must be installed
and available on the system PATH for recording to work.

Basic usage notes:
- Install ffmpeg for Windows: https://ffmpeg.org/download.html
- The recorder captures the primary desktop using `gdigrab` and will add webcam
  and microphone inputs via `dshow` if enabled in the UI.
- System (loopback) audio capture is platform/device dependent; to capture system audio
  you may need a virtual audio device (e.g. "Stereo Mix" or a virtual cable) and
  pass that device name in advanced settings (not yet exposed in the UI).

- Live preview: The UI can open a live preview window using `ffplay` (part of ffmpeg builds). Install `ffplay`/`ffmpeg` on PATH to enable preview.

Device probing:
- The service can probe dshow devices by calling `ScreenRecordingService.ProbeDshowDevicesAsync()`.

Limitations & next steps:
- This implementation focuses on a pragmatic, reliable recorder using ffmpeg. A pure
  Media Foundation / GraphicsCapture implementation can be added later if a fully
  native encoder/sink is desired.
