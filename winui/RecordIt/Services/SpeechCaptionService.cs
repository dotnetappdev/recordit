using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace RecordIt.Services;

// ── Caption style presets ────────────────────────────────────────────────────

/// <summary>Visual preset that controls colours, background and typography.</summary>
public enum CaptionStyle
{
    /// <summary>White text · semi-transparent black bg · rounded pill (default)</summary>
    Classic,
    /// <summary>Black text · solid yellow bg — high contrast broadcast style (BBC / accessibility)</summary>
    Broadcast,
    /// <summary>White text · black outline only, no background — cinema / subtitle style</summary>
    Cinema,
    /// <summary>Full-width bar pinned to bottom with slide-up motion</summary>
    LowerThird,
    /// <summary>Large white text · solid black bg — maximum accessibility readability</summary>
    Accessible,
    /// <summary>Cyan/magenta neon glow — streamer / gaming aesthetic</summary>
    Neon,
    /// <summary>User-defined colours from TextColor / BgOpacity properties</summary>
    Custom,
}

// ── Caption configuration (persisted via SettingsService) ────────────────────

public sealed class CaptionConfig
{
    /// <summary>Spoken language for recognition, e.g. "en-US", "fr-FR".</summary>
    public string Language      { get; set; } = "en-US";

    /// <summary>Visual preset (overrides TextColor/BgOpacity when not Custom).</summary>
    public CaptionStyle Style   { get; set; } = CaptionStyle.Classic;

    /// <summary>Caption display font size (pt). Overridden by Accessible preset.</summary>
    public double FontSize      { get; set; } = 22;

    /// <summary>Caption text colour key used in Custom style: "White" | "Yellow" | "Cyan" | "Green" | "Red" | "Orange".</summary>
    public string TextColor     { get; set; } = "White";

    /// <summary>Background panel opacity 0–1, used in Custom style.</summary>
    public double BgOpacity     { get; set; } = 0.55;

    /// <summary>"Bottom" | "Top" position in the preview canvas.</summary>
    public string Position      { get; set; } = "Bottom";

    /// <summary>Seconds before auto-clearing a caption line (0 = never).</summary>
    public int    ClearAfterSec { get; set; } = 4;

    /// <summary>
    /// When true the caption text is burned into the recording output via
    /// an ffmpeg drawtext filter added at recording start.
    /// </summary>
    public bool BurnIntoRecording { get; set; } = false;

    /// <summary>Maximum characters per caption line before truncating.</summary>
    public int MaxLineChars { get; set; } = 80;
}

// ── Speech caption service ────────────────────────────────────────────────────

/// <summary>
/// Wraps Windows.Media.SpeechRecognition for live continuous dictation.
/// Fires <see cref="CaptionTextChanged"/> on the thread-pool; callers must
/// marshal to the UI thread themselves.
/// Also tracks timestamped entries so callers can export an SRT subtitle file.
/// </summary>
public sealed class SpeechCaptionService : IDisposable
{
    private SpeechRecognizer? _recognizer;

    // ── SRT tracking ─────────────────────────────────────────────────────────
    private DateTime _sessionStart;
    private TimeSpan _lastEntryEnd = TimeSpan.Zero;
    private readonly List<(TimeSpan Start, TimeSpan End, string Text)> _entries = new();

    // ── Public state ─────────────────────────────────────────────────────────
    public bool IsRunning { get; private set; }
    public CaptionConfig Config { get; set; } = new();

    /// <summary>True when at least one caption entry has been recorded this session.</summary>
    public bool HasSubtitles => _entries.Count > 0;

    // ── Events ───────────────────────────────────────────────────────────────
    /// <summary>Raised every time a confident recognition result arrives.</summary>
    public event EventHandler<string>? CaptionTextChanged;

    /// <summary>Raised when an error prevents recognition from starting or continuing.</summary>
    public event EventHandler<string>? ErrorOccurred;

    // ── Start / Stop ─────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        if (IsRunning) return;
        try
        {
            var lang = new Language(Config.Language);
            _recognizer = new SpeechRecognizer(lang);

            // Dictation topic gives good general-purpose accuracy
            _recognizer.Constraints.Add(
                new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation, "dictation"));

            var compile = await _recognizer.CompileConstraintsAsync();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
            {
                ErrorOccurred?.Invoke(this, $"Speech compile failed: {compile.Status}");
                return;
            }

            // Reset SRT tracking for this session
            _sessionStart  = DateTime.UtcNow;
            _lastEntryEnd  = TimeSpan.Zero;
            _entries.Clear();

            _recognizer.ContinuousRecognitionSession.ResultGenerated += OnResult;
            _recognizer.ContinuousRecognitionSession.Completed       += OnCompleted;
            await _recognizer.ContinuousRecognitionSession.StartAsync();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning || _recognizer == null) return;
        try
        {
            _recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResult;
            _recognizer.ContinuousRecognitionSession.Completed       -= OnCompleted;
            await _recognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch { /* already stopped */ }
        finally
        {
            IsRunning = false;
        }
    }

    // ── SRT export ───────────────────────────────────────────────────────────

    /// <summary>Returns an SRT-formatted string of all recognised phrases this session.</summary>
    public string GetSrtContent()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _entries.Count; i++)
        {
            var (start, end, text) = _entries[i];
            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{FormatSrt(start)} --> {FormatSrt(end)}");
            sb.AppendLine(text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Clears the subtitle history without stopping recognition.</summary>
    public void ClearHistory()
    {
        _entries.Clear();
        _lastEntryEnd = TimeSpan.Zero;
    }

    private static string FormatSrt(TimeSpan ts) =>
        $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";

    // ── Internal events ──────────────────────────────────────────────────────

    private void OnResult(
        SpeechContinuousRecognitionSession session,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Confidence == SpeechRecognitionConfidence.Rejected) return;
        var text = args.Result.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Truncate to MaxLineChars
        if (text.Length > Config.MaxLineChars)
            text = text[..Config.MaxLineChars] + "…";

        // Record timestamped entry for SRT export
        var end   = DateTime.UtcNow - _sessionStart;
        var start = _lastEntryEnd == TimeSpan.Zero
            ? end - TimeSpan.FromSeconds(2.5)         // estimate phrase duration
            : _lastEntryEnd + TimeSpan.FromMilliseconds(80); // small gap after previous
        if (start < TimeSpan.Zero)  start = TimeSpan.Zero;
        if (start >= end)           start = end - TimeSpan.FromMilliseconds(500);
        _entries.Add((start, end, text));
        _lastEntryEnd = end;

        CaptionTextChanged?.Invoke(this, text);
    }

    private void OnCompleted(
        SpeechContinuousRecognitionSession session,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        IsRunning = false;
        if (args.Status != SpeechRecognitionResultStatus.Success &&
            args.Status != SpeechRecognitionResultStatus.UserCanceled)
        {
            ErrorOccurred?.Invoke(this, $"Recognition ended: {args.Status}");
        }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        try { _recognizer?.Dispose(); } catch { }
        _recognizer = null;
    }
}
