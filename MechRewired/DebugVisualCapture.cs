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
using System.Text.Json;

namespace MechRewired;

/// <summary>
/// Writes named, repeatable visual baselines on demand in debug builds.  The capture command
/// intentionally records the active camera rather than silently moving the player; its manifest
/// makes the mission, transform, render size and sky preset explicit so the same view can be
/// recreated while preserving a real gameplay session.
/// </summary>
public sealed partial class DebugVisualCapture : Node
{
    private readonly MissionSkyController m_sky;
    private readonly Camera3D m_camera;
    private readonly string m_missionId;
    private readonly Node m_console;

    public DebugVisualCapture(MissionSkyController sky, Camera3D camera, string missionId, Node console = null)
    {
        m_sky = sky;
        m_camera = camera;
        m_missionId = missionId;
        m_console = console;
    }

    /// <summary>
    /// Captures one of authored, day, dusk or night to user://visual-captures.
    /// </summary>
    public async void Capture(string preset)
    {
        var restoreConsoleAfterCapture = HideConsoleForCapture();
        if (!m_sky.TryApplyCapturePreset(preset))
        {
            GD.PushWarning(
                $"MechRewired: unknown visual preset '{preset}'. Use authored, day, dusk or night.");
            RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
            return;
        }

        // Let the console hide and the renderer observe the selected time before reading the viewport.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var outputDirectory = ProjectSettings.GlobalizePath("user://visual-captures");
        var makeDirectoryError = DirAccess.MakeDirRecursiveAbsolute(outputDirectory);
        if (makeDirectoryError != Error.Ok)
        {
            GD.PushError(
                $"MechRewired: cannot create visual-capture directory '{outputDirectory}' ({makeDirectoryError}).");
            RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
            return;
        }

        var fileStem = $"{ToFileSafeName(m_missionId)}-{ToFileSafeName(preset)}";
        var imagePath = Path.Combine(outputDirectory, $"{fileStem}.png");
        var image = GetViewport().GetTexture().GetImage();
        var saveError = image.SavePng(imagePath);
        if (saveError != Error.Ok)
        {
            GD.PushError($"MechRewired: could not save visual capture '{imagePath}' ({saveError}).");
            RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
            return;
        }

        var manifest = new
        {
            mission = m_missionId,
            preset = preset.Trim().ToLowerInvariant(),
            renderWidth = image.GetWidth(),
            renderHeight = image.GetHeight(),
            camera = new
            {
                position = new[] { m_camera.GlobalPosition.X, m_camera.GlobalPosition.Y, m_camera.GlobalPosition.Z },
                rotationDegrees = new[] { m_camera.GlobalRotationDegrees.X, m_camera.GlobalRotationDegrees.Y, m_camera.GlobalRotationDegrees.Z },
                fov = m_camera.Fov
            },
            sky = m_sky.Describe(),
            engine = Engine.GetVersionInfo()["string"].ToString(),
            generatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        var manifestPath = Path.Combine(outputDirectory, $"{fileStem}.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(
            $"MechRewired: wrote visual baseline '{preset}' to {imagePath} " +
            $"({image.GetWidth()}x{image.GetHeight()}; manifest {manifestPath}).");
        RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
    }

    public async void CaptureAll()
    {
        var restoreConsoleAfterCapture = HideConsoleForCapture();
        try
        {
            foreach (var preset in new[] { "authored", "day", "dusk", "night" })
            {
                if (!m_sky.TryApplyCapturePreset(preset))
                {
                    continue;
                }

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                WriteCapture(preset);
            }

            m_sky.TryApplyCapturePreset("authored");
        }
        finally
        {
            RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
        }
    }

    /// <summary>
    /// Captures the cockpit's lit and material-diagnostic views from the tester's current
    /// position.  A single command therefore produces comparable evidence for lighting,
    /// source textures and geometric normal direction without manual screenshot juggling.
    /// </summary>
    public async void CaptureCockpitDiagnostics(PlayerCockpit cockpit)
    {
        var restoreConsoleAfterCapture = HideConsoleForCapture();
        var originalMode = cockpit.FrameDiagnosticMode;
        try
        {
            foreach (var mode in new[]
                     {
                         CockpitFrameDiagnosticMode.Lit,
                         CockpitFrameDiagnosticMode.Albedo,
                         CockpitFrameDiagnosticMode.GeometricNormal,
                         CockpitFrameDiagnosticMode.NormalMap,
                         CockpitFrameDiagnosticMode.Roughness,
                         CockpitFrameDiagnosticMode.Metallic,
                         CockpitFrameDiagnosticMode.DirectSun
                     })
            {
                cockpit.FrameDiagnosticMode = mode;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                WriteCapture(
                    $"cockpit-{cockpit.FrameDiagnosticModeName.Replace(' ', '-')}",
                    cockpit.FrameDiagnosticModeName);
            }
        }
        finally
        {
            cockpit.FrameDiagnosticMode = originalMode;
            RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
        }
    }

    /// <summary>
    /// Captures a compact metallic/roughness matrix from the current cockpit view. The values
    /// deliberately span painted, weathered and exposed-metal responses while keeping the
    /// texture, camera, sky and geometry identical between images.
    /// </summary>
    public async void CaptureCockpitMaterialSweep(PlayerCockpit cockpit)
    {
        var restoreConsoleAfterCapture = HideConsoleForCapture();
        var originalMode = cockpit.FrameDiagnosticMode;
        var originalMetallic = cockpit.FrameMetallic;
        var originalRoughness = cockpit.FrameRoughness;
        var metallicValues = new[] { 0.0f, 0.25f, 0.50f, 0.75f };
        var roughnessValues = new[] { 0.35f, 0.60f, 0.85f };

        try
        {
            cockpit.FrameDiagnosticMode = CockpitFrameDiagnosticMode.Lit;
            foreach (var metallic in metallicValues)
            {
                foreach (var roughness in roughnessValues)
                {
                    cockpit.FrameMetallic = metallic;
                    cockpit.FrameRoughness = roughness;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    WriteCapture(
                        $"cockpit-material-m{metallic * 100:000}-r{roughness * 100:000}",
                        "material sweep",
                        metallic,
                        roughness,
                        cockpit.FrameTextureScale);
                }
            }

            GD.Print(
                $"MechRewired: wrote {metallicValues.Length * roughnessValues.Length} cockpit " +
                "material comparisons to user://visual-captures.");
        }
        finally
        {
            cockpit.FrameMetallic = originalMetallic;
            cockpit.FrameRoughness = originalRoughness;
            cockpit.FrameDiagnosticMode = originalMode;
            RestoreConsoleAfterCapture(restoreConsoleAfterCapture);
        }
    }

    private void WriteCapture(
        string preset,
        string cockpitDiagnostic = null,
        float? cockpitMetallic = null,
        float? cockpitRoughness = null,
        float? cockpitTextureScale = null)
    {
        var outputDirectory = ProjectSettings.GlobalizePath("user://visual-captures");
        if (DirAccess.MakeDirRecursiveAbsolute(outputDirectory) != Error.Ok)
        {
            return;
        }

        var fileStem = $"{ToFileSafeName(m_missionId)}-{ToFileSafeName(preset)}";
        var imagePath = Path.Combine(outputDirectory, $"{fileStem}.png");
        var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(imagePath) != Error.Ok)
        {
            return;
        }

        var manifestPath = Path.Combine(outputDirectory, $"{fileStem}.json");
        var manifest = new
        {
            mission = m_missionId,
            preset,
            renderWidth = image.GetWidth(),
            renderHeight = image.GetHeight(),
            camera = new
            {
                position = new[] { m_camera.GlobalPosition.X, m_camera.GlobalPosition.Y, m_camera.GlobalPosition.Z },
                rotationDegrees = new[] { m_camera.GlobalRotationDegrees.X, m_camera.GlobalRotationDegrees.Y, m_camera.GlobalRotationDegrees.Z },
                fov = m_camera.Fov
            },
            sky = m_sky.Describe(),
            cockpitDiagnostic,
            cockpitMaterial = cockpitMetallic.HasValue
                ? new
                {
                    metallic = cockpitMetallic.Value,
                    roughness = cockpitRoughness,
                    textureScale = cockpitTextureScale
                }
                : null,
            engine = Engine.GetVersionInfo()["string"].ToString(),
            generatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(
            $"MechRewired: wrote visual baseline '{preset}' to {imagePath} " +
            $"({image.GetWidth()}x{image.GetHeight()}; manifest {manifestPath}).");
    }

    private static string ToFileSafeName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => invalidCharacters.Contains(character) || character == '/' || character == '\\'
                ? '-'
                : character));
    }

    private bool HideConsoleForCapture()
    {
        if (m_console == null || !m_console.Call("is_visible").AsBool())
        {
            return false;
        }

        m_console.Call("toggle_console");
        return true;
    }

    private void RestoreConsoleAfterCapture(bool restore)
    {
        if (restore && m_console != null)
        {
            m_console.Call("toggle_console");
        }
    }
}
