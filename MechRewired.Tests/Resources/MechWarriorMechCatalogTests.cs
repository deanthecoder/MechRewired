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
public sealed class MechWarriorMechCatalogTests
{
    [Test]
    public void LoadResolvesAConfigurationThroughItsChassisAbbreviation()
    {
        var data = CreateCatalogData(("mdg", "maddog", "Mad Dog", 60));

        var catalog = MechWarriorMechCatalog.Load(data);
        var chassis = catalog.ResolveConfiguration("MDG00STD.MEK");

        Assert.That(chassis.ResourceName, Is.EqualTo("maddog"));
        Assert.That(chassis.DisplayName, Is.EqualTo("Mad Dog"));
        Assert.That(chassis.Tonnage, Is.EqualTo(60));
    }

    [Test]
    public void LoadRejectsARecordWithNoDisplayName()
    {
        var data = CreateCatalogData(("mdg", "maddog", string.Empty, 60));

        Assert.That(() => MechWarriorMechCatalog.Load(data), Throws.TypeOf<InvalidDataException>());
    }

    private static byte[] CreateCatalogData(params (string Abbreviation, string Resource, string Name, int Tonnage)[] entries)
    {
        var data = new byte[4 + entries.Length * 45];
        BitConverter.GetBytes(entries.Length).CopyTo(data, 0);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 4 + index * 45;
            WriteAscii(data, offset + 3, 4, entries[index].Abbreviation);
            WriteAscii(data, offset + 7, 9, entries[index].Resource);
            WriteAscii(data, offset + 16, 25, entries[index].Name);
            BitConverter.GetBytes(entries[index].Tonnage).CopyTo(data, offset + 41);
        }

        return data;
    }

    private static void WriteAscii(byte[] data, int offset, int length, string value)
    {
        var encoded = System.Text.Encoding.ASCII.GetBytes(value);
        Array.Copy(encoded, 0, data, offset, Math.Min(encoded.Length, length - 1));
    }
}
