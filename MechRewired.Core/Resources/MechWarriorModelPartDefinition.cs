// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;

namespace MechRewired.Resources;

/// <summary>
/// Identifies one WTB component and its rest translation within an assembled mech.
/// </summary>
/// <remarks>
/// Translations use MechRewired world units after the standard WTB source scale has been applied.
/// </remarks>
public sealed record MechWarriorModelPartDefinition(string Name, string ResourcePath, Vector3 Translation);
