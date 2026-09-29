// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Rendering;
using NUnit.Framework;

namespace MechRewired.Tests.Rendering;

[TestFixture]
public sealed class BenchmarkFrameStatisticsTests
{
    [Test]
    public void CalculatesFrameAndFpsStatisticsUsingNearestRankPercentiles()
    {
        var summary = BenchmarkFrameStatistics.Calculate(
            Enumerable.Range(1, 10).Select(value => value * 10.0),
            frameBudgetMs: 50.0);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Frames, Is.EqualTo(10));
            Assert.That(summary.MeanFrameMs, Is.EqualTo(55.0));
            Assert.That(summary.MedianFrameMs, Is.EqualTo(55.0));
            Assert.That(summary.P95FrameMs, Is.EqualTo(100.0));
            Assert.That(summary.P99FrameMs, Is.EqualTo(100.0));
            Assert.That(summary.AverageFps, Is.EqualTo(1000.0 / 55.0).Within(1e-10));
            Assert.That(summary.OnePercentLowFps, Is.EqualTo(10.0));
            Assert.That(summary.OverBudgetPercent, Is.EqualTo(50.0));
        });
    }

    [Test]
    public void OneFrameProducesDefinedPercentilesAndOnePercentLow()
    {
        var summary = BenchmarkFrameStatistics.Calculate([20.0], frameBudgetMs: 20.0);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Frames, Is.EqualTo(1));
            Assert.That(summary.MedianFrameMs, Is.EqualTo(20.0));
            Assert.That(summary.P95FrameMs, Is.EqualTo(20.0));
            Assert.That(summary.P99FrameMs, Is.EqualTo(20.0));
            Assert.That(summary.AverageFps, Is.EqualTo(50.0));
            Assert.That(summary.OnePercentLowFps, Is.EqualTo(50.0));
            Assert.That(summary.OverBudgetPercent, Is.Zero);
        });
    }

    [Test]
    public void UsesCeilingCountForSlowestOnePercent()
    {
        var summary = BenchmarkFrameStatistics.Calculate(
            [.. Enumerable.Repeat(10.0, 198), 20.0, 30.0, 40.0], frameBudgetMs: 30.0);

        Assert.That(summary.OnePercentLowFps, Is.EqualTo(1000.0 / 30.0).Within(1e-10));
        Assert.That(summary.OverBudgetPercent, Is.EqualTo(100.0 / 201.0).Within(1e-10));
    }

    [Test]
    public void EmptySequenceThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BenchmarkFrameStatistics.Calculate([]));
    }

    [TestCase(0.0)]
    [TestCase(-1.0)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void InvalidFrameTimesThrowArgumentException(double frameMs)
    {
        Assert.Throws<ArgumentException>(() => BenchmarkFrameStatistics.Calculate([16.0, frameMs]));
    }

    [Test]
    public void NullSequenceThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BenchmarkFrameStatistics.Calculate(null!));
    }

    [TestCase(0.0)]
    [TestCase(-1.0)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidFrameBudgetThrowsArgumentOutOfRangeException(double budgetMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkFrameStatistics.Calculate([16.0], budgetMs));
    }
}
