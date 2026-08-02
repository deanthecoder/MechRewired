// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies the original game-data requirements exposed by the engine.
/// </summary>
/// <remarks>
/// The required list remains deliberately small while the DOS vertical slice is developed.
/// </remarks>
[TestFixture]
public sealed class MechWarriorDataFileTests
{
    [Test]
    public void RequiredDosFilesContainTheProjectArchive()
    {
        Assert.That(MechWarriorDataFile.RequiredDosFiles, Is.EqualTo(new[]
        {
            "MW2.PRJ"
        }));
    }
}
