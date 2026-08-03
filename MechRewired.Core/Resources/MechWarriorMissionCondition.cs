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
/// Preserves one compact condition or action encoded in an MW2 mission-table record.
/// </summary>
/// <remarks>
/// The one-byte opcode and 24-bit argument are exposed without assigning unverified semantics.
/// </remarks>
public sealed record MechWarriorMissionCondition(char Opcode, int Argument);
