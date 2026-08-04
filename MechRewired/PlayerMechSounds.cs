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
    AudioStreamWav ReactorHum,
    AudioStreamWav DeploymentReport,
    AudioStreamWav StartWalking,
    AudioStreamWav StopWalking,
    AudioStreamWav StartRunning,
    AudioStreamWav StopRunning,
    AudioStreamWav NavigationPointTone,
    IReadOnlyList<AudioStreamWav> NavigationPointReports,
    AudioStreamWav DisplayZoom,
    AudioStreamWav MediumLaser)
{
    private const string TorsoMotorPath = "SNDS/TORSLOOP.SFL";
    private const string FootfallPath = "SNDS/NONFOOT.SFL";
    private const string StartupPath = "SNDS/NONPSTRT.SFL";
    private const string ReactorHumPath = "SNDS/MECHUMXX.WAV";
    private const string DeploymentReportPath = "SNDS/YELL00BS.SFL";
    private const string StartWalkingPath = "SNDS/STOP2WLK.SFL";
    private const string StopWalkingPath = "SNDS/WLK2STOP.SFL";
    private const string StartRunningPath = "SNDS/WALK2RUN.SFL";
    private const string StopRunningPath = "SNDS/RUN2WLK.SFL";
    private const string NavigationPointTonePath = "SNDS/MECNAVPT.SFL";
    private const string DisplayZoomPath = "SNDS/VIEWZOOM.SFL";
    private const string MediumLaserPath = "SNDS/MECMLASR.SFL";
    private static readonly string[] NavigationPointReportPaths =
    [
        "SNDS/GENEGOES.SFL",
        "SNDS/GENEGOFS.SFL",
        "SNDS/GENEGOGS.SFL"
    ];

    public static PlayerMechSounds Load(MechWarriorProjectArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return new PlayerMechSounds(
            LoadResource(archive, TorsoMotorPath, true, "torso motor"),
            LoadResource(archive, FootfallPath, false, "footfall"),
            LoadResource(archive, StartupPath, false, "mech startup"),
            LoadWaveResource(archive, ReactorHumPath, true, "mech reactor hum"),
            LoadResource(archive, DeploymentReportPath, false, "Pyre Light deployment report"),
            LoadResource(archive, StartWalkingPath, false, "stop-to-walk transition"),
            LoadResource(archive, StopWalkingPath, false, "walk-to-stop transition"),
            LoadResource(archive, StartRunningPath, false, "walk-to-run transition"),
            LoadResource(archive, StopRunningPath, false, "run-to-walk transition"),
            LoadResource(archive, NavigationPointTonePath, false, "navigation point arrival tone"),
            NavigationPointReportPaths
                .Select((path, index) => LoadResource(
                    archive,
                    path,
                    false,
                    $"Pyre Light NAV {index + 1} arrival report"))
                .ToArray(),
            LoadResource(archive, DisplayZoomPath, true, "cockpit display zoom motor"),
            LoadResource(archive, MediumLaserPath, false, "medium laser fire"));
    }

    internal static AudioStreamWav LoadResource(
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

    internal static AudioStreamWav LoadWaveResource(
        MechWarriorProjectArchive archive,
        string resourcePath,
        bool loop,
        string purpose)
    {
        var entry = archive.GetEntry(resourcePath);
        var sound = MechWarriorWaveFile.Load(archive.ReadEntry(entry));
        var samples = sound.BitsPerSample == 8
            ? sound.Samples.Select(sample => unchecked((byte)(sample - 128))).ToArray()
            : sound.Samples;
        var stream = new AudioStreamWav
        {
            Data = samples,
            Format = sound.BitsPerSample == 8
                ? AudioStreamWav.FormatEnum.Format8Bits
                : AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = sound.SampleRate,
            Stereo = false,
            LoopMode = loop
                ? AudioStreamWav.LoopModeEnum.Forward
                : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = samples.Length / (sound.BitsPerSample / 8)
        };
        GD.Print(
            $"MechRewired: loaded {entry.Path} ({sound.SampleRate:N0} Hz mono; " +
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
