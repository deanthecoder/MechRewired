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

namespace MechRewired.Simulation;

/// <summary>A two-dimensional cubic Bézier curve used for smooth planned vehicle courses.</summary>
public readonly record struct CubicBezierCurve(
    Vector2 Start,
    Vector2 StartControl,
    Vector2 EndControl,
    Vector2 End)
{
    /// <summary>Returns the point at a normalized distance along the curve parameter.</summary>
    public Vector2 Evaluate(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        var inverseT = 1.0f - t;
        return inverseT * inverseT * inverseT * Start +
               3.0f * inverseT * inverseT * t * StartControl +
               3.0f * inverseT * t * t * EndControl +
               t * t * t * End;
    }
}
