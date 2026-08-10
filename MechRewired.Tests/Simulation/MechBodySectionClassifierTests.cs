// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Simulation;

[TestFixture]
public sealed class MechBodySectionClassifierTests
{
    [TestCase("LeftArm", MechBodySection.LeftArm)]
    [TestCase("RightRearToe", MechBodySection.RightFoot)]
    [TestCase("POLY/TW1DECLL.WTB", MechBodySection.LeftArm)]
    [TestCase("FRM00LULEG.WTB", MechBodySection.LeftUpperLeg)]
    [TestCase("FRM00RKNEE.WTB", MechBodySection.RightLowerLeg)]
    [TestCase("NOVA_RRTOE.WTB", MechBodySection.RightFoot)]
    [TestCase("JEN00_HIPS.WTB", MechBodySection.Hips)]
    [TestCase("TW1WINSH.WTB", MechBodySection.Torso)]
    public void ClassifiesOriginalAndSemanticPartNames(string name, MechBodySection expected)
    {
        Assert.That(MechBodySectionClassifier.Classify(name), Is.EqualTo(expected));
    }
}
