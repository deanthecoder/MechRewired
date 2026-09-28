// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>Stores Quest rendering choices alongside the existing game preferences.</summary>
internal static class QuestGraphicsPreferences
{
    private const string SettingsPath = "user://settings.cfg";
    private const string Section = "quest_graphics";
    private const string TriplanarKey = "terrain_triplanar";

    public static bool LoadTerrainTriplanar()
    {
        using var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok)
        {
            return false;
        }

        var value = config.GetValue(Section, TriplanarKey, false);
        return value.VariantType == Variant.Type.Bool && value.AsBool();
    }

    public static void SaveTerrainTriplanar(bool enabled)
    {
        using var config = new ConfigFile();
        var loadResult = config.Load(SettingsPath);
        if (loadResult != Error.Ok && loadResult != Error.FileNotFound)
        {
            GD.PushWarning($"MechRewired: could not read graphics preferences ({loadResult}); settings were not overwritten.");
            return;
        }

        config.SetValue(Section, TriplanarKey, enabled);
        var result = config.Save(SettingsPath);
        if (result != Error.Ok)
        {
            GD.PushWarning($"MechRewired: could not save terrain triplanar preference ({result}).");
        }
    }
}
