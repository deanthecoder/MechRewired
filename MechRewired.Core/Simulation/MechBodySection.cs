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

/// <summary>
/// Identifies the logical assembly to which an original MW2 mech mesh belongs.
/// </summary>
public enum MechBodySection
{
    Hips,
    Torso,
    LeftArm,
    RightArm,
    LeftUpperLeg,
    LeftLowerLeg,
    LeftFoot,
    RightUpperLeg,
    RightLowerLeg,
    RightFoot
}

/// <summary>
/// Classifies both semantic player-part names and original MW2 POLY filenames.
/// </summary>
public static class MechBodySectionClassifier
{
    public static MechBodySection Classify(string partName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        var name = Path.GetFileNameWithoutExtension(partName).ToUpperInvariant();

        if (ContainsAny(name, "LFOOT", "LFTOE", "LLTOE", "LRTOE", "LEFTFRONTTOE", "LEFTREARTOE"))
        {
            return MechBodySection.LeftFoot;
        }

        if (ContainsAny(name, "RFOOT", "RFTOE", "RLTOE", "RRTOE", "RIGHTFRONTTOE", "RIGHTREARTOE"))
        {
            return MechBodySection.RightFoot;
        }

        if (ContainsAny(name, "LLLEG", "LKNEE", "LEFTLOWERLEG"))
        {
            return MechBodySection.LeftLowerLeg;
        }

        if (ContainsAny(name, "RLLEG", "RKNEE", "RIGHTLOWERLEG"))
        {
            return MechBodySection.RightLowerLeg;
        }

        if (ContainsAny(name, "LULEG", "LEFTUPPERLEG"))
        {
            return MechBodySection.LeftUpperLeg;
        }

        if (ContainsAny(name, "RULEG", "RIGHTUPPERLEG"))
        {
            return MechBodySection.RightUpperLeg;
        }

        if (ContainsAny(name, "LARM", "LGUN", "DECLL", "LEFTARM", "LEFTDECAL"))
        {
            return MechBodySection.LeftArm;
        }

        if (ContainsAny(name, "RARM", "RGUN", "DECLR", "RIGHTARM", "RIGHTDECAL"))
        {
            return MechBodySection.RightArm;
        }

        if (ContainsAny(name, "HIPS", "HIP"))
        {
            return MechBodySection.Hips;
        }

        return MechBodySection.Torso;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);
}
