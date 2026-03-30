namespace RecordIt.Core.Models;

/// <summary>
/// Simple video effect parameters mapped from the UI sliders.
/// Values are in normalized/semantic ranges (not raw slider 0..100).
/// </summary>
public sealed class VideoEffectConfig
{
    /// <summary>Brightness offset (roughly -0.2..+0.2). Neutral is 0.</summary>
    public float Brightness { get; set; }

    /// <summary>Saturation multiplier. Neutral is 1.</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>Vignette strength (0..1). Neutral is 0.</summary>
    public float VignetteStrength { get; set; }

    /// <summary>Sharpen amount (0..1). Neutral is 0.</summary>
    public float SharpenAmount { get; set; }

    /// <summary>Blur sigma (0..2). Neutral is 0.</summary>
    public float BlurSigma { get; set; }

    /// <summary>Noise reduction strength (0..1). Neutral is 0.</summary>
    public float NoiseReductionStrength { get; set; }
}

