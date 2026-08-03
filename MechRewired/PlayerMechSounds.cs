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
using MechRewired.Resources;

namespace MechRewired;

/// <summary>
/// Loads the original MW2 player-mech effects and makes them playable by Godot.
/// </summary>
public sealed record PlayerMechSounds(
    AudioStreamWav TorsoMotor,
    AudioStreamWav Footfall,
    AudioStreamWav Startup,
    AudioStreamWav DeploymentReport)
{
    private const string TorsoMotorPath = "SNDS/TORSLOOP.SFL";
    private const string FootfallPath = "SNDS/NONFOOT.SFL";
    private const string StartupPath = "SNDS/NONPSTRT.SFL";
    private const string DeploymentReportPath = "SNDS/YELL00BS.SFL";

    public static PlayerMechSounds Load(MechWarriorProjectArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return new PlayerMechSounds(
            Load(archive, TorsoMotorPath, true, "torso motor"),
            Load(archive, FootfallPath, false, "footfall"),
            Load(archive, StartupPath, false, "mech startup"),
            Load(archive, DeploymentReportPath, false, "Pyre Light deployment report"));
    }

    private static AudioStreamWav Load(
        MechWarriorProjectArchive archive,
        string resourcePath,
        bool loop,
        string purpose)
    {
        var entry = archive.GetEntry(resourcePath);
        var sound = MechWarriorSoundFile.Load(archive.ReadEntry(entry));
        var stream = CreateStream(sound, loop);
        GD.Print(
            $"MechRewired: loaded {entry.Path} ({MechWarriorSoundFile.SampleRate:N0} Hz mono; " +
            $"{sound.Duration.TotalSeconds:F2} seconds; {purpose}{(loop ? ", looped" : string.Empty)}).");
        return stream;
    }

    private static AudioStreamWav CreateStream(MechWarriorSoundFile sound, bool loop)
    {
        var signedSamples = sound.Samples
            .Select(sample => unchecked((byte)(sample - 128)))
            .ToArray();
        return new AudioStreamWav
        {
            Data = signedSamples,
            Format = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = MechWarriorSoundFile.SampleRate,
            Stereo = false,
            LoopMode = loop
                ? AudioStreamWav.LoopModeEnum.Forward
                : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = signedSamples.Length
        };
    }
}
