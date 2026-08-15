// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Simulation;

public enum MechWeaponKind
{
    Laser,
    PulseLaser,
    Ballistic,
    Missile
}

public sealed record MechWeaponSpecification(
    string Name,
    string HudName,
    MechWeaponKind Kind,
    int Damage,
    double RangeMeters,
    double RecycleSeconds,
    int ProjectilesPerShot,
    string SoundResourceName,
    uint BeamColorRgb = 0,
    int Heat = 0,
    int AmmoPerBin = 0)
{
    /// <summary>Whether this weapon consumes an authored MEK ammunition bin.</summary>
    public bool UsesAmmo => AmmoPerBin > 0;
}

public sealed record MechMountedWeapon(
    ushort SourceId,
    MechWeaponSpecification Specification,
    MechDamageSection Section,
    int AuthoredGroup);

/// <summary>
/// Maps the equipment-instance identifiers used by original MW2 MEK files to combat properties.
/// </summary>
public static class MechWeaponCatalog
{
    private static readonly IReadOnlyList<CatalogEntry> Entries =
    [
        new(0, 99, new MechWeaponSpecification(
            "LRM 20", "LRM20", MechWeaponKind.Missile, 1, 630, 10.0, 20, "MECBBAAT.SFL", Heat: 6, AmmoPerBin: 120)),
        new(100, 199, new MechWeaponSpecification(
            "LRM 15", "LRM15", MechWeaponKind.Missile, 1, 630, 5.5, 15, "MECBBAAT.SFL", Heat: 5, AmmoPerBin: 120)),
        new(200, 299, new MechWeaponSpecification(
            "LRM 10", "LRM10", MechWeaponKind.Missile, 1, 630, 5.0, 10, "MECBBAAT.SFL", Heat: 4, AmmoPerBin: 120)),
        new(300, 399, new MechWeaponSpecification(
            "LRM 5", "LRM5", MechWeaponKind.Missile, 1, 630, 4.5, 5, "MECBBAAT.SFL", Heat: 2, AmmoPerBin: 120)),
        new(400, 499, new MechWeaponSpecification(
            "SRM 6", "SRM6", MechWeaponKind.Missile, 2, 270, 4.0, 6, "MECBBAAT.SFL", Heat: 4, AmmoPerBin: 90)),
        new(500, 599, new MechWeaponSpecification(
            "SRM 4", "SRM4", MechWeaponKind.Missile, 2, 270, 3.5, 4, "MECBBAAT.SFL", Heat: 3, AmmoPerBin: 100)),
        new(600, 699, new MechWeaponSpecification(
            "SRM 2", "SRM2", MechWeaponKind.Missile, 2, 270, 3.0, 2, "MECBBAAT.SFL", Heat: 2, AmmoPerBin: 100)),
        new(700, 799, new MechWeaponSpecification(
            "Streak SRM 6", "SSRM6", MechWeaponKind.Missile, 2, 360, 4.0, 6, "MECBBAAT.SFL", Heat: 4, AmmoPerBin: 90)),
        new(800, 899, new MechWeaponSpecification(
            "Streak SRM 4", "SSRM4", MechWeaponKind.Missile, 2, 360, 3.5, 4, "MECBBAAT.SFL", Heat: 3, AmmoPerBin: 100)),
        new(900, 999, new MechWeaponSpecification(
            "Streak SRM 2", "SSRM2", MechWeaponKind.Missile, 2, 360, 3.0, 2, "MECBBAAT.SFL", Heat: 2, AmmoPerBin: 100)),
        new(1000, 1099, new MechWeaponSpecification(
            "Machine Gun", "MGUN", MechWeaponKind.Ballistic, 1, 270, 0.18, 1, "MECMGUN1.SFL", 0xffd060, AmmoPerBin: 200)),
        new(2100, 2199, new MechWeaponSpecification(
            "ER PPC", "ERPPC", MechWeaponKind.Laser, 15, 690, 7.5, 1, "MECMISNR.SFL", 0xe8f8ff, 15)),
        new(2200, 2299, new MechWeaponSpecification(
            "ER Large Laser", "ERLLAS", MechWeaponKind.Laser, 10, 750, 6.0, 1, "MECBLASR.SFL", 0x3080ff, 12)),
        new(2300, 2399, new MechWeaponSpecification(
            "ER Medium Laser", "ERMLAS", MechWeaponKind.Laser, 7, 450, 4.0, 1, "MECMLASR.SFL", 0x30ff50, 5)),
        new(2400, 2499, new MechWeaponSpecification(
            "ER Small Laser", "ERSLAS", MechWeaponKind.Laser, 5, 180, 3.0, 1, "MECSLASR.SFL", 0xff3020, 2)),
        new(2500, 2599, new MechWeaponSpecification(
            "Large Pulse Laser", "LPLAS", MechWeaponKind.PulseLaser, 10, 600, 6.0, 3, "MECPLASR.SFL", 0x3080ff, 10)),
        new(2600, 2699, new MechWeaponSpecification(
            "Medium Pulse Laser", "MPLAS", MechWeaponKind.PulseLaser, 7, 360, 5.0, 3, "MECPLASR.SFL", 0x30ff50, 4)),
        new(2700, 2799, new MechWeaponSpecification(
            "Small Pulse Laser", "SPLAS", MechWeaponKind.PulseLaser, 3, 180, 4.0, 3, "MECPLASR.SFL", 0xff3020, 2))
    ];

    public static bool TryGet(ushort sourceId, out MechWeaponSpecification specification)
    {
        var entry = Entries.FirstOrDefault(candidate => sourceId >= candidate.FirstId && sourceId <= candidate.LastId);
        specification = entry?.Specification;
        return specification != null;
    }

    private sealed record CatalogEntry(
        ushort FirstId,
        ushort LastId,
        MechWeaponSpecification Specification);
}
