using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorDataFileTests
{
    [Test]
    public void Preferred3DfxFilesContainTheThreeRendererDataFiles()
    {
        Assert.That(MechWarriorDataFile.Preferred3DfxFiles, Is.EqualTo(new[]
        {
            "MW2.PRJ",
            "MW2.MIP",
            "SKYGND.PAR"
        }));
    }
}
