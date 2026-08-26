// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;
using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Simulation;

[TestFixture]
public sealed class CubicBezierCurveTests
{
    [Test]
    public void EvaluationIncludesBothEndpointsAndSmoothlyInterpolatesBetweenThem()
    {
        var curve = new CubicBezierCurve(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f),
            new Vector2(10.0f, 10.0f),
            new Vector2(20.0f, 10.0f));

        Assert.Multiple(() =>
        {
            Assert.That(curve.Evaluate(0.0f), Is.EqualTo(new Vector2(0.0f, 0.0f)));
            Assert.That(curve.Evaluate(1.0f), Is.EqualTo(new Vector2(20.0f, 10.0f)));
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(new Vector2(10.0f, 5.0f)));
        });
    }
}
