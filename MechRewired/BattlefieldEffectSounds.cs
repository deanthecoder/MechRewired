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
/// Loads positional battlefield audio from the original game archive.
/// </summary>
public sealed record BattlefieldEffectSounds(
    IReadOnlyDictionary<string, AudioStreamWav> AmbientFire,
    IReadOnlyList<AudioStreamWav> Explosions)
{
    public static BattlefieldEffectSounds Load(MechWarriorProjectArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var ambientFire = new Dictionary<string, AudioStreamWav>(StringComparer.OrdinalIgnoreCase)
        {
            ["mecfire1"] = LoadWaveResource(archive, "SNDS/MECFIRE1.WAV", true, "ambient fire"),
            ["mecfire2"] = PlayerMechSounds.LoadResource(
                archive,
                "SNDS/MECFIRE2.SFL",
                true,
                "alternate ambient fire")
        };
        var explosions = new[]
        {
            PlayerMechSounds.LoadResource(archive, "SNDS/SMLEXPL1.SFL", false, "explosion variant 1"),
            PlayerMechSounds.LoadResource(archive, "SNDS/SMLEXPL2.SFL", false, "explosion variant 2")
        };
        return new BattlefieldEffectSounds(ambientFire, explosions);
    }

    private static AudioStreamWav LoadWaveResource(
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
}
