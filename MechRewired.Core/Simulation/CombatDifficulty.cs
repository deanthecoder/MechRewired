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

public enum CombatDifficulty
{
    Easy,
    Medium,
    Hard
}

/// <summary>
/// Applies the original game's global combat difficulty on top of each hostile pilot's authored GPS skill.
/// </summary>
public sealed record CombatDifficultyProfile(
    double FireDecisionIntervalSeconds,
    double BaseAimErrorDegrees,
    double AimErrorPerGunnerySkillDegrees,
    double MaximumRangeAimErrorDegrees)
{
    public static CombatDifficultyProfile For(CombatDifficulty difficulty) => difficulty switch
    {
        // Easy and Medium deliberately provide a broad targeting-computer advantage. MechVM's
        // reconstruction uses two degrees of aim error per authored gunnery point; Medium retains
        // that useful reference while Hard preserves MechRewired's previous combat tuning.
        CombatDifficulty.Easy => new(1.25, 2.0, 2.5, 1.5),
        CombatDifficulty.Medium => new(1.0, 0.0, 2.0, 0.0),
        CombatDifficulty.Hard => new(0.60, 0.35, 0.30, 0.75),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null)
    };

    public double GetMaximumAimErrorDegrees(int gunnerySkill, double rangeFraction) =>
        BaseAimErrorDegrees +
        Math.Max(gunnerySkill, 0) * AimErrorPerGunnerySkillDegrees +
        Math.Clamp(rangeFraction, 0.0, 1.0) * MaximumRangeAimErrorDegrees;
}
