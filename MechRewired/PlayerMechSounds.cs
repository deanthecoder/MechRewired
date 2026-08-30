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
    AudioStreamWav EnemyPowerUpDetected,
    AudioStreamWav EnemyMechDestroyed,
    AudioStreamWav NavigationPointTone,
    AudioStreamWav DisplayZoom,
    AudioStreamWav ExternalCameraEngaged,
    AudioStreamWav Autopilot,
    AudioStreamWav AutopilotEnabled,
    AudioStreamWav AutopilotDisabled,
    IReadOnlyDictionary<string, AudioStreamWav> WeaponFireSounds,
    AudioStreamWav MissileLock,
    AudioStreamWav WeaponUnavailable,
    AudioStreamWav ChainFire,
    AudioStreamWav GroupFire,
    IReadOnlyList<AudioStreamWav> WeaponImpacts,
    AudioStreamWav CriticalHit,
    AudioStreamWav HeatCritical,
    AudioStreamWav ThermalShutdown,
    AudioStreamWav ShutdownOverride,
    AudioStreamWav ShuttingDown,
    AudioStreamWav ShutdownEffect,
    AudioStreamWav DeathExplosion,
    AudioStreamWav MissionFailed)
{
    private const string TorsoMotorPath = "SNDS/TORSLOOP.SFL";
    private const string FootfallPath = "SNDS/NONFOOT.SFL";
    private const string StartupPath = "SNDS/NONPSTRT.SFL";
    private const string ReactorHumPath = "SNDS/MECHUMXX.WAV";
    private const string StartWalkingPath = "SNDS/STOP2WLK.SFL";
    private const string StopWalkingPath = "SNDS/WLK2STOP.SFL";
    private const string StartRunningPath = "SNDS/WALK2RUN.SFL";
    private const string StopRunningPath = "SNDS/RUN2WLK.SFL";
    private const string EnemyPowerUpDetectedPath = "SNDS/BET79.SFL";
    private const string EnemyMechDestroyedPath = "SNDS/BET75.SFL";
    private const string NavigationPointTonePath = "SNDS/MECNAVPT.SFL";
    private const string DisplayZoomPath = "SNDS/VIEWZOOM.SFL";
    private const string ExternalCameraEngagedPath = "SNDS/BET71.SFL";
    private const string AutopilotPath = "SNDS/BET70.SFL";
    private const string AutopilotEnabledPath = "SNDS/BETENGAG.SFL";
    private const string AutopilotDisabledPath = "SNDS/BETOFF.SFL";
    private const string MissileLockPath = "SNDS/BET73.SFL";
    private const string WeaponUnavailablePath = "SNDS/MECWPTG1.SFL";
    private const string ChainFirePath = "SNDS/BET14_1.SFL";
    private const string GroupFirePath = "SNDS/BET14_2.SFL";
    private const string CriticalHitPath = "SNDS/BET6.SFL";
    private const string HeatCriticalPath = "SNDS/BET7.SFL";
    private const string ThermalShutdownPath = "SNDS/BET8.SFL";
    private const string ShutdownOverridePath = "SNDS/BET9.SFL";
    private const string ShuttingDownPath = "SNDS/BET11_1.SFL";
    private const string ShutdownEffectPath = "SNDS/MECSHTD1.SFL";
    private const string DeathExplosionPath = "SNDS/MECEXPBG.SFL";
    private const string MissionFailedPath = "SNDS/GENE001F.SFL";
    private static readonly string[] WeaponImpactPaths =
    [
        "SNDS/MECWIMP1.SFL",
        "SNDS/MECWIMP2.SFL",
        "SNDS/MECWIMP3.SFL",
        "SNDS/MECWIMP4.SFL",
        "SNDS/MECWIMP5.SFL"
    ];
    private static readonly string[] WeaponFireResourceNames =
    [
        "MECSLASR.SFL",
        "MECMLASR.SFL",
        "MECBLASR.SFL",
        "MECPLASR.SFL",
        "MECMGUN1.SFL",
        "MECBBAAT.SFL",
        "MECMISNR.SFL"
    ];

    public AudioStreamWav MediumLaser => WeaponFireSounds["MECMLASR.SFL"];

    public static PlayerMechSounds Load(MechWarriorProjectArchive archive, string missionPrefix)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionPrefix);
        return new PlayerMechSounds(
            LoadResource(archive, TorsoMotorPath, true, "torso motor"),
            LoadResource(archive, FootfallPath, false, "footfall"),
            LoadResource(archive, StartupPath, false, "mech startup"),
            LoadWaveResource(archive, ReactorHumPath, true, "mech reactor hum"),
            LoadResource(
                archive,
                $"SNDS/{missionPrefix}00BS.SFL",
                false,
                $"{missionPrefix} mission deployment report"),
            LoadResource(archive, StartWalkingPath, false, "stop-to-walk transition"),
            LoadResource(archive, StopWalkingPath, false, "walk-to-stop transition"),
            LoadResource(archive, StartRunningPath, false, "walk-to-run transition"),
            LoadResource(archive, StopRunningPath, false, "run-to-walk transition"),
            LoadResource(archive, EnemyPowerUpDetectedPath, false, "enemy power-up detected report"),
            LoadResource(archive, EnemyMechDestroyedPath, false, "enemy mech destroyed report"),
            LoadResource(archive, NavigationPointTonePath, false, "navigation point arrival tone"),
            LoadResource(archive, DisplayZoomPath, true, "cockpit display zoom motor"),
            LoadResource(archive, ExternalCameraEngagedPath, false, "external-camera engaged report"),
            LoadResource(archive, AutopilotPath, false, "autopilot report"),
            LoadResource(archive, AutopilotEnabledPath, false, "autopilot engaged report"),
            LoadResource(archive, AutopilotDisabledPath, false, "autopilot disabled report"),
            WeaponFireResourceNames.ToDictionary(
                resourceName => resourceName,
                resourceName => LoadResource(
                    archive,
                    $"SNDS/{resourceName}",
                    false,
                    $"{Path.GetFileNameWithoutExtension(resourceName)} weapon fire"),
                StringComparer.OrdinalIgnoreCase),
            LoadResource(archive, MissileLockPath, false, "missile-lock report"),
            LoadResource(archive, WeaponUnavailablePath, false, "unavailable weapon clunk"),
            LoadResource(archive, ChainFirePath, false, "chain-fire report"),
            LoadResource(archive, GroupFirePath, false, "group-fire report"),
            WeaponImpactPaths
                .Select((path, index) => LoadResource(
                    archive,
                    path,
                    false,
                    $"weapon impact {index + 1}"))
                .ToArray(),
            LoadResource(archive, CriticalHitPath, false, "critical-hit report"),
            LoadResource(archive, HeatCriticalPath, false, "heat-critical report"),
            LoadResource(archive, ThermalShutdownPath, false, "thermal shutdown report"),
            LoadResource(archive, ShutdownOverridePath, false, "shutdown override report"),
            LoadResource(archive, ShuttingDownPath, false, "manual shutdown report"),
            LoadResource(archive, ShutdownEffectPath, false, "mech shutdown effect"),
            LoadResource(archive, DeathExplosionPath, false, "player mech destruction explosion"),
            LoadResource(archive, MissionFailedPath, false, "mission failed report"));
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
