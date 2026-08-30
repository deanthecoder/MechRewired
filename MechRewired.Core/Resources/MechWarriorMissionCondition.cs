// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Resources;

/// <summary>
/// Identifies the state of another mission-table record used to activate an objective.
/// </summary>
public enum MechWarriorMissionConditionResult : byte
{
    Completed = (byte)'C',
    Failed = (byte)'F',
    Initial = (byte)'I',
    Succeeded = (byte)'S'
}

/// <summary>References one objective record in one MTBL/star table.</summary>
public sealed record MechWarriorMissionCondition(
    MechWarriorMissionConditionResult Result,
    byte ObjectiveIndex,
    byte TableIndex);
