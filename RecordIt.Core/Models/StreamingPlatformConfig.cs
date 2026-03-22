namespace RecordIt.Core.Models;

public enum PlatformId
{
    Twitch,
    YouTube,
    YouTubeShorts,
    Kick,
    TikTok,
    CustomRtmp,
}

/// <summary>Describes a single streaming destination.</summary>
public class StreamingPlatformConfig
{
    public PlatformId Id { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>RTMP server base URL (without stream key).</summary>
    public string RtmpBaseUrl { get; set; } = string.Empty;

    /// <summary>Stream key.  Stored encrypted in the database.</summary>
    public string StreamKey { get; set; } = string.Empty;

    /// <summary>Full ingest URL = RtmpBaseUrl + StreamKey.</summary>
    public string FullRtmpUrl => RtmpBaseUrl.TrimEnd('/') + '/' + StreamKey;

    /// <summary>When true the output is cropped/scaled to 9:16 (1080×1920).</summary>
    public bool IsVertical { get; set; }

    /// <summary>Enabled for the current streaming session.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Parameters that control how each stream is encoded.</summary>
public class StreamEncoderSettings
{
    public int VideoBitrateKbps { get; set; } = 6000;
    public int AudioBitrateKbps { get; set; } = 160;
    public int FrameRate { get; set; } = 30;
    public string Preset { get; set; } = "veryfast";
}

/// <summary>Full request handed to <see cref="MultiStreamService"/>.</summary>
public class StartStreamRequest
{
    public List<StreamingPlatformConfig> Platforms { get; set; } = [];
    public StreamEncoderSettings Encoder { get; set; } = new();

    /// <summary>
    /// Optional desktop capture source identifier (used on Windows with DirectShow/dshow).
    /// If empty the first available screen is captured.
    /// </summary>
    public string? CaptureSourceId { get; set; }
}
