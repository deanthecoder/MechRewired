// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Rendering;

/// <summary>Summary statistics for a measured sequence of frame times.</summary>
public sealed record BenchmarkFrameSummary(
    int Frames,
    double MeanFrameMs,
    double MedianFrameMs,
    double P95FrameMs,
    double P99FrameMs,
    double AverageFps,
    double OnePercentLowFps,
    double OverBudgetPercent);

/// <summary>Calculates repeatable summary metrics from measured frame times.</summary>
public static class BenchmarkFrameStatistics
{
    /// <summary>
    /// Summarizes positive finite frame times. Percentiles use the nearest-rank method
    /// (rank = ceil(p * N)); the 1% low is the FPS corresponding to the mean of the
    /// slowest ceil(N * 0.01) frames.
    /// </summary>
    public static BenchmarkFrameSummary Calculate(
        IEnumerable<double> frameMilliseconds,
        double frameBudgetMs = 1000.0 / 72.0)
    {
        ArgumentNullException.ThrowIfNull(frameMilliseconds);
        if (!double.IsFinite(frameBudgetMs) || frameBudgetMs <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameBudgetMs), "Frame budget must be positive and finite.");
        }

        var frames = frameMilliseconds.ToArray();
        if (frames.Length == 0)
        {
            throw new ArgumentException("At least one frame time is required.", nameof(frameMilliseconds));
        }

        foreach (var frameMs in frames)
        {
            if (!double.IsFinite(frameMs) || frameMs <= 0.0)
            {
                throw new ArgumentException("Frame times must be positive and finite.", nameof(frameMilliseconds));
            }
        }

        Array.Sort(frames);
        var mean = frames.Average();
        var median = frames.Length % 2 == 0
            ? (frames[frames.Length / 2 - 1] + frames[frames.Length / 2]) / 2.0
            : frames[frames.Length / 2];
        var slowestFrameCount = Math.Max(1, (int)Math.Ceiling(frames.Length * 0.01));
        var slowestMean = frames.Skip(frames.Length - slowestFrameCount).Average();
        var overBudgetCount = frames.Count(frameMs => frameMs > frameBudgetMs);

        return new BenchmarkFrameSummary(
            frames.Length,
            mean,
            median,
            NearestRank(frames, 0.95),
            NearestRank(frames, 0.99),
            1000.0 / mean,
            1000.0 / slowestMean,
            overBudgetCount * 100.0 / frames.Length);
    }

    private static double NearestRank(double[] sortedFrames, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sortedFrames.Length);
        return sortedFrames[rank - 1];
    }
}
