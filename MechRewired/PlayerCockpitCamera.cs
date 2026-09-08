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

/// <summary>
/// Provides constrained pilot head-look from inside the player cockpit.
/// </summary>
public partial class PlayerCockpitCamera : Camera3D
{
    private const float HeadLookSpeed = Mathf.Tau / 3.0f;
    private const float MouseSensitivity = 0.002f;
    private const float HeadLookResponse = 6.0f;
    private const float CenterPitch = 0.23f * Mathf.Pi / 180.0f;
    private const float MaximumYaw = Mathf.Pi / 2.0f;
    private const float MinimumPitch = -Mathf.Pi / 4.0f;
    private const float MaximumPitch = Mathf.Pi / 3.0f;

    private float m_targetYaw;
    private float m_targetPitch = CenterPitch;
    private bool m_wasPivoting;

    public override void _Ready()
    {
        Rotation = new Vector3(CenterPitch, 0.0f, 0.0f);
    }

    public override void _Process(double delta)
    {
        if (!Current)
        {
            return;
        }

        var shiftHeld = Input.IsPhysicalKeyPressed(Key.Shift);
        var yawInput = 0.0f;
        var pitchInput = 0.0f;
        if (shiftHeld)
        {
            if (Input.IsPhysicalKeyPressed(Key.Left))
            {
                yawInput += 1.0f;
            }

            if (Input.IsPhysicalKeyPressed(Key.Right))
            {
                yawInput -= 1.0f;
            }

            if (Input.IsPhysicalKeyPressed(Key.Up))
            {
                pitchInput += 1.0f;
            }

            if (Input.IsPhysicalKeyPressed(Key.Down))
            {
                pitchInput -= 1.0f;
            }
        }

        if (yawInput != 0.0f || pitchInput != 0.0f)
        {
            ApplyLookDelta(
                yawInput * HeadLookSpeed * (float)delta,
                pitchInput * HeadLookSpeed * (float)delta);
            m_wasPivoting = true;
        }
        else if (!shiftHeld && m_wasPivoting)
        {
            CenterView();
        }

        var blend = 1.0f - Mathf.Exp(-HeadLookResponse * (float)delta);
        Rotation = new Vector3(
            Mathf.LerpAngle(Rotation.X, m_targetPitch, blend),
            Mathf.LerpAngle(Rotation.Y, m_targetYaw, blend),
            0.0f);
    }

    /// <summary>
    /// Pivots the pilot's view using captured mouse movement while head-look is held.
    /// </summary>
    public void ApplyMouseLook(Vector2 relativeMotion)
    {
        ApplyLookDelta(
            -relativeMotion.X * MouseSensitivity,
            -relativeMotion.Y * MouseSensitivity);
        m_wasPivoting = true;
    }

    public void CenterView()
    {
        m_targetPitch = CenterPitch;
        m_targetYaw = 0.0f;
        m_wasPivoting = false;
    }

    private void ApplyLookDelta(float yaw, float pitch)
    {
        m_targetYaw = Mathf.Clamp(m_targetYaw + yaw, -MaximumYaw, MaximumYaw);
        m_targetPitch = Mathf.Clamp(m_targetPitch + pitch, MinimumPitch, MaximumPitch);
    }
}
