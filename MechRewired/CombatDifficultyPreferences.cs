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
using MechRewired.Simulation;

namespace MechRewired;

internal static class CombatDifficultyPreferences
{
    private const string SettingsPath = "user://settings.cfg";
    private const string Section = "combat";
    private const string Key = "difficulty";

    public static CombatDifficulty Load()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok)
        {
            return CombatDifficulty.Medium;
        }

        var value = config.GetValue(Section, Key, CombatDifficulty.Medium.ToString()).AsString();
        return Enum.TryParse<CombatDifficulty>(value, true, out var difficulty) &&
               Enum.IsDefined(difficulty)
            ? difficulty
            : CombatDifficulty.Medium;
    }

    public static void Save(CombatDifficulty difficulty)
    {
        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue(Section, Key, difficulty.ToString());
        var result = config.Save(SettingsPath);
        if (result != Error.Ok)
        {
            GD.PushWarning($"MechRewired: could not save combat difficulty ({result}).");
        }
    }
}
