namespace MechRewired.Resources;

public static class MechWarriorDataFile
{
    public const string ProjectArchive = "MW2.PRJ";
    public const string MipArchive = "MW2.MIP";
    public const string SkyAndGroundParameters = "SKYGND.PAR";

    public static IReadOnlyList<string> Preferred3DfxFiles { get; } =
    [
        ProjectArchive,
        MipArchive,
        SkyAndGroundParameters
    ];
}

