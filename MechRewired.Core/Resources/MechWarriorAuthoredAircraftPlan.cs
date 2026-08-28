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

/// <summary>Resolved original-data inputs for one scripted aircraft actor.</summary>
public sealed record MechWarriorAuthoredAircraftPlan(
    MechWarriorLevelSource Source,
    MechWarriorLevelActor Actor,
    MechWarriorWorldObject MotionObject,
    MechWarriorWorldPathTable Path,
    MechWarriorWorldTask PathTask,
    bool RotateWithPath,
    int SoundObjectId,
    float MaximumSoundDistance,
    string SoundResourceName,
    bool LoopSound,
    MechWarriorLevelObject RotorComponent);
