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

[TestFixture]
public sealed class MechWarriorMissionResourcesTests
{
    [TestCase("YELLSCN1.BWD", "YELL")]
    [TestCase("FIREscn12.bwd", "FIRE")]
    public void GetMissionPrefixReadsThePrefixBeforeScenarioNumber(string scenarioName, string expectedPrefix)
    {
        Assert.That(MechWarriorMissionResources.GetMissionPrefix(scenarioName), Is.EqualTo(expectedPrefix));
    }

    [TestCase("YELLSCN.BWD")]
    [TestCase("YELLWLD1.BWD")]
    [TestCase("YELLSCNX.BWD")]
    public void GetMissionPrefixRejectsNamesThatAreNotScenarios(string scenarioName)
    {
        Assert.That(
            () => MechWarriorMissionResources.GetMissionPrefix(scenarioName),
            Throws.TypeOf<InvalidDataException>());
    }
}
