namespace RecordIt.Encoder.Models;

public enum GpuVendor
{
    Unknown = 0,
    Nvidia  = 1,
    Amd     = 2,
    Intel   = 3,
}

/// <summary>GPU adapter information discovered via WMIC / Win32_VideoController.</summary>
public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor);
