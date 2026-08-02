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
    private const float MouseSensitivity = 0.002f;
    private const float MaximumYaw = Mathf.Pi / 3.0f;
    private const float MinimumPitch = 0.23f * Mathf.Pi / 180.0f;
    private const float MaximumPitch = Mathf.Pi / 4.0f;

    public override void _Ready()
    {
        Rotation = new Vector3(MinimumPitch, 0.0f, 0.0f);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!Current)
        {
            return;
        }

        switch (inputEvent)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion mouseMotion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Rotation = new Vector3(
                    Mathf.Clamp(Rotation.X - mouseMotion.Relative.Y * MouseSensitivity, MinimumPitch, MaximumPitch),
                    Mathf.Clamp(Rotation.Y - mouseMotion.Relative.X * MouseSensitivity, -MaximumYaw, MaximumYaw),
                    0.0f);
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}
