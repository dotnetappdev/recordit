namespace RecordIt.Core.Models;

/// <summary>Represents a time range in seconds (start inclusive, end exclusive).</summary>
public readonly record struct TimeRange(double StartSec, double EndSec)
{
    public double DurationSec => EndSec - StartSec;
}

