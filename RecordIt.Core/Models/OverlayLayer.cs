namespace RecordIt.Core.Models;

public enum FadeStyle
{
    None,
    Linear,
    EaseIn,
    EaseOut
}

public class OverlayLayer
{
    public string Path { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public int FadeInMs { get; set; }
    public int FadeOutMs { get; set; }
    public FadeStyle Fade { get; set; } = FadeStyle.None;
}
