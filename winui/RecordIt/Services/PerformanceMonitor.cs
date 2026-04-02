using System;
using System.Diagnostics;
using System.Threading;

namespace RecordIt.Services;

public class PerformanceStats
{
    public double CpuUsage { get; set; }
    public long MemoryUsageMB { get; set; }
    public int CurrentFps { get; set; }
    public int TargetFps { get; set; }
    public int DroppedFrames { get;set; }
    public int TotalFrames { get; set; }
    public double EncodingLagMs { get; set; }
    public long BitrateKbps { get; set; }
}

public class PerformanceMonitor : IDisposable
{
    private readonly PerformanceCounter? _cpuCounter;
    private readonly Process _currentProcess;
    private Timer? _monitorTimer;
    
    private int _frameCount;
    private int _droppedFrames;
    private DateTime _lastFpsCheck = DateTime.Now;
    private double _currentFps;
    
    public event EventHandler<PerformanceStats>? StatsUpdated;

    public PerformanceMonitor()
    {
        _currentProcess = Process.GetCurrentProcess();
        
        // Try to create CPU performance counter (may fail on some systems)
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _cpuCounter.NextValue(); // First call always returns 0, prime it
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    public void Start(int updateIntervalMs = 1000)
    {
        _monitorTimer = new Timer(_ => UpdateStats(), null, 0, updateIntervalMs);
    }

    public void Stop()
    {
        _monitorTimer?.Dispose();
        _monitorTimer = null;
    }

    public void RecordFrame(bool dropped = false)
    {
        _frameCount++;
        if (dropped) _droppedFrames++;
    }

    private void UpdateStats()
    {
        try
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsCheck).TotalSeconds;
            
            if (elapsed >= 1.0)
            {
                _currentFps = _frameCount / elapsed;
                _lastFpsCheck = now;
                _frameCount = 0;
            }

            _currentProcess.Refresh();
            
            var stats = new PerformanceStats
            {
                CpuUsage = _cpuCounter?.NextValue() ?? 0,
                MemoryUsageMB = _currentProcess.WorkingSet64 / (1024 * 1024),
                CurrentFps = (int)_currentFps,
                TargetFps = 60, // Can be set from encoder settings
                DroppedFrames = _droppedFrames,
                TotalFrames = _frameCount,
                EncodingLagMs = 0, // Updated from encoder
                BitrateKbps = 0 // Updated from stream/encoder
            };

            StatsUpdated?.Invoke(this, stats);
        }
        catch
        {
            // Ignore errors in monitoring
        }
    }

    public void Dispose()
    {
        Stop();
        _cpuCounter?.Dispose();
        _currentProcess?.Dispose();
    }
}
