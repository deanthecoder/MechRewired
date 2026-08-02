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
public sealed class MechWarriorMechFileTests
{
    [Test]
    public void LoadDecodesTimberWolfMovementSpeeds()
    {
        var data = new byte[24];
        BitConverter.GetBytes(75).CopyTo(data, 0);
        BitConverter.GetBytes(5).CopyTo(data, 4);

        var mech = MechWarriorMechFile.Load(data);

        Assert.That(mech.Tonnage, Is.EqualTo(75));
        Assert.That(mech.WalkingMovementPoints, Is.EqualTo(5));
        Assert.That(mech.CruisingSpeedKph, Is.EqualTo(54.0));
        Assert.That(mech.MaximumSpeedKph, Is.EqualTo(86.4));
    }

    [Test]
    public void LoadRejectsATruncatedGeneralHeader()
    {
        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorMechFile.Load(new byte[23]));

        Assert.That(exception.Message, Does.Contain("23 bytes"));
        Assert.That(exception.Message, Does.Contain("24 bytes"));
    }
}
