// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>Samples one target and estimates its velocity once per salvo per process frame.</summary>
/// <remarks>Missiles retain their own steering, range, arming distance, and swept collisions.</remarks>
public sealed class MissileSalvoGuidance(Func<Vector3?> targetPosition, Func<bool> targetAlive = null)
{
    private bool m_sampled;
    private ulong m_frame;
    private Vector3? m_target;
    private Vector3? m_previousTarget;

    public Vector3 TargetVelocity { get; private set; }

    public Vector3? Sample(ulong frame, float elapsed)
    {
        // Death can occur between two missiles in the same frame. Check the cheap state
        // flag each time without repeating the target's world-position calculation.
        if (targetAlive != null && !targetAlive())
        {
            m_target = null;
            m_previousTarget = null;
            TargetVelocity = Vector3.Zero;
            return null;
        }
        if (m_sampled && m_frame == frame) return m_target;
        if (m_sampled && frame != m_frame + 1)
        {
            // No follower sampled intervening frames; their elapsed time is unknown.
            m_previousTarget = null;
            TargetVelocity = Vector3.Zero;
        }
        m_sampled = true;
        m_frame = frame;
        m_target = targetPosition();
        if (m_target.HasValue)
        {
            if (m_previousTarget.HasValue && elapsed > 0.0001f)
            {
                var measuredVelocity = (m_target.Value - m_previousTarget.Value) / elapsed;
                TargetVelocity = TargetVelocity.Lerp(measuredVelocity, Math.Min(elapsed * 8.0f, 1.0f));
            }
            m_previousTarget = m_target;
        }
        return m_target;
    }
}
