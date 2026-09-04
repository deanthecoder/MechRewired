// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Simulation;

/// <summary>Blends a mech's toes into a relaxed airborne pose and back into its walking pose.</summary>
/// <remarks>
/// Each toe's direction is derived from its geometry and original joint transform.
/// The transition is independent of the host renderer and never changes the authored rest angles.
/// </remarks>
public sealed class MechAirbornePose
{
    public const float SagRadians = 36.0f * MathF.PI / 180.0f;
    private const float TakeoffBlendSeconds = 1.2f;
    private const float LandingBlendSeconds = 0.18f;

    public float Weight { get; private set; }

    /// <summary>Advances the takeoff or landing blend using elapsed simulation time.</summary>
    public void Advance(float deltaSeconds, bool airborne)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        if (airborne)
        {
            // Start with zero angular speed, allowing the chassis to lift before the toes droop.
            var progress = Math.Min(1.0f, MathF.Sqrt(Weight) + deltaSeconds / TakeoffBlendSeconds);
            Weight = progress * progress;
        }
        else
        {
            Weight = Math.Max(0.0f, Weight - deltaSeconds / LandingBlendSeconds);
        }
    }

    /// <summary>Blends the normal gait pitch into downward toe sag, relative to its rest angle.</summary>
    public float GetToePitch(float gaitPitchRadians, float sagDirection) =>
        gaitPitchRadians * (1.0f - Weight) + SagRadians * sagDirection * Weight;

    /// <summary>Chooses the pitch sign that lowers the toe's transformed center of geometry.</summary>
    public static float ChooseSagDirection(float positivePitchHeight, float negativePitchHeight) =>
        MathF.Abs(positivePitchHeight - negativePitchHeight) < 0.0001f
            ? 0.0f
            : positivePitchHeight < negativePitchHeight ? 1.0f : -1.0f;
}
