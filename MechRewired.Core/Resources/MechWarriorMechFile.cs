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
/// Decodes the general movement header from an original MW2 MEK configuration.
/// </summary>
public sealed class MechWarriorMechFile
{
    private const int GeneralHeaderSize = 24;
    private const double MovementPointSpeedKph = 10.8;

    private MechWarriorMechFile(int tonnage, int walkingMovementPoints)
    {
        Tonnage = tonnage;
        WalkingMovementPoints = walkingMovementPoints;
    }

    public int Tonnage { get; }

    public int WalkingMovementPoints { get; }

    public int RunningMovementPoints => (int)Math.Ceiling(WalkingMovementPoints * 1.5);

    public double CruisingSpeedKph => WalkingMovementPoints * MovementPointSpeedKph;

    public double MaximumSpeedKph => RunningMovementPoints * MovementPointSpeedKph;

    public static MechWarriorMechFile Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < GeneralHeaderSize)
        {
            throw new InvalidDataException(
                $"The MEK resource is {data.Length} bytes; at least {GeneralHeaderSize} bytes are required.");
        }

        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream);
        var tonnage = reader.ReadInt32();
        var walkingMovementPoints = reader.ReadInt32();
        if (tonnage <= 0)
        {
            throw new InvalidDataException($"The MEK tonnage must be positive; found {tonnage}.");
        }

        if (walkingMovementPoints <= 0)
        {
            throw new InvalidDataException(
                $"The MEK walking movement points must be positive; found {walkingMovementPoints}.");
        }

        return new MechWarriorMechFile(tonnage, walkingMovementPoints);
    }
}
