// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using Godot;
using MechRewired.Missions;
using MechRewired.Resources;
using MechRewired.Simulation;
using System.Globalization;

namespace MechRewired;

/// <summary>
/// Hosts the initial MechRewired Godot scene.
/// </summary>
/// <remarks>
/// Startup composition remains here while resource parsing and simulation live in the engine-independent core project.
/// </remarks>
public partial class Main : Node3D
{
    private const int SkyTopPaletteIndex = 224;
    private const int SkyHorizonPaletteIndex = 238;
    private const int GeneralIlluminationLevel = 12;
    private const int ObjectIlluminationLevel = 8;
    private const byte MaximumTexturedMechMaterialIndex = 63;
    private const byte AuthoredVaporMaterialIndex = 170;
    private const byte CamoMechMaterialIndex = 0;
    private const byte FlaggedCamoMechMaterialIndex = 0x70;
    private const byte WolfSmallInsigniaMaterialIndex = 0x14;
    private const byte JadeFalconSmallInsigniaMaterialIndex = 0x15;
    private const byte WolfLargeInsigniaMaterialIndex = 0x3C;
    private const byte JadeFalconLargeInsigniaMaterialIndex = 0xF0;
    private const float DefaultFogDistance = 1200.0f;
    private const float MinimumFogDistance = 300.0f;
    private const float MaximumFogDistance = 5000.0f;
    private const float MinimumSceneryObstacleHeight = 5.0f;
    private const string WolfScenarioPath = "BWD/YELLSCN1.BWD";
    private const string WolfPlayerMechPath = "MEK/MDG00STD.MEK";
    private const string JadeFalconScenarioPath = "BWD/PINKSCN1.BWD";
    private const string JadeFalconPlayerMechPath = "MEK/STM01STD.MEK";
    private static readonly IReadOnlyDictionary<string, string> DamageShapePrefixes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["battlemaster"] = "BTTL",
            ["direwolf"] = "DIRE",
            ["elemental"] = "ELEM",
            ["firemoth"] = "FIRE",
            ["gargoyle"] = "GARG",
            ["hellbringer"] = "HELL",
            ["jenner"] = "JENN",
            ["kitfox"] = "KITF",
            ["maddog"] = "MADD",
            ["marauder"] = "MARA",
            ["nova"] = "NOVA",
            ["rifleman"] = "RIFL",
            ["stormcrow"] = "STRM",
            ["summoner"] = "SUMM",
            ["tarantula"] = "TARA",
            ["timberwolf"] = "TIMB",
            ["warhammer"] = "WARH",
            ["warhawk"] = "WARK"
        };
    private static readonly string[] ExplosionDebrisPaths =
    [
        "POLY/CHUNKER1.WTB",
        "POLY/CHUNKER2.WTB",
        "POLY/CHUNKLET.WTB"
    ];

    private BattlefieldEffects m_battlefieldEffects;
    private static ClanCampaignSelection s_pendingCampaignRestart;
#if DEBUG
    private Node m_debugConsole;
    private PlayerHud m_debugHud;
    private PlayerCockpit m_debugCockpit;
    private MissionSkyController m_debugSky;
    private DebugVisualCapture m_debugVisualCapture;
    private TerrainDiagnostics m_debugTerrain;
    private GroundSandFog m_debugGroundSand;
#endif

    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        GD.Print(
            $"MechRewired: rendering with {RenderingServer.GetCurrentRenderingMethod()} " +
            $"on {RenderingServer.GetCurrentRenderingDriverName()}.");
#if DEBUG
        ConfigureDebugConsole();
#endif
        if (!TryOpenGameArchive(out var archive))
        {
            return;
        }

        if (s_pendingCampaignRestart != ClanCampaignSelection.None)
        {
            var restartCampaign = s_pendingCampaignRestart;
            s_pendingCampaignRestart = ClanCampaignSelection.None;
            GD.Print($"MechRewired: restarting {restartCampaign} campaign from the debug console.");
            StartCampaign(archive, restartCampaign);
            return;
        }

        if (TryGetRequestedCampaign(out var requestedCampaign))
        {
            GD.Print($"MechRewired: launching {requestedCampaign} directly from the command line.");
            StartCampaign(archive, requestedCampaign);
            return;
        }

        var clanSelection = new ClanSelectionScreen(archive)
        {
            Name = "ClanSelection"
        };
        clanSelection.CampaignSelected += campaign => StartCampaign(archive, campaign);
        AddChild(clanSelection);
    }

    private static bool TryGetRequestedCampaign(out ClanCampaignSelection campaign)
    {
        var arguments = OS.GetCmdlineUserArgs();
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--jade", StringComparison.OrdinalIgnoreCase))
            {
                campaign = ClanCampaignSelection.JadeFalcon;
                return true;
            }

            if (string.Equals(argument, "--wolf", StringComparison.OrdinalIgnoreCase))
            {
                campaign = ClanCampaignSelection.Wolf;
                return true;
            }

            const string assignmentPrefix = "--campaign=";
            if (argument.StartsWith(assignmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return TryParseCampaignName(argument[assignmentPrefix.Length..], out campaign);
            }

            if (!string.Equals(argument, "--campaign", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 < arguments.Length &&
                TryParseCampaignName(arguments[index + 1], out campaign))
            {
                return true;
            }

            GD.PushWarning(
                "MechRewired: --campaign expects 'jade' or 'wolf'; showing clan selection.");
            campaign = ClanCampaignSelection.None;
            return false;
        }

        campaign = ClanCampaignSelection.None;
        return false;
    }

    private static bool TryParseCampaignName(
        string name,
        out ClanCampaignSelection campaign)
    {
        campaign = name.Trim().ToLowerInvariant() switch
        {
            "jade" or "jadefalcon" or "jade-falcon" or "falcon" or "pink" =>
                ClanCampaignSelection.JadeFalcon,
            "wolf" or "wolfclan" or "wolf-clan" or "colmar" or "yellow" =>
                ClanCampaignSelection.Wolf,
            _ => ClanCampaignSelection.None
        };
        return campaign != ClanCampaignSelection.None;
    }

    private void StartCampaign(MechWarriorProjectArchive archive, ClanCampaignSelection campaign)
    {
        var clanSelection = GetNodeOrNull<ClanSelectionScreen>("ClanSelection");
        clanSelection?.Hide();
        var (scenarioPath, playerMechPath) = campaign switch
        {
            ClanCampaignSelection.JadeFalcon => (JadeFalconScenarioPath, JadeFalconPlayerMechPath),
            ClanCampaignSelection.Wolf => (WolfScenarioPath, WolfPlayerMechPath),
            _ => throw new ArgumentOutOfRangeException(nameof(campaign), campaign, "A Clan campaign must be selected.")
        };
        GD.Print($"MechRewired: selected {campaign} campaign ({scenarioPath}; {playerMechPath}).");
        if (!TryLoadGameData(
                archive,
                scenarioPath,
                playerMechPath,
                out var palette,
                out var playerChassis,
                out var playerChassisName,
                out var level,
                out var planet,
                out var luminosityTable,
                out var playerStart,
                out var navigationPoints,
                out var missionDefinition,
                out var playerMechDefinition,
                out var missionGamePieces,
                out var missionResources))
        {
            clanSelection?.Show();
            return;
        }

        try
        {
            var missionAreaBoundaries = LoadMissionAreaBoundaries(
                archive,
                missionResources,
                missionDefinition);
            BuildScene(
                archive,
                palette,
                playerChassis,
                playerChassisName,
                level,
                planet,
                luminosityTable,
                playerStart,
                navigationPoints,
                missionAreaBoundaries,
                missionDefinition,
                playerMechDefinition,
                missionGamePieces,
                missionResources);
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot render the scene: {exception.Message}");
            clanSelection?.Show();
            return;
        }

        clanSelection?.QueueFree();
    }

    private static bool TryOpenGameArchive(out MechWarriorProjectArchive archive)
    {
        archive = null;
        try
        {
            var projectDirectory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
            var repositoryDirectory = projectDirectory.Parent ??
                                      throw new DirectoryNotFoundException("The MechRewired repository directory could not be resolved.");
            var dataDirectory = new DirectoryInfo(Path.Combine(repositoryDirectory.FullName, "local", "game-data"));
            var projectArchive = MechWarriorResourceCheck.CheckDosFiles(dataDirectory);
            archive = MechWarriorProjectArchive.Open(projectArchive);
            GD.Print($"MechRewired: indexed {archive.Entries.Count:N0} resources from {projectArchive.Name} ({projectArchive.Length:N0} bytes).");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot load original game data: {exception}");
            return false;
        }
    }

#if DEBUG
    private void ConfigureDebugConsole()
    {
        m_debugConsole = GetNodeOrNull<Node>("/root/Console");
        if (m_debugConsole == null)
        {
            GD.PushWarning("MechRewired: debug console autoload is unavailable.");
            return;
        }

        m_debugConsole.Call(
            "add_command",
            "version",
            Callable.From(PrintApplicationVersion),
            0,
            0,
            "Reports the MechRewired application version.");
        m_debugConsole.Call(
            "add_command",
            "falcon",
            Callable.From(() => RestartCampaign(ClanCampaignSelection.JadeFalcon)),
            0,
            0,
            "Restarts directly into the Jade Falcon first mission.");
        m_debugConsole.Call(
            "add_command",
            "wolf",
            Callable.From(() => RestartCampaign(ClanCampaignSelection.Wolf)),
            0,
            0,
            "Restarts directly into the Wolf first mission.");
    }

    private void RestartCampaign(ClanCampaignSelection campaign)
    {
        s_pendingCampaignRestart = campaign;
        CallDeferred(nameof(ReloadCurrentScene));
    }

    private void ReloadCurrentScene() => GetTree().ReloadCurrentScene();

    private void RegisterDebugConsoleHud(PlayerHud playerHud)
    {
        m_debugHud = playerHud;
        if (m_debugConsole == null)
        {
            return;
        }

        m_debugConsole.Call(
            "add_cvar",
            "hud.glow",
            playerHud.HudGlow,
            "Controls the soft halo around green HUD lines and text (0 disables it; default 1).");
        m_debugConsole.Call(
            "add_cvar",
            "hud.glow.radius",
            playerHud.HudGlowRadius,
            "Controls the HUD halo spread in pixels (0 disables the spread; default 8). Use with hud.glow for a wide, dim halo.");
        m_debugConsole.Connect(
            "console_cvar_changed",
            Callable.From<string, Variant>(OnDebugConsoleCvarChanged));
    }

    private void RegisterDebugConsoleTargeting(PlayerTargeting targeting)
    {
        if (m_debugConsole == null)
        {
            return;
        }

        m_debugConsole.Call(
            "add_command",
            "targeting.status",
            Callable.From(targeting.LogTargetingState),
            0,
            0,
            "Lists remaining targets for each active mission objective.");
    }

    private void RegisterDebugConsoleCockpit(PlayerCockpit cockpit)
    {
        m_debugCockpit = cockpit;
        if (m_debugConsole == null)
        {
            return;
        }

        m_debugConsole.Call(
            "add_cvar",
            "cockpit.texture_scale",
            cockpit.FrameTextureScale,
            "Controls Metal029 texture repetitions per cockpit metre (default 1.5).");
        m_debugConsole.Call(
            "add_cvar",
            "cockpit.metallic",
            cockpit.FrameMetallic,
            "Controls cockpit-frame metalness from 0 to 1 (default 0.75).");
        m_debugConsole.Call(
            "add_cvar",
            "cockpit.roughness",
            cockpit.FrameRoughness,
            "Controls cockpit-frame reflection blur from 0 to 1 (default 0.60).");
        m_debugConsole.Call(
            "add_command",
            "cockpit.inspect",
            Callable.From<string>(SetCockpitInspection),
            new[] { "view" },
            1,
            "Shows cockpit-frame lit, albedo, normal, normalmap, roughness, metallic or directsun diagnostics. Use all to capture every view.");
        m_debugConsole.Call(
            "add_command",
            "cockpit.inspect_all",
            Callable.From(CaptureCockpitInspection),
            0,
            0,
            "Writes the complete cockpit material diagnostic capture set.");
        m_debugConsole.Call(
            "add_command",
            "cockpit.material_sweep",
            Callable.From(CaptureCockpitMaterialSweep),
            0,
            0,
            "Writes 12 labelled cockpit metallic/roughness comparisons from the current view.");
    }

    private void SetCockpitInspection(string view)
    {
        if (m_debugCockpit == null)
        {
            return;
        }

        if (string.Equals(view, "all", StringComparison.OrdinalIgnoreCase))
        {
            CaptureCockpitInspection();
            return;
        }

        if (!m_debugCockpit.TrySetFrameDiagnosticMode(view))
        {
            m_debugConsole?.Call(
                "print_warning",
                "Unknown cockpit view. Use lit, albedo, normal, normalmap, roughness, metallic, directsun or all.");
            return;
        }

        m_debugConsole?.Call(
            "print_line",
            $"MechRewired: cockpit inspection set to {m_debugCockpit.FrameDiagnosticModeName}.");
    }

    private void CaptureCockpitInspection()
    {
        if (m_debugCockpit == null || m_debugVisualCapture == null)
        {
            return;
        }

        m_debugVisualCapture.CaptureCockpitDiagnostics(m_debugCockpit);
    }

    private void CaptureCockpitMaterialSweep()
    {
        if (m_debugCockpit == null || m_debugVisualCapture == null)
        {
            return;
        }

        m_debugVisualCapture.CaptureCockpitMaterialSweep(m_debugCockpit);
    }

    private void RegisterDebugConsoleTerrain(TerrainDiagnostics terrain)
    {
        m_debugTerrain = terrain;
        if (m_debugConsole == null)
        {
            return;
        }

        m_debugConsole.Call(
            "add_command",
            "terrain.inspect",
            Callable.From<string>(SetTerrainInspection),
            new[] { "view" },
            1,
            "Shows terrain lit, albedo, raw, normal, rock, directsun or roughness diagnostics. Use terrain.inspect_all to capture every view.");
        m_debugConsole.Call(
            "add_command",
            "terrain.inspect_all",
            Callable.From(CaptureTerrainInspection),
            0,
            0,
            "Writes the complete terrain diagnostic capture set from the current camera.");
        m_debugConsole.Call(
            "add_command",
            "terrain.capture_stones",
            Callable.From(() => m_debugVisualCapture?.CaptureTerrainStoneFixture(m_debugTerrain)),
            0,
            0,
            "Recreates and captures the scattered-rock terrain regression view.");
        m_debugConsole.Call(
            "add_command",
            "terrain.capture_seam",
            Callable.From(() => m_debugVisualCapture?.CaptureTerrainSeamFixture(m_debugTerrain)),
            0,
            0,
            "Recreates and captures the authored terrain-seam regression views.");
        m_debugConsole.Call(
            "add_command",
            "terrain.capture_jade_mountain",
            Callable.From(() => m_debugVisualCapture?.CaptureJadeMountainFixture(m_debugTerrain)),
            0,
            0,
            "Recreates and captures the Jade mountain-base and sunlit-crest regression view.");
        m_debugConsole.Call(
            "add_command",
            "terrain.parallax_sweep",
            Callable.From(() => m_debugVisualCapture?.CaptureTerrainParallaxSweep(m_debugTerrain)),
            0,
            0,
            "Captures the fixed terrain view with parallax off, default and strong.");
        RegisterTerrainInspectionCommand("terrain.inspect_lit", "lit");
        RegisterTerrainInspectionCommand("terrain.inspect_albedo", "albedo");
        RegisterTerrainInspectionCommand("terrain.inspect_raw", "raw");
        RegisterTerrainInspectionCommand("terrain.inspect_normal", "normal");
        RegisterTerrainInspectionCommand("terrain.inspect_rock", "rock");
        RegisterTerrainInspectionCommand("terrain.inspect_directsun", "directsun");
        RegisterTerrainInspectionCommand("terrain.inspect_roughness", "roughness");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.texture_scale",
            terrain.TextureScale,
            "Controls terrain texture repetitions per rendered metre (default 0.05).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.detail",
            terrain.DetailStrength,
            "Controls fine terrain texture detail from 0 to 1 (default 0.78).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.normal",
            terrain.NormalStrength,
            "Controls terrain normal-map strength from 0 to 2 (default 1.0).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.dunes",
            terrain.DunePatchCoverage,
            "Controls lowland dune-field coverage from 0 to 1 (default 0.25).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.hardpan",
            terrain.HardpanPatchCoverage,
            "Controls compacted brown-soil coverage from 0 to 1 (default 0.08).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.stones",
            terrain.StonePatchCoverage,
            "Controls scattered-rock patch coverage from 0 to 1 (default 0.10).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.stone_scale",
            terrain.StoneTextureScale,
            "Scales the scattered-rock texture relative to the terrain texture (default 0.5).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.parallax",
            terrain.ParallaxDepthMetres,
            "Sets terrain parallax depth in metres from 0 to 1 (default 0.15).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.rock_start",
            terrain.RockSlopeStartDegrees,
            "Sets the slope in degrees where sandstone begins blending in (default 12).");
        m_debugConsole.Call(
            "add_cvar",
            "terrain.rock_end",
            terrain.RockSlopeEndDegrees,
            "Sets the slope in degrees which becomes fully sandstone (default 38).");

        if (OS.GetCmdlineUserArgs().Contains("--capture-terrain-stones"))
        {
            m_debugVisualCapture?.CaptureTerrainStoneFixture(
                m_debugTerrain,
                quitAfterCapture: true);
        }
        else if (OS.GetCmdlineUserArgs().Contains("--capture-terrain-seam"))
        {
            m_debugVisualCapture?.CaptureTerrainSeamFixture(
                m_debugTerrain,
                quitAfterCapture: true);
        }
        else if (OS.GetCmdlineUserArgs().Contains("--capture-jade-mountain"))
        {
            m_debugVisualCapture?.CaptureJadeMountainFixture(
                m_debugTerrain,
                quitAfterCapture: true);
        }
        else if (OS.GetCmdlineUserArgs().Contains("--capture-terrain-parallax-sweep"))
        {
            m_debugVisualCapture?.CaptureTerrainParallaxSweep(
                m_debugTerrain,
                quitAfterCapture: true);
        }
    }

    private void RegisterTerrainInspectionCommand(string command, string view)
    {
        m_debugConsole?.Call(
            "add_command",
            command,
            Callable.From(() => SetTerrainInspection(view)),
            0,
            0,
            $"Shows the terrain {view} diagnostic view.");
    }

    private void SetTerrainInspection(string view)
    {
        if (m_debugTerrain == null)
        {
            return;
        }

        if (!m_debugTerrain.TrySetMode(view))
        {
            m_debugConsole?.Call(
                "print_warning",
                "Unknown terrain view. Use lit, albedo, raw, normal, rock, directsun or roughness; " +
                "terrain.inspect_all captures the complete set.");
            return;
        }

        m_debugConsole?.Call(
            "print_line",
            $"MechRewired: terrain inspection set to {m_debugTerrain.ModeName}.");
    }

    private void CaptureTerrainInspection()
    {
        if (m_debugTerrain == null || m_debugVisualCapture == null)
        {
            return;
        }

        m_debugVisualCapture.CaptureTerrainDiagnostics(m_debugTerrain);
    }

    private void RegisterDebugConsoleSky(
        MissionSkyController sky,
        Camera3D camera,
        string missionId)
    {
        m_debugSky = sky;
        m_debugVisualCapture = new DebugVisualCapture(sky, camera, missionId, m_debugConsole)
        {
            Name = "DebugVisualCapture"
        };
        AddChild(m_debugVisualCapture);
        if (m_debugConsole == null)
        {
            return;
        }

        m_debugConsole.Call(
            "add_cvar",
            "sky.time",
            sky.TimeOfDay,
            "Sets the fixed MW2 mission time in hours (0-24; default comes from INIT).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.cloud.coverage",
            sky.CloudCoverage,
            "Sets sparse high-cloud coverage from 0 to 1 (desert default 0.4).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.cloud.density",
            sky.CloudDensity,
            "Sets high-cloud brightness/density (default 1.0).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.cloud.height",
            sky.CloudHeight,
            "Sets apparent high-cloud scale/height (default 1.8).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.fog.multiplier",
            sky.FogMultiplier,
            "Scales the level-authored atmospheric depth cue (biome-tuned default).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.fog.start",
            sky.FogStartFraction,
            "Sets where fog begins as a fraction of its full authored distance (biome-tuned default).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.fog.aerial",
            sky.FogAerialPerspective,
            "Blends distant objects toward the sky colour behind them (0-1; biome-tuned default).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.fog.sun_scatter",
            sky.FogSunScatter,
            "Adds directional sunlight to atmospheric haze (0-1; biome-tuned default).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.sun.azimuth_offset",
            sky.SunAzimuthOffsetDegrees,
            "Offsets the INIT-derived sun direction in degrees for visual tuning.");
        m_debugConsole.Call(
            "add_cvar",
            "sky.sun.flare",
            sky.SunLensFlareIntensity,
            "Sets the uncapped sun lens-flare multiplier (default 3).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.sun.god_rays",
            sky.SunGodRayStrength,
            "Sets the depth-occluded sun-shaft strength from 0 to 0.2.");
        m_debugConsole.Call(
            "add_cvar",
            "sky.shadow.distance",
            sky.SunShadowDistance,
            "Sets the directional-sun shadow coverage distance in metres.");
        m_debugConsole.Call(
            "add_cvar",
            "sky.shadow.opacity",
            sky.SunShadowOpacity,
            "Sets sun-shadow opacity from 0 to 1 (default 0.90 for readable terrain and mech shadows).");
        m_debugConsole.Call(
            "add_cvar",
            "sky.exposure",
            sky.Exposure,
            "Sets Sky3D tonemap exposure (default 1).");
        m_debugConsole.Call(
            "add_command",
            "visual.capture",
            Callable.From<string>(m_debugVisualCapture.Capture),
            new[] { "preset" },
            1,
            "Writes a named visual baseline: authored, day, dusk or night.");
        m_debugConsole.Call(
            "add_command",
            "visual.capture_all",
            Callable.From(m_debugVisualCapture.CaptureAll),
            0,
            0,
            "Writes authored, day, dusk and night visual baselines.");
        m_debugConsole.Call(
            "add_command",
            "snap",
            Callable.From(m_debugVisualCapture.Snap),
            0,
            0,
            "Closes the console and saves a timestamped screenshot to Downloads.");
    }

    private void RegisterDebugConsoleGroundSand(GroundSandFog groundSand)
    {
        m_debugGroundSand = groundSand;
        if (m_debugConsole == null)
        {
            return;
        }

        m_debugConsole.Call(
            "add_cvar",
            "sand.enabled",
            groundSand.Enabled ? 1.0f : 0.0f,
            "Enables the near-field windblown sand layer (0 or 1).");
        m_debugConsole.Call(
            "add_cvar",
            "sand.density",
            groundSand.Density,
            "Sets sand-sheet opacity/density (default 0.20).");
        m_debugConsole.Call(
            "add_cvar",
            "sand.coverage",
            groundSand.Coverage,
            "Sets how much ground is occupied by moving sand sheets (0-1; default 0.50).");
        m_debugConsole.Call(
            "add_cvar",
            "sand.speed",
            groundSand.WindSpeed,
            "Sets windblown sand drift speed in metres per second (default 10).");
        m_debugConsole.Call(
            "add_cvar",
            "sand.height",
            groundSand.Height,
            "Sets the maximum height of the sand layer in metres (default 3).");
        m_debugConsole.Call(
            "add_cvar",
            "sand.fill",
            groundSand.Fill,
            "Adds a warm ambient lift to sand shadowing (default 0.25).");
        if (m_battlefieldEffects != null)
        {
            m_debugConsole.Call(
                "add_cvar",
                "vfx.explosion_fog",
                m_battlefieldEffects.ExplosionFogDensity,
                "Sets initial localized volumetric smoke density for major explosions (default 0.40).");
            m_debugConsole.Call(
                "add_command",
                "vfx.test_explosion_fog",
                Callable.From(m_battlefieldEffects.SpawnExplosionFogTest),
                0,
                0,
                "Spawns only a major-explosion fog volume 18m ahead for visibility testing.");
        }
    }

    private void OnDebugConsoleCvarChanged(string name, Variant value)
    {
        if (!float.TryParse(
                value.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var numericValue))
        {
            return;
        }

        if (string.Equals(name, "hud.glow", StringComparison.OrdinalIgnoreCase) &&
            m_debugHud != null)
        {
            m_debugHud.HudGlow = numericValue;
        }
        else if (string.Equals(name, "hud.glow.radius", StringComparison.OrdinalIgnoreCase) &&
                 m_debugHud != null)
        {
            m_debugHud.HudGlowRadius = numericValue;
        }
        else if (string.Equals(name, "cockpit.texture_scale", StringComparison.OrdinalIgnoreCase) &&
                 m_debugCockpit != null)
        {
            m_debugCockpit.FrameTextureScale = numericValue;
        }
        else if (string.Equals(name, "cockpit.metallic", StringComparison.OrdinalIgnoreCase) &&
                 m_debugCockpit != null)
        {
            m_debugCockpit.FrameMetallic = numericValue;
        }
        else if (string.Equals(name, "cockpit.roughness", StringComparison.OrdinalIgnoreCase) &&
                 m_debugCockpit != null)
        {
            m_debugCockpit.FrameRoughness = numericValue;
        }
        else if (string.Equals(name, "terrain.texture_scale", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.TextureScale = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.detail", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.DetailStrength = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.normal", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.NormalStrength = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.dunes", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.DunePatchCoverage = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.hardpan", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.HardpanPatchCoverage = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.stones", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.StonePatchCoverage = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.stone_scale", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.StoneTextureScale = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.parallax", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.ParallaxDepthMetres = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.rock_start", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.RockSlopeStartDegrees = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "terrain.rock_end", StringComparison.OrdinalIgnoreCase) &&
                 m_debugTerrain != null)
        {
            m_debugTerrain.RockSlopeEndDegrees = numericValue;
            m_debugTerrain.ApplySurfaceTuning();
        }
        else if (string.Equals(name, "sky.time", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.TimeOfDay = numericValue;
        }
        else if (string.Equals(name, "sky.cloud.coverage", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.CloudCoverage = numericValue;
        }
        else if (string.Equals(name, "sky.cloud.density", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.CloudDensity = numericValue;
        }
        else if (string.Equals(name, "sky.cloud.height", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.CloudHeight = numericValue;
        }
        else if (string.Equals(name, "sky.fog.multiplier", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.FogMultiplier = numericValue;
        }
        else if (string.Equals(name, "sky.fog.start", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.FogStartFraction = numericValue;
        }
        else if (string.Equals(name, "sky.fog.aerial", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.FogAerialPerspective = numericValue;
        }
        else if (string.Equals(name, "sky.fog.sun_scatter", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.FogSunScatter = numericValue;
        }
        else if (string.Equals(name, "sky.sun.azimuth_offset", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.SunAzimuthOffsetDegrees = numericValue;
        }
        else if (string.Equals(name, "sky.sun.flare", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.SunLensFlareIntensity = numericValue;
        }
        else if (string.Equals(name, "sky.sun.god_rays", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.SunGodRayStrength = numericValue;
        }
        else if (string.Equals(name, "sky.shadow.distance", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.SunShadowDistance = numericValue;
        }
        else if (string.Equals(name, "sky.shadow.opacity", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.SunShadowOpacity = numericValue;
        }
        else if (string.Equals(name, "sky.exposure", StringComparison.OrdinalIgnoreCase) &&
                 m_debugSky != null)
        {
            m_debugSky.Exposure = numericValue;
        }
        else if (string.Equals(name, "sand.enabled", StringComparison.OrdinalIgnoreCase) &&
                 m_debugGroundSand != null)
        {
            m_debugGroundSand.Enabled = numericValue >= 0.5f;
        }
        else if (string.Equals(name, "sand.density", StringComparison.OrdinalIgnoreCase) &&
                 m_debugGroundSand != null)
        {
            m_debugGroundSand.Density = numericValue;
        }
        else if (string.Equals(name, "sand.coverage", StringComparison.OrdinalIgnoreCase) &&
                 m_debugGroundSand != null)
        {
            m_debugGroundSand.Coverage = numericValue;
        }
        else if (string.Equals(name, "sand.speed", StringComparison.OrdinalIgnoreCase) &&
                 m_debugGroundSand != null)
        {
            m_debugGroundSand.WindSpeed = numericValue;
        }
        else if (string.Equals(name, "sand.height", StringComparison.OrdinalIgnoreCase) &&
                 m_debugGroundSand != null)
        {
            m_debugGroundSand.Height = numericValue;
        }
        else if (string.Equals(name, "sand.fill", StringComparison.OrdinalIgnoreCase) &&
                 m_debugGroundSand != null)
        {
            m_debugGroundSand.Fill = numericValue;
        }
        else if (string.Equals(name, "vfx.explosion_fog", StringComparison.OrdinalIgnoreCase) &&
                 m_battlefieldEffects != null)
        {
            m_battlefieldEffects.ExplosionFogDensity = numericValue;
        }
    }

    private void PrintApplicationVersion()
    {
        var applicationVersion = ProjectSettings
            .GetSetting("application/config/version", "0.1.0")
            .ToString();
        var engineVersion = Engine.GetVersionInfo()["string"].ToString();
        m_debugConsole?.Call("print_line", $"MechRewired {applicationVersion} (Godot {engineVersion})");
    }
#endif

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            return;
        }

#if DEBUG
        if (m_battlefieldEffects?.TryHandleDebugInput(keyEvent) == true)
        {
            GetViewport().SetInputAsHandled();
        }
#endif
    }

    private static bool TryLoadGameData(
        MechWarriorProjectArchive archive,
        string scenarioPath,
        string playerMechPath,
        out MechWarriorPalette palette,
        out MechWarriorMechChassis playerChassis,
        out string playerChassisName,
        out MechWarriorLevel level,
        out MechWarriorWorldFile planet,
        out MechWarriorLuminosityTable luminosityTable,
        out MechWarriorWorldNavPoint playerStart,
        out IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        out MissionDefinition missionDefinition,
        out MechWarriorMechFile playerMechDefinition,
        out IReadOnlyList<MechWarriorMissionGamePiece> missionGamePieces,
        out MechWarriorMissionResources missionResources)
    {
        palette = null;
        playerChassis = null;
        playerChassisName = null;
        level = null;
        planet = null;
        luminosityTable = null;
        playerStart = null;
        navigationPoints = null;
        missionDefinition = null;
        playerMechDefinition = null;
        missionGamePieces = null;
        missionResources = null;
        try
        {
            ArgumentNullException.ThrowIfNull(archive);
            var resolvedMissionResources = MechWarriorMissionResources.Load(archive, scenarioPath);
            missionResources = resolvedMissionResources;
            GD.Print(
                $"MechRewired: resolved mission {missionResources.ScenarioEntry.Path} " +
                $"(prefix {missionResources.MissionPrefix}; palette {missionResources.PaletteEntry.Path}; " +
                $"world {missionResources.Level.Entry.Path}; planet {missionResources.Planet.Entry.Path}; " +
                $"deployment {missionResources.PlayerStart.Entry.Path}; " +
                $"{missionResources.NavigationPoints.Count} navigation references).");

            var paletteEntry = missionResources.PaletteEntry;
            palette = MechWarriorPalette.Load(archive.ReadEntry(paletteEntry));

            var playerMechEntry = archive.GetEntry(playerMechPath);
            var mechCatalog = MechWarriorMechCatalog.Load(archive);
            var playerChassisIdentity = mechCatalog.ResolveConfiguration(playerMechEntry.Name);
            var playerChassisEntry = archive.GetEntry(
                $"BWD/{playerChassisIdentity.ResourceName.ToUpperInvariant()}.BWD");
            playerChassis = MechWarriorMechChassis.Load(archive.ReadEntry(playerChassisEntry));
            playerChassisName = playerChassisIdentity.DisplayName;
            GD.Print(
                $"MechRewired: configured player {playerChassisName} " +
                $"({playerChassisIdentity.Tonnage} tons, {playerChassis.PointsOfFire.Count} firing points).");
            playerMechDefinition = MechWarriorMechFile.Load(archive.ReadEntry(playerMechEntry));
            GD.Print(
                $"MechRewired: loaded player loadout ({playerMechDefinition.Weapons.Count} weapons, " +
                $"{playerMechDefinition.HeatSinkCount} heat sinks, {playerMechDefinition.AmmoBinCount} ammo bins" +
                (playerMechDefinition.UnsupportedWeaponIds.Count == 0
                    ? string.Empty
                    : $", unsupported weapon IDs [{string.Join(", ", playerMechDefinition.UnsupportedWeaponIds)}]") +
                ").");
            var planetEntry = missionResources.Planet.Entry;
            planet = MechWarriorWorldFile.Load(archive.ReadEntry(planetEntry));
            GD.Print(
                $"MechRewired: loaded mission lighting (time {planet.TimeOfDay}; " +
                $"view distance {planet.ViewDistance:F0}m; luma {planet.LuminosityTable}).");
            var luminosityEntry = archive.GetEntry($"LUMA/{planet.LuminosityTable}.TBL");
            luminosityTable = MechWarriorLuminosityTable.Load(archive.ReadEntry(luminosityEntry));

            var playerStartEntry = missionResources.PlayerStart.Entry;
            var playerStartWorld = MechWarriorWorldFile.Load(
                archive.ReadEntry(playerStartEntry),
                missionResources.PlayerStart.Include.Transform);
            if (playerStartWorld.NavPoints.Count != 1)
            {
                throw new InvalidDataException(
                    $"{playerStartEntry.Path} contains {playerStartWorld.NavPoints.Count} deployment points; expected one.");
            }

            playerStart = playerStartWorld.NavPoints[0];
            GD.Print("MechRewired: loaded player deployment.");

            var scenarioEntry = missionResources.ScenarioEntry;
            var scenario = missionResources.Scenario;
            missionDefinition = LoadMissionDefinition(scenarioEntry, scenario);
            navigationPoints = LoadMissionNavigationPoints(archive, missionResources);
            missionGamePieces = MechWarriorMissionGamePieceLoader.Load(archive, scenario);
            GD.Print(
                $"MechRewired: resolved {missionGamePieces.Count} mission game pieces " +
                $"({missionGamePieces.Count(piece => piece.Star.Disposition == MechWarriorMissionDisposition.Hostile)} hostile).");

            level = MechWarriorLevel.Load(
                archive,
                missionResources.Level.Entry.Path,
                include =>
                {
                    var includeEntry = archive.GetEntry("BWD", include.ResourceIndex);
                    var includeWorld = MechWarriorWorldFile.Load(archive.ReadEntry(includeEntry));
                    var isAnimatedDropShip = WorldHasTaskArgument(includeWorld, "drop");
                    return !isAnimatedDropShip;
                });

            GD.Print(
                $"MechRewired: assembled {missionResources.MissionPrefix} mission world ({level.Sources.Count} BWD resources, " +
                $"{level.TerrainObjects.Count} terrain objects, {level.SceneryObjects.Count} scenery objects, " +
                $"{level.DebrisObjects.Count} debris objects, {level.EffectObjects.Count} effect controls, " +
                $"{level.Actors.Count} actors).");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot load original game data: {exception.Message}");
            return false;
        }
    }

    private static MissionDefinition LoadMissionDefinition(
        MechWarriorProjectEntry scenarioEntry,
        MechWarriorWorldFile scenario)
    {
        var missionTable = scenario.MissionTables.SingleOrDefault(table => table.Index == 0) ??
                           throw new InvalidDataException(
                               $"{scenarioEntry.Path} does not contain the default MTBL mission table.");
        var definition = MissionDefinition.FromMissionTable(missionTable);
        if (definition.Objectives.Count == 0)
        {
            throw new InvalidDataException(
                $"{scenarioEntry.Path} MTBL {missionTable.Index} contains no supported objectives.");
        }

        GD.Print(
            $"MechRewired: decoded {scenario.MissionTables.Count} mission tables from {scenarioEntry.Path}; " +
            $"default table has {missionTable.Entries.Count} records and " +
            $"{definition.Objectives.Count} supported objectives.");
        return definition;
    }

    private static IReadOnlyList<MechWarriorMissionNavigationPoint> LoadMissionNavigationPoints(
        MechWarriorProjectArchive archive,
        MechWarriorMissionResources missionResources)
    {
        var navigationPoints = new List<MechWarriorMissionNavigationPoint>();
        foreach (var navigationResource in missionResources.NavigationPoints)
        {
            var navigationEntry = navigationResource.Entry;
            var navigationWorld = MechWarriorWorldFile.Load(
                archive.ReadEntry(navigationEntry),
                navigationResource.Include.Transform);
            if (navigationWorld.NavPoints.Count != 1)
            {
                throw new InvalidDataException(
                    $"{navigationEntry.Path} contains {navigationWorld.NavPoints.Count} navigation points; expected one.");
            }

            var navigationPoint = navigationWorld.NavPoints[0];
            if (!navigationPoint.Targetable || string.IsNullOrWhiteSpace(navigationPoint.Description))
            {
                throw new InvalidDataException(
                    $"{navigationEntry.Path} does not contain a named, targetable navigation point.");
            }

            navigationPoints.Add(new MechWarriorMissionNavigationPoint(
                Path.GetFileNameWithoutExtension(navigationEntry.Name),
                navigationPoint));
        }

        if (navigationPoints.Count == 0)
        {
            throw new InvalidDataException(
                $"{missionResources.ScenarioEntry.Path} contains no named navigation includes.");
        }

        GD.Print(
            $"MechRewired: loaded {navigationPoints.Count} mission navigation points from " +
            $"{missionResources.ScenarioEntry.Path}.");
        return navigationPoints.AsReadOnly();
    }

    private static IReadOnlyList<MechWarriorMissionAreaBoundary> LoadMissionAreaBoundaries(
        MechWarriorProjectArchive archive,
        MechWarriorMissionResources missionResources,
        MissionDefinition missionDefinition)
    {
        var boundaryNames = missionDefinition.EventReports
            .Select(report => report.Trigger)
            .Concat(missionDefinition.FailureEvents)
            .Where(missionEvent => missionEvent.Kind == MissionEventKind.MissionAreaBoundaryExited)
            .Select(missionEvent => missionEvent.TargetResourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (boundaryNames.Length == 0)
        {
            return Array.Empty<MechWarriorMissionAreaBoundary>();
        }

        var resourcesByName = missionResources.MissionAreaBoundaries.ToDictionary(
            resource => Path.GetFileNameWithoutExtension(resource.Entry.Name),
            StringComparer.OrdinalIgnoreCase);
        var boundaries = new List<MechWarriorMissionAreaBoundary>();
        foreach (var boundaryName in boundaryNames)
        {
            if (!resourcesByName.TryGetValue(boundaryName, out var boundaryResource))
            {
                throw new InvalidDataException(
                    $"{missionResources.ScenarioEntry.Path} MTBL refers to missing mission-area boundary " +
                    $"BWD/{boundaryName}.BWD.");
            }

            var boundaryWorld = MechWarriorWorldFile.Load(
                archive.ReadEntry(boundaryResource.Entry),
                boundaryResource.Include.Transform);
            if (boundaryWorld.NavPoints.Count != 1)
            {
                throw new InvalidDataException(
                    $"{boundaryResource.Entry.Path} contains {boundaryWorld.NavPoints.Count} boundary points; " +
                    "expected one.");
            }

            var point = boundaryWorld.NavPoints[0];
            if ((point.ActionFlags & (int)MechWarriorMissionAction.Leave) == 0)
            {
                throw new InvalidDataException(
                    $"{boundaryResource.Entry.Path} NAVP does not carry the authored Leave action.");
            }

            boundaries.Add(new MechWarriorMissionAreaBoundary(boundaryName, point));
        }

        var ordered = boundaries.OrderBy(boundary => boundary.Point.Radius).ToArray();
        GD.Print(
            $"MechRewired: loaded {ordered.Length} mission-area boundaries from " +
            $"{missionResources.ScenarioEntry.Path}.");
        return ordered;
    }

    private void BuildScene(
        MechWarriorProjectArchive archive,
        MechWarriorPalette palette,
        MechWarriorMechChassis playerChassis,
        string playerChassisName,
        MechWarriorLevel level,
        MechWarriorWorldFile planet,
        MechWarriorLuminosityTable luminosityTable,
        MechWarriorWorldNavPoint playerStart,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        IReadOnlyList<MechWarriorMissionAreaBoundary> missionAreaBoundaries,
        MissionDefinition missionDefinition,
        MechWarriorMechFile playerMechDefinition,
        IReadOnlyList<MechWarriorMissionGamePiece> missionGamePieces,
        MechWarriorMissionResources missionResources)
    {
        var runtimeContent = new MissionRuntimeContent();
        var terrainBiome = level.TerrainBiome;
        var usesDesertTerrain = terrainBiome == MechWarriorTerrainBiome.Desert;
        var atmosphericVisibilityRange = CalculateVisibilityDistance(planet.ViewDistance);
        var atmosphericDepthCueRange = CalculateDepthCueDistance(
            planet.Lighting?.ShadeDistance,
            atmosphericVisibilityRange);
        var skyProfile = MissionSkyProfile.FromWorld(
            planet,
            palette,
            atmosphericDepthCueRange,
            atmosphericVisibilityRange,
            SkyTopPaletteIndex,
            SkyHorizonPaletteIndex,
            17,
            terrainBiome);
        var missionSky = MissionSkyController.Create(this, skyProfile);
        GD.Print("MechRewired: configured mission atmosphere.");

        var levelRoot = new Node3D
        {
            Name = "MissionWorld"
        };
        AddChild(levelRoot);
        var playerUsesJadeFalconDecals = string.Equals(
            missionResources.MissionPrefix,
            "PINK",
            StringComparison.OrdinalIgnoreCase);
        var terrainMaterial = usesDesertTerrain
            ? TerrainSurfaceMaterial.Create(TerrainSurfaceKind.Desert)
            : null;
        var terrainWireframeMaterial = usesDesertTerrain
            ? TerrainSurfaceMaterial.CreateWireframe(TerrainSurfaceKind.Desert)
            : null;
        // MW2's material selectors are context-sensitive. The first playable mech set uses the
        // low material-bank range; scenery selectors may instead be palette/visibility flags.
        // Keep indexed albedo lookup scoped to the verified mech range below.
        var materialMapEntry = archive.GetEntry("BWD/MW2_MAP1.BWD");
        var materialMap = MechWarriorMaterialMap.Load(archive.ReadEntry(materialMapEntry), 1);
        var authoredVaporTexture = LoadOriginalIndexedTexture(
            archive,
            materialMap,
            palette,
            AuthoredVaporMaterialIndex);
        var materialImages = new Dictionary<byte, MechWarriorIndexedImage>();
        var playerMaterialImages = new Dictionary<byte, MechWarriorIndexedImage>();
        TerrainDiagnostics terrainDiagnostics = null;
#if DEBUG
        terrainDiagnostics = new TerrainDiagnostics
        {
            Name = "TerrainDiagnostics"
        };
        AddChild(terrainDiagnostics);
#endif

        var actorRoot = new Node3D
        {
            Name = "Actors"
        };
        AddChild(actorRoot);
        var explosionDebrisMeshes = ExplosionDebrisPaths.SelectMany(path =>
        {
            var entry = archive.GetEntry(path);
            var models = MechWarriorModel.LoadAll(archive.ReadEntry(entry));
            var meshes = models.Select(model => MechWarriorModelMeshBuilder.Build(
                    model,
                    palette,
                    luminosityTable,
                    ObjectIlluminationLevel))
                .ToArray();
            return meshes;
        }).ToArray();
        var battlefieldActors = level.Actors
            .Select(actor => new BattlefieldActor(actor, explosionDebrisMeshes))
            .ToArray();
        var battlefieldEffectSounds = BattlefieldEffectSounds.Load(archive);
        var battlefieldEffects = new BattlefieldEffects(
            battlefieldEffectSounds.Explosions,
            authoredVaporTexture)
        {
            Name = "BattlefieldEffects"
        };
        AddChild(battlefieldEffects);
        m_battlefieldEffects = battlefieldEffects;
        var actorComponents = new Dictionary<
            (string SourcePath, int ObjectId),
            (BattlefieldActor Actor, bool Destroyed)>();
        foreach (var battlefieldActor in battlefieldActors)
        {
            actorRoot.AddChild(battlefieldActor);
            battlefieldActor.Destroyed += battlefieldEffects.SpawnDestruction;
            foreach (var component in battlefieldActor.Definition.Components)
            {
                actorComponents.Add(
                    (component.SourceEntry.Path, component.Id),
                    (battlefieldActor, false));
            }

            foreach (var component in battlefieldActor.Definition.DestroyedComponents)
            {
                actorComponents.Add(
                    (component.SourceEntry.Path, component.Id),
                    (battlefieldActor, true));
            }
        }
        var destructionLinks = MechWarriorActorDestructionLinkResolver.Resolve(level);
        var actorRoots = MechWarriorActorHierarchyResolver.ResolveRoots(level);
        var battlefieldActorsByDefinition = battlefieldActors.ToDictionary(actor => actor.Definition);
        var objectiveRootsByActor = battlefieldActors.ToDictionary(
            actor => actor,
            actor => battlefieldActorsByDefinition[actorRoots[actor.Definition]]);
        var linkedActors = destructionLinks
            .Select(link => (
                Parent: battlefieldActors.Single(actor => ReferenceEquals(actor.Definition, link.Parent)),
                Child: battlefieldActors.Single(actor => ReferenceEquals(actor.Definition, link.Child))))
            .GroupBy(link => link.Parent)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BattlefieldActor>)group.Select(link => link.Child).ToArray());
        if (linkedActors.Count > 0)
        {
            actorRoot.AddChild(new AuthoredActorDestructionController(linkedActors));
        }

        var meshCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var wireframeCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var modelCache = new Dictionary<string, IReadOnlyList<MechWarriorModel>>(StringComparer.OrdinalIgnoreCase);
        var groundPaletteWeights = new Dictionary<byte, double>();
        var debugTriangles = new List<DebugTriangle>();
        var worldBounds = new Aabb();
        var hasWorldBounds = false;
        var renderedInstanceCount = 0;
        var renderedActorComponentCount = 0;
        var renderedDebrisCount = 0;
        var settledActors = new HashSet<BattlefieldActor>();
        var sourceTerrainRoots = new List<Node3D>();
        var pendingActorSettlements = new List<(
            BattlefieldActor Actor,
            Node3D RootRepresentation,
            IReadOnlyList<MechWarriorModel> Models)>();
        var staticSceneryObstacles = new List<SceneryObstacle>();
        var staticObstacleIndicesByObject = new Dictionary<(string SourcePath, int ObjectId), int>();
        var collisionWallsByObject = new Dictionary<
            (string SourcePath, int ObjectId),
            IReadOnlyList<SceneryWallTriangle>>();
        var renderedBoundsByObject = new Dictionary<(string SourcePath, int ObjectId), Aabb>();
        var renderedRootsByObject = new Dictionary<(string SourcePath, int ObjectId), Node3D>();
        var authoredColorTasks = LoadAuthoredColorTasks(level.Sources, palette);
        var airborneSetPieceSources = level.Sources
            .Where(source => WorldHasTaskArgument(
                source.World,
                "recon"))
            .Select(source => source.Entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renderedObjects = level.StaticObjects
            .Concat(level.Actors.SelectMany(actor => actor.Components))
            .Concat(level.Actors.SelectMany(actor => actor.DestroyedComponents))
            .Where(levelObject => levelObject.Kind != MechWarriorLevelObjectKind.Effect);
        foreach (var levelObject in renderedObjects)
        {
            if (!meshCache.TryGetValue(levelObject.ModelEntry.Path, out var meshes))
            {
                if (levelObject.ModelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
                {
                    // DUMMY.WTB represents a BWD locator, often for a palette-cycled light.
                    // Its small payload is intentionally not a renderable WTB model.
                    meshes = Array.Empty<ArrayMesh>();
                    modelCache.Add(levelObject.ModelEntry.Path, Array.Empty<MechWarriorModel>());
                    wireframeCache.Add(levelObject.ModelEntry.Path, Array.Empty<ArrayMesh>());
                }
                else
                {
                    try
                    {
                        var models = MechWarriorModel.LoadAll(archive.ReadEntry(levelObject.ModelEntry));
                        var highestDetailIndex = Enumerable.Range(0, models.Count)
                            .MaxBy(index => models[index].Polygons.Count);
                        var highestDetailModels = new[] { models[highestDetailIndex] };
                        modelCache.Add(levelObject.ModelEntry.Path, highestDetailModels);
                        var illuminationLevel = levelObject.Kind == MechWarriorLevelObjectKind.Terrain
                            ? GeneralIlluminationLevel
                            : ObjectIlluminationLevel;
                        meshes = highestDetailModels
                            .Select(model => MechWarriorModelMeshBuilder.Build(
                                model,
                                palette,
                                luminosityTable,
                                illuminationLevel))
                            .ToArray();
                        if (levelObject.Kind != MechWarriorLevelObjectKind.Terrain)
                        {
                            foreach (var mesh in meshes)
                            {
                                MechWarriorModelMeshBuilder.ApplyStructureSurfaceFinish(mesh);
                            }
                        }
                        wireframeCache.Add(
                            levelObject.ModelEntry.Path,
                            highestDetailModels.Select(MechWarriorModelMeshBuilder.BuildWireframe).ToArray());
                    }
                    catch (InvalidDataException exception)
                    {
                        meshes = Array.Empty<ArrayMesh>();
                        modelCache.Add(levelObject.ModelEntry.Path, Array.Empty<MechWarriorModel>());
                        wireframeCache.Add(levelObject.ModelEntry.Path, Array.Empty<ArrayMesh>());
                        var objectPosition = levelObject.Transform.Translation;
                        GD.PushWarning(
                            $"MechRewired: skipped unsupported {levelObject.ModelEntry.Path} object {levelObject.Id} at " +
                            $"({objectPosition.X:F2}, {objectPosition.Y:F2}, {objectPosition.Z:F2}): {exception.Message}");
                    }
                }

                meshCache.Add(levelObject.ModelEntry.Path, meshes);
            }

            if (meshes.Count == 0)
            {
                if (authoredColorTasks.TryGetValue((levelObject.SourceEntry.Path, levelObject.Id), out var colors))
                {
                    var locatorPosition = MechWarriorCoordinateSystem.ToGodotPosition(levelObject.Transform.Translation);
                    if (levelObject.RelativeToId >= 0 &&
                        renderedBoundsByObject.TryGetValue(
                            (levelObject.SourceEntry.Path, levelObject.RelativeToId),
                            out var parentBounds))
                    {
                        // DUMMY locators carry the horizontal attachment point, but their vertical coordinate
                        // is not a physical mesh origin. Mount them on the authored parent assembly instead.
                        locatorPosition.Y = parentBounds.End.Y;
                    }

                    var locator = new Node3D
                    {
                        Name = $"{levelObject.ModelEntry.Name}LightLocator",
                        Position = locatorPosition
                    };
                    locator.AddChild(CreateAnimatedLocatorLight(colors));
                    if (TryFindOwningActor(
                            actorComponents,
                            levelObject,
                            out var locatorActor,
                            out var destroyedWithActor))
                    {
                        locatorActor.AddRepresentation(locator, destroyedWithActor);
                    }
                    else
                    {
                        levelRoot.AddChild(locator);
                    }
                }

                continue;
            }

            var position = MechWarriorCoordinateSystem.ToGodotPosition(levelObject.Transform.Translation);
            if (levelObject.Kind == MechWarriorLevelObjectKind.Debris)
            {
                var lowestVertex = meshes.Min(mesh => mesh.GetAabb().Position.Y);
                position.Y = DerivedTerrainSurfaceBuilder.ImplicitGroundHeight - lowestVertex;
            }

            var objectRoot = new Node3D
            {
                Name = levelObject.ModelEntry.Name,
                Position = position,
                RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(levelObject.Transform.RotationDegrees),
                Scale = MechWarriorCoordinateSystem.ToGodotScale(levelObject.Transform.Scale)
            };
            objectRoot.SetMeta("mechrewired_object_id", levelObject.Id);
            renderedRootsByObject[(levelObject.SourceEntry.Path, levelObject.Id)] = objectRoot;
            var isDestroyedRepresentation = false;
            BattlefieldActor battlefieldActor = null;
            if (levelObject.Kind == MechWarriorLevelObjectKind.Actor &&
                actorComponents.TryGetValue(
                    (levelObject.SourceEntry.Path, levelObject.Id),
                    out var actorComponent))
            {
                isDestroyedRepresentation = actorComponent.Destroyed;
                battlefieldActor = actorComponent.Actor;
                battlefieldActor.AddRepresentation(objectRoot, isDestroyedRepresentation);
                runtimeContent.AddActorRepresentation(battlefieldActor.Definition, isDestroyedRepresentation);
            }
            else
            {
                levelRoot.AddChild(objectRoot);
            }

            if (levelObject.Kind == MechWarriorLevelObjectKind.Terrain)
            {
                sourceTerrainRoots.Add(objectRoot);
                AccumulateGroundPaletteWeights(
                    groundPaletteWeights,
                    objectRoot.GlobalTransform,
                    modelCache[levelObject.ModelEntry.Path]);
            }

            if (battlefieldActor != null &&
                battlefieldActor.IsDamageable &&
                !isDestroyedRepresentation &&
                !airborneSetPieceSources.Contains(levelObject.SourceEntry.Path) &&
                settledActors.Add(battlefieldActor))
            {
                pendingActorSettlements.Add((
                    battlefieldActor,
                    objectRoot,
                    modelCache[levelObject.ModelEntry.Path]));
            }
            var wireframes = wireframeCache[levelObject.ModelEntry.Path];
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var solidInstance = new MeshInstance3D
                {
                    Mesh = meshes[meshIndex],
                    MaterialOverride = levelObject.Kind == MechWarriorLevelObjectKind.Terrain
                        ? terrainMaterial
                        : null,
                    // Terrain WTBs include hidden faces around/beneath their visible land.
                    // Casting those two-sided shadows blacks out the fallback plane below them.
                    CastShadow = levelObject.Kind == MechWarriorLevelObjectKind.Terrain
                        ? GeometryInstance3D.ShadowCastingSetting.On
                        : GeometryInstance3D.ShadowCastingSetting.DoubleSided,
                    // Authored terrain meshes are control geometry only. One welded derivative
                    // owns rendering for every biome, preventing transient overlap and z-fighting.
                    Visible = levelObject.Kind != MechWarriorLevelObjectKind.Terrain
                };
                objectRoot.AddChild(solidInstance);
                if (solidInstance.Visible)
                {
                    solidInstance.AddToGroup(DebugCamera.SolidMeshGroup);
                }
                if (solidInstance.Visible)
                {
                    var wireframeInstance = new MeshInstance3D
                    {
                        Mesh = wireframes[meshIndex],
                        Visible = false
                    };
                    // The solid is hidden in wireframe mode, so its diagnostic copy must be a sibling.
                    // objectRoot carries the shared authored transform for both instances.
                    objectRoot.AddChild(wireframeInstance);
                    wireframeInstance.AddToGroup(DebugCamera.WireframeMeshGroup);
                }
            }

            var collisionWalls = BuildSceneryWalls(
                objectRoot.GlobalTransform,
                modelCache[levelObject.ModelEntry.Path]);
            var objectBounds = meshes
                .Select(mesh => objectRoot.GlobalTransform * mesh.GetAabb())
                .Aggregate((combined, next) => combined.Merge(next));
            renderedBoundsByObject[(levelObject.SourceEntry.Path, levelObject.Id)] = objectBounds;
            collisionWallsByObject[(levelObject.SourceEntry.Path, levelObject.Id)] = collisionWalls;
            if (levelObject.Kind == MechWarriorLevelObjectKind.Scenery &&
                TryCreateSceneryObstacle(
                    $"{levelObject.ModelEntry.Name} object {levelObject.Id}",
                    collisionWalls,
                    out var sceneryObstacle))
            {
                staticSceneryObstacles.Add(sceneryObstacle);
                staticObstacleIndicesByObject[(levelObject.SourceEntry.Path, levelObject.Id)] =
                    staticSceneryObstacles.Count - 1;
            }

            if (!isDestroyedRepresentation)
            {
                AddDebugTriangles(
                    debugTriangles,
                    levelObject,
                    objectRoot.GlobalTransform,
                    modelCache[levelObject.ModelEntry.Path]);
            }

            if (!isDestroyedRepresentation)
            {
                renderedInstanceCount++;
                if (levelObject.Kind == MechWarriorLevelObjectKind.Actor)
                {
                    renderedActorComponentCount++;
                }
                else if (levelObject.Kind == MechWarriorLevelObjectKind.Debris)
                {
                    renderedDebrisCount++;
                }
            }

            var pointBounds = new Aabb(position, Vector3.Zero);
            worldBounds = hasWorldBounds ? worldBounds.Merge(pointBounds) : pointBounds;
            hasWorldBounds = true;
        }

        foreach (var battlefieldActor in battlefieldActors)
        {
            if (airborneSetPieceSources.Contains(battlefieldActor.Definition.SourceEntry.Path))
            {
                battlefieldActor.ConfigureSceneryObstacles(null, null);
                continue;
            }

            var activeWalls = battlefieldActor.Definition.Components
                .SelectMany(component => collisionWallsByObject.GetValueOrDefault(
                    (component.SourceEntry.Path, component.Id),
                    Array.Empty<SceneryWallTriangle>()))
                .ToArray();
            var destroyedWalls = battlefieldActor.Definition.DestroyedComponents
                .SelectMany(component => collisionWallsByObject.GetValueOrDefault(
                    (component.SourceEntry.Path, component.Id),
                    Array.Empty<SceneryWallTriangle>()))
                .ToArray();
            TryCreateSceneryObstacle(
                $"{battlefieldActor.Description} object {battlefieldActor.Definition.ObjectId}",
                activeWalls,
                out var activeObstacle);
            TryCreateSceneryObstacle(
                $"{battlefieldActor.Description} wreckage {battlefieldActor.Definition.ObjectId}",
                destroyedWalls,
                out var destroyedObstacle);
            battlefieldActor.ConfigureSceneryObstacles(activeObstacle, destroyedObstacle);
        }

        GD.Print(
            $"MechRewired: rendered mission world ({renderedInstanceCount} instances, " +
            $"{renderedActorComponentCount} active actor components, {renderedDebrisCount} ground-settled debris objects, " +
            $"{meshCache.Count} unique models; luminosity levels {GeneralIlluminationLevel} terrain / " +
            $"{ObjectIlluminationLevel} objects).");
        IReadOnlyList<DebugTriangle> groundCoverageTriangles;
        if (usesDesertTerrain)
        {
            var derivedTerrain = AddDerivedTerrain(
                levelRoot,
                debugTriangles,
                terrainMaterial,
                terrainWireframeMaterial,
                terrainDiagnostics,
                useMacroRelief: true,
                groundReliefKind: TerrainGroundReliefKind.Desert,
                snapLowExteriorVertices: true,
                sealToImplicitGround: true);
            groundCoverageTriangles = derivedTerrain.CollisionTriangles;
        }
        else
        {
            var rockyGroundColor = CalculateRepresentativeGroundColor(
                groundPaletteWeights,
                palette,
                sourcePaletteIndex => sourcePaletteIndex,
                "raw visible rocky-plains terrain",
                out _);
            var derivedTerrain = AddDerivedTerrain(
                levelRoot,
                debugTriangles,
                TerrainSurfaceMaterial.Create(
                    TerrainSurfaceKind.RockyMountain,
                    rockyGroundColor),
                TerrainSurfaceMaterial.CreateWireframe(
                    TerrainSurfaceKind.RockyMountain,
                    rockyGroundColor),
                terrainDiagnostics,
                useMacroRelief: false,
                groundReliefKind: TerrainGroundReliefKind.Rocky,
                snapLowExteriorVertices: false,
                sealToImplicitGround: true);
            groundCoverageTriangles = derivedTerrain.CollisionTriangles;
        }

        foreach (var sourceTerrainRoot in sourceTerrainRoots)
        {
            sourceTerrainRoot.QueueFree();
        }

        var renderedGroundDebugTriangles = AddImplicitGround(
            levelRoot,
            worldBounds,
            groundPaletteWeights,
            palette,
            luminosityTable,
            debugTriangles,
            groundCoverageTriangles,
            terrainDiagnostics,
            terrainBiome);
        var terrainSurface = new TerrainSurfaceIndex(debugTriangles);
        runtimeContent.ReportInfo(
            MissionFidelityFindingKind.ProceduralFallback,
            missionResources.Level.Entry.Path,
            "derived terrain",
            "The original terrain control meshes are supplemented by the derived terrain surface and implicit ground.");
        GD.Print($"MechRewired: prepared {terrainBiome.ToString().ToLowerInvariant()} terrain surface.");
        var terrainRocks = TerrainRockScatter.Create(
            terrainSurface,
            GetTerrainBounds(debugTriangles),
            terrainBiome);
        levelRoot.AddChild(terrainRocks);
        foreach (var (actor, rootRepresentation, models) in pendingActorSettlements)
        {
            SettleActorOnTerrain(actor, rootRepresentation, models, terrainSurface, debugTriangles);
        }
        BattlefieldPhysics.AddTerrainCollision(levelRoot, debugTriangles);
        battlefieldEffects.ConfigureTerrain(terrainSurface);
        foreach (var battlefieldActor in battlefieldActors)
        {
            battlefieldActor.ConfigureTerrain(terrainSurface);
        }
        var hostileAircraft = LoadAuthoredAircraft(
            archive,
            level,
            battlefieldActors,
            renderedRootsByObject,
            debugTriangles,
            battlefieldEffects,
            runtimeContent);
        LoadAuthoredWorldPaths(
            archive,
            level,
            levelRoot,
            battlefieldActors,
            actorComponents,
            renderedRootsByObject,
            debugTriangles,
            staticSceneryObstacles,
            staticObstacleIndicesByObject,
            collisionWallsByObject,
            runtimeContent);
        var instantiatedEffects = LoadAmbientEffects(
            archive,
            level,
            battlefieldEffects,
            actorComponents,
            battlefieldEffectSounds.AmbientFire,
            runtimeContent);
        foreach (var effect in instantiatedEffects)
        {
            runtimeContent.AddEffect(effect);
        }
        var playerRotation = MechWarriorCoordinateSystem.ToGodotRotation(
            new System.Numerics.Vector3(0.0f, playerStart.StartingAngle, 0.0f));
        var playerBasis = Basis.FromEuler(playerRotation * (Mathf.Pi / 180.0f));
        var deploymentDirection = (-playerBasis.Z).Normalized();
        var deploymentLeft = -playerBasis.X.Normalized();
        var playerDeploymentPosition = MechWarriorCoordinateSystem.ToGodotPosition(playerStart.Position);
        var deploymentAnchor = playerDeploymentPosition +
                               deploymentDirection * 55.0f +
                               deploymentLeft * 40.0f;
        deploymentAnchor.Y = FindDeploymentSurfaceHeight(terrainSurface, deploymentAnchor);
        var dropShipDepartureDirection = playerDeploymentPosition - deploymentAnchor;
        dropShipDepartureDirection.Y = 0.0f;
        dropShipDepartureDirection = dropShipDepartureDirection.Normalized();
        var extractionResourceName = missionDefinition.Objectives
            .LastOrDefault(objective => objective.Kind == MissionObjectiveKind.Extract)
            ?.TargetResourceName;
        var extractionPoint = navigationPoints.FirstOrDefault(point => string.Equals(
                                  point.ResourceName,
                                  extractionResourceName,
                                  StringComparison.OrdinalIgnoreCase)) ??
                              navigationPoints.LastOrDefault();
        var extractionPosition = extractionPoint != null
            ? MechWarriorCoordinateSystem.ToGodotPosition(extractionPoint.Point.Position)
            : deploymentAnchor;
        var extractionApproach = navigationPoints.Count > 1
            ? (extractionPosition -
               MechWarriorCoordinateSystem.ToGodotPosition(navigationPoints[^2].Point.Position))
            : deploymentDirection;
        extractionApproach.Y = 0.0f;
        extractionApproach = extractionApproach.Normalized();
        var extractionLeft = -extractionApproach.Cross(Vector3.Up).Normalized();
        var extractionAnchor = extractionPosition + extractionApproach * 55.0f + extractionLeft * 40.0f;
        extractionAnchor.Y = FindDeploymentSurfaceHeight(terrainSurface, extractionAnchor);
        var missionDropShips = LoadMissionDropShips(
            archive,
            missionResources.Level.Entry.Path,
            levelRoot,
            battlefieldEffects,
            palette,
            luminosityTable,
            deploymentAnchor,
            extractionAnchor,
            dropShipDepartureDirection,
            runtimeContent);

        var playerMechSounds = PlayerMechSounds.Load(archive, missionResources.MissionPrefix);
        GD.Print("MechRewired: loaded player and mission audio.");
        var playerMech = new PlayerMech(
            playerMechDefinition,
            playerMechSounds);
        AddChild(playerMech);

        var bounds = new Aabb();
        var hasBounds = false;
        var triangleCount = 0;
        var vertexCount = 0;
        var renderedPartCount = 0;
        var playerObjectsById = playerChassis.Objects.ToDictionary(chassisObject => chassisObject.Id);
        var playerTorsoObjectId = playerChassis.ThingObjectIds
            .FirstOrDefault(id => playerObjectsById.ContainsKey(id));
        var playerTorsoPivot = playerTorsoObjectId != 0
            ? MechWarriorCoordinateSystem.ToGodotPosition(
                playerObjectsById[playerTorsoObjectId].Transform.Translation)
            : Vector3.Zero;
        playerMech.Torso.Position = playerTorsoPivot;
        var playerPartRoots = BuildChassisObjectHierarchy(
            playerChassis,
            playerMech.Legs,
            playerMech.Torso,
            playerTorsoObjectId);
        foreach (var chassisObject in playerChassis.Objects.Where(chassisObject =>
                     chassisObject.ModelResourceIndex >= 0))
        {
            var modelEntry = archive.GetEntry("POLY", chassisObject.ModelResourceIndex);
            if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var model = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry))
                .MaxBy(candidate => candidate.Polygons.Count) ??
                throw new InvalidDataException($"{modelEntry.Path} contains no mech model.");
            var isDecalModel = modelEntry.Name.Contains("DEC", StringComparison.OrdinalIgnoreCase);
            LoadMaterialImages(
                archive,
                materialMapEntry,
                materialMap,
                playerMaterialImages,
                model.Polygons.Select(polygon => polygon.MaterialIndex),
                playerUsesJadeFalconDecals,
                isDecalModel,
                runtimeContent,
                modelEntry.Path);
            var partRoot = playerPartRoots[chassisObject.Id];
            var renderMesh = MechWarriorModelMeshBuilder.Build(
                model,
                palette,
                luminosityTable,
                GeneralIlluminationLevel,
                playerMaterialImages,
                preserveTexturePalette: isDecalModel);
            if (isDecalModel)
            {
                MechWarriorModelMeshBuilder.ApplyMechDecalFinish(renderMesh);
            }
            else
            {
                MechWarriorModelMeshBuilder.ApplyMechSurfaceFinish(renderMesh);
            }
            var modelInstance = new MeshInstance3D
            {
                Name = modelEntry.Name,
                Mesh = renderMesh,
                Layers = PlayerMech.ExteriorRenderLayer,
                CastShadow = isDecalModel
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.DoubleSided
            };
            partRoot.AddChild(modelInstance);
            playerMech.RegisterGaitPart(partRoot, modelEntry.Name);
            playerMech.RegisterGaitFootMesh(modelInstance, modelEntry.Name);
            playerMech.RegisterDestructiblePart(modelInstance, modelEntry.Name);
            modelInstance.AddToGroup(DebugCamera.SolidMeshGroup);

            var wireframeInstance = new MeshInstance3D
            {
                Name = $"{modelEntry.Name}Wireframe",
                Mesh = MechWarriorModelMeshBuilder.BuildWireframe(model),
                Visible = false,
                Layers = PlayerMech.ExteriorRenderLayer
            };
            partRoot.AddChild(wireframeInstance);
            wireframeInstance.AddToGroup(DebugCamera.WireframeMeshGroup);

            var absoluteTransform = BuildMechTransform(chassisObject.Transform);
            var partBounds = absoluteTransform * renderMesh.GetAabb();
            bounds = hasBounds ? bounds.Merge(partBounds) : partBounds;
            hasBounds = true;
            renderedPartCount++;
            vertexCount += model.Vertices.Count;
            triangleCount += model.Polygons.Sum(polygon => polygon.VertexIndices.Count - 2);
        }

        if (!hasBounds)
        {
            throw new InvalidDataException($"The {playerChassisName} chassis contains no supported renderable mech parts.");
        }

        var deploymentPosition = MechWarriorCoordinateSystem.ToGodotPosition(playerStart.Position);
        var surfaceHeight = FindDeploymentSurfaceHeight(terrainSurface, deploymentPosition);
        playerMech.Position = new Vector3(
            deploymentPosition.X,
            surfaceHeight - bounds.Position.Y,
            deploymentPosition.Z);
        playerMech.RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(
            new System.Numerics.Vector3(0.0f, playerStart.StartingAngle, 0.0f));
        playerMech.Configure(
            bounds,
            playerTorsoPivot,
            BuildWeaponMounts(playerChassis, playerObjectsById, playerTorsoObjectId),
            terrainSurface,
            () => GetSceneryObstacles(staticSceneryObstacles, battlefieldActors));
        terrainRocks?.ConfigureObserver(playerMech);
#if DEBUG
        RegisterDebugConsoleCockpit(playerMech.Cockpit);
        RegisterDebugConsoleSky(
            missionSky,
            playerMech.CockpitCamera,
            Path.GetFileNameWithoutExtension(missionResources.ScenarioEntry.Name));
        RegisterDebugConsoleTerrain(terrainDiagnostics);
        RegisterDebugConsoleAmbientEffects(battlefieldEffects);
#endif
        var playerMission = new PlayerMission(archive, missionDefinition);
        AddChild(playerMission);
        var playerDeathSequence = new PlayerDeathSequence(
            playerMech,
            battlefieldEffects,
            playerMechSounds.DeathExplosion,
            playerMission.Fail);
        AddChild(playerDeathSequence);
        battlefieldEffects.ConfigureObserver(playerMech);
        if (missionSky.EnableLocalizedVolumetricFog(usesDesertTerrain ? 160.0f : 280.0f))
        {
            if (usesDesertTerrain)
            {
                var groundSand = new GroundSandFog(playerMech, terrainSurface)
                {
                    Name = "GroundSandFog"
                };
                levelRoot.AddChild(groundSand);
                GD.Print("MechRewired: enabled localized ground sand.");
#if DEBUG
                RegisterDebugConsoleGroundSand(groundSand);
#endif
            }
            else
            {
                var mountainHaze = new GroundMountainHaze(
                    playerMech,
                    terrainSurface,
                    skyProfile.HorizonColor)
                {
                    Name = "GroundMountainHaze"
                };
                levelRoot.AddChild(mountainHaze);
                GD.Print("MechRewired: enabled localized mountain-valley haze.");
            }
        }
        if (usesDesertTerrain)
        {
            playerMech.FootfallLanded += battlefieldEffects.SpawnFootfallDust;
        }
        foreach (var battlefieldActor in battlefieldActors)
        {
            battlefieldActor.ConfigureEffectPersistence(playerMech);
        }

        var enemyMechs = LoadEnemyMechs(
            archive,
            palette,
            luminosityTable,
            materialMapEntry,
            materialMap,
            materialImages,
            missionGamePieces,
            !playerUsesJadeFalconDecals,
            playerMech,
            playerMechSounds.WeaponFireSounds,
            battlefieldEffects,
            atmosphericVisibilityRange,
            () => GetSceneryObstacles(staticSceneryObstacles, battlefieldActors),
            terrainSurface,
            debugTriangles.AsReadOnly(),
            runtimeContent);
        GD.Print(
            $"MechRewired: configured {staticSceneryObstacles.Count} static and " +
            $"{battlefieldActors.Length} actor scenery obstacles.");
        var playerNavigation = new PlayerNavigation(
            playerMech,
            navigationPoints,
            missionAreaBoundaries,
            playerMechSounds.NavigationPointTone);
        AddChild(playerNavigation);
        foreach (var navigationPoint in navigationPoints)
        {
            runtimeContent.AddNavigationPoint(navigationPoint);
        }
        foreach (var objective in missionDefinition.Objectives)
        {
            runtimeContent.AddObjective(objective);
        }
        playerNavigation.NavigationPointReached += index => playerMission.Apply(new MissionEvent(
            MissionEventKind.NavigationPointReached,
            navigationPoints[index].ResourceName));
        var playerAutopilot = new PlayerAutopilot(
            playerMech,
            playerNavigation,
            playerMechSounds.Autopilot,
            playerMechSounds.AutopilotEnabled,
            playerMechSounds.AutopilotDisabled);
        AddChild(playerAutopilot);
        var playerTargeting = new PlayerTargeting(
            playerMech,
            playerMission,
            debugTriangles.AsReadOnly(),
            battlefieldActors,
            hostileAircraft,
            enemyMechs,
            objectiveRootsByActor,
            playerMechDefinition,
            playerMechSounds,
            battlefieldEffects);
        AddChild(playerTargeting);
        playerNavigation.MissionAreaBoundaryExited += boundary =>
        {
            var previousOutcome = playerMission.Outcome;
            playerMission.Apply(new MissionEvent(
                MissionEventKind.MissionAreaBoundaryExited,
                boundary.ResourceName));
            if (previousOutcome == MissionOutcome.Active &&
                playerMission.Outcome == MissionOutcome.Failed)
            {
                playerTargeting.InitiateShutdown("mission area exited");
            }
        };
        ReportMissionFidelityAudit(
            missionResources,
            level,
            navigationPoints,
            missionDefinition,
            missionGamePieces,
            runtimeContent);
#if DEBUG
        RegisterDebugConsoleTargeting(playerTargeting);
#endif

        var hudLayer = new CanvasLayer
        {
            Name = "PlayerHudLayer",
            Layer = 10
        };
        AddChild(hudLayer);
        var playerDamageSilhouette = LoadDamageSilhouette(
            archive,
            playerChassisName,
            playerChassis);
        var playerHud = new PlayerHud(
            playerMech,
            playerDamageSilhouette,
            playerNavigation,
            playerTargeting,
            playerMission)
        {
            Name = "PlayerHud",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        hudLayer.AddChild(playerHud);
        playerHud.BeginPowerUp();
#if DEBUG
        RegisterDebugConsoleHud(playerHud);
#endif
        var missionDebrief = new MissionDebrief(playerMission);
        AddChild(missionDebrief);
        // A failure is presented only after the external death camera has concluded.
        // PlayerDeathSequence no longer reloads the scene underneath the debrief.
        playerDeathSequence.Completed += () => missionDebrief.Present(MissionOutcome.Failed);
        playerMission.MissionResolved += outcome =>
        {
            if (outcome == MissionOutcome.Failed && !playerDeathSequence.IsActive)
            {
                missionDebrief.Present(outcome);
            }
        };
        playerMission.MissionCompleted += () =>
        {
            playerMech.LockMovementForExtraction();
            if (missionDropShips.Count == 0)
            {
                missionDebrief.Present(MissionOutcome.Successful);
                return;
            }

            var landedDropShips = 0;
            foreach (var dropShip in missionDropShips)
            {
                dropShip.ExtractionLanded += () =>
                {
                    landedDropShips++;
                    if (landedDropShips == missionDropShips.Count)
                    {
                        missionDebrief.Present(MissionOutcome.Successful);
                    }
                };
                dropShip.BeginExtraction();
            }
        };

        GD.Print($"MechRewired: deployed player {playerChassisName} ({renderedPartCount} rendered parts).");

        var target = playerMech.ToGlobal(bounds.GetCenter());
        var modelSize = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        var cameraDistance = Math.Max(modelSize * 3.0f, 1.0f);
        var cameraDirection = new Vector3(0.75f, 0.4f, 1.0f).Normalized();
        var visualSceneTriangles = debugTriangles
            .Where(triangle => triangle.ResourcePath != "IMPLICIT/GROUND")
            .Concat(renderedGroundDebugTriangles)
            .ToArray();
        var camera = new DebugCamera
        {
            Position = target + cameraDirection * cameraDistance,
            Current = false,
            Far = Math.Max(cameraDistance * 4.0f, 8000.0f),
            CullMask = 1u | PlayerMech.ExteriorRenderLayer,
            SceneTriangles = visualSceneTriangles,
            CockpitCamera = playerMech.CockpitCamera,
            ExternalCamera = playerMech.ExternalCamera,
            PlayerMech = playerMech,
            PlayerTargeting = playerTargeting
        };
        camera.LookAtFromPosition(camera.Position, target);
#if DEBUG
        camera.DestroyHostilesRequested += () =>
        {
            var hostiles = enemyMechs.Where(enemy => !enemy.IsDestroyed).ToArray();
            foreach (var hostile in hostiles)
            {
                hostile.DebugDestroy();
            }

            GD.Print($"MechRewired: DEBUG destroyed {hostiles.Length} active hostile mech(s).");
        };
#endif
        AddChild(camera);
    }

#if DEBUG
    private void RegisterDebugConsoleAmbientEffects(BattlefieldEffects battlefieldEffects)
    {
        ArgumentNullException.ThrowIfNull(battlefieldEffects);
        m_debugConsole?.Call(
            "add_command",
            "vfx.capture_ambient",
            Callable.From(() => CaptureAmbientEffects(battlefieldEffects)),
            0,
            0,
            "Frames and captures the densest cluster of authored ambient effects.");

        if (OS.GetCmdlineUserArgs().Contains("--capture-ambient-effects"))
        {
            CaptureAmbientEffects(battlefieldEffects, quitAfterCapture: true);
        }
    }

    private void CaptureAmbientEffects(
        BattlefieldEffects battlefieldEffects,
        bool quitAfterCapture = false)
    {
        if (m_debugVisualCapture == null || !battlefieldEffects.TryGetAmbientCaptureBounds(out var bounds))
        {
            GD.PushWarning("MechRewired: no authored ambient effects are available to capture.");
            if (quitAfterCapture)
            {
                GetTree().Quit();
            }
            return;
        }

        m_debugVisualCapture.CaptureAmbientEffectsFixture(bounds, quitAfterCapture);
    }
#endif

    private static void ReportMissionFidelityAudit(
        MechWarriorMissionResources resources,
        MechWarriorLevel level,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        MissionDefinition definition,
        IReadOnlyList<MechWarriorMissionGamePiece> gamePieces,
        MissionRuntimeContent runtimeContent)
    {
        var audit = MissionFidelityAudit.Analyze(
            resources, level, navigationPoints, definition, gamePieces, runtimeContent);
        if (audit.WarningCount == 0)
        {
            GD.Print(
                $"MechRewired: mission fidelity audit passed for {resources.ScenarioEntry.Path} " +
                $"({audit.Findings.Count} informational structural record(s)).");
            return;
        }

        var groupedCounts = audit.Findings
            .Where(finding => finding.Severity == MissionFidelitySeverity.Warning)
            .GroupBy(finding => finding.Kind)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}");
        GD.Print(
            $"WARNING: MechRewired: mission fidelity audit found {audit.WarningCount} warning(s) for " +
            $"{resources.ScenarioEntry.Path} ({string.Join(", ", groupedCounts)}).");
        foreach (var group in audit.Findings
                     .Where(finding => finding.Severity == MissionFidelitySeverity.Warning)
                     .GroupBy(finding => finding.Kind)
                     .OrderBy(group => group.Key))
        {
            var finding = group.First();
            GD.Print(
                $"WARNING: MechRewired: audit {finding.Kind} ({group.Count()}): {finding.SourceResource} " +
                $"{finding.Identity}: {finding.Reason}");
        }
    }

    private IReadOnlyList<EnemyMech> LoadEnemyMechs(
        MechWarriorProjectArchive archive,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        MechWarriorProjectEntry materialMapEntry,
        MechWarriorMaterialMap materialMap,
        Dictionary<byte, MechWarriorIndexedImage> materialImages,
        IReadOnlyList<MechWarriorMissionGamePiece> missionGamePieces,
        bool useJadeFalconDecals,
        PlayerMech playerMech,
        IReadOnlyDictionary<string, AudioStreamWav> weaponSounds,
        BattlefieldEffects battlefieldEffects,
        float atmosphericVisibilityRange,
        Func<IReadOnlyList<SceneryObstacle>> sceneryObstacleProvider,
        TerrainSurfaceIndex terrainSurface,
        IReadOnlyList<DebugTriangle> debugTriangles,
        MissionRuntimeContent runtimeContent)
    {
        var enemyRoot = new Node3D { Name = "EnemyMechs" };
        AddChild(enemyRoot);
        var enemies = new List<EnemyMech>();
        var damageSilhouettes = new Dictionary<string, MechDamageSilhouette>(StringComparer.OrdinalIgnoreCase);
        var chassisWithoutDamageSilhouettes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gamePiece in missionGamePieces.Where(gamePiece =>
                     gamePiece.Star.Disposition == MechWarriorMissionDisposition.Hostile))
        {
            var chassis = MechWarriorMechChassis.Load(archive.ReadEntry(gamePiece.ChassisEntry));
            var mechDefinition = MechWarriorMechFile.Load(archive.ReadEntry(gamePiece.ConfigurationEntry));
            MechDamageSilhouette damageSilhouette = null;
            var normalizedChassisName = gamePiece.Specification.ChassisName.Replace(" ", string.Empty);
            if (DamageShapePrefixes.ContainsKey(normalizedChassisName) &&
                !damageSilhouettes.TryGetValue(gamePiece.Specification.ChassisName, out damageSilhouette))
            {
                damageSilhouette = LoadDamageSilhouette(
                    archive,
                    gamePiece.Specification.ChassisName,
                    chassis);
                damageSilhouettes.Add(gamePiece.Specification.ChassisName, damageSilhouette);
            }
            else if (!DamageShapePrefixes.ContainsKey(normalizedChassisName) &&
                     chassisWithoutDamageSilhouettes.Add(gamePiece.Specification.ChassisName))
            {
                GD.Print(
                    $"MechRewired: {gamePiece.Specification.ChassisName} has no original HUD damage silhouette; " +
                    "using the hostile emplacement target display.");
            }

            var enemy = new EnemyMech(
                gamePiece,
                mechDefinition,
                playerMech,
                battlefieldEffects,
                weaponSounds,
                damageSilhouette,
                atmosphericVisibilityRange,
                position => FindDeploymentSurfaceHeight(terrainSurface, position),
                sceneryObstacleProvider,
                debugTriangles);
            enemyRoot.AddChild(enemy);
            runtimeContent.AddCombatant(gamePiece);

            var objectsById = chassis.Objects.ToDictionary(mechObject => mechObject.Id);
            var torsoObjectId = chassis.ThingObjectIds.FirstOrDefault(id => objectsById.ContainsKey(id));
            var torsoPivot = torsoObjectId != 0
                ? MechWarriorCoordinateSystem.ToGodotPosition(objectsById[torsoObjectId].Transform.Translation)
                : Vector3.Zero;
            var partRoots = BuildChassisObjectHierarchy(
                chassis,
                enemy.Legs,
                enemy.Torso,
                torsoObjectId);
            if (enemy.IsStationaryEmplacement &&
                chassis.ThingObjectIds.Count > 1 &&
                partRoots.TryGetValue(chassis.ThingObjectIds[1], out var pitchPivot))
            {
                enemy.ConfigureAimPitchPivot(pitchPivot);
            }
            var bounds = new Aabb();
            var hasBounds = false;
            var renderedParts = 0;
            var animatedGaitParts = 0;
            var renderedModelNames = new List<string>();
            var renderedPolygons = 0;
            foreach (var chassisObject in chassis.Objects.Where(chassisObject => chassisObject.ModelResourceIndex >= 0))
            {
                var modelEntry = archive.GetEntry("POLY", chassisObject.ModelResourceIndex);
                renderedModelNames.Add(modelEntry.Name);
                if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var models = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry));
                var model = models.MaxBy(candidate => candidate.Polygons.Count) ??
                            throw new InvalidDataException($"{modelEntry.Path} contains no mech model.");
                var isDecalModel = modelEntry.Name.Contains("DEC", StringComparison.OrdinalIgnoreCase);
                LoadMaterialImages(
                    archive,
                    materialMapEntry,
                    materialMap,
                    materialImages,
                    model.Polygons.Select(polygon => polygon.MaterialIndex),
                    useJadeFalconDecals,
                    isDecalModel,
                    runtimeContent,
                    modelEntry.Path);
                var mesh = MechWarriorModelMeshBuilder.Build(
                    model,
                    palette,
                    luminosityTable,
                    GeneralIlluminationLevel,
                    materialImages,
                    preserveTexturePalette: isDecalModel);
                if (isDecalModel)
                {
                    MechWarriorModelMeshBuilder.ApplyMechDecalFinish(mesh);
                }
                else
                {
                    MechWarriorModelMeshBuilder.ApplyMechSurfaceFinish(mesh);
                }
                var partRoot = partRoots[chassisObject.Id];
                var modelInstance = new MeshInstance3D
                {
                    Name = modelEntry.Name,
                    Mesh = mesh,
                    CastShadow = isDecalModel
                        ? GeometryInstance3D.ShadowCastingSetting.Off
                        : GeometryInstance3D.ShadowCastingSetting.DoubleSided
                };
                partRoot.AddChild(modelInstance);
                if (enemy.RegisterGaitPart(partRoot, modelEntry.Name))
                {
                    animatedGaitParts++;
                }
                enemy.RegisterDestructiblePart(modelInstance, modelEntry.Name);
                modelInstance.AddToGroup(DebugCamera.SolidMeshGroup);

                var wireframe = new MeshInstance3D
                {
                    Name = $"{modelEntry.Name}Wireframe",
                    Mesh = MechWarriorModelMeshBuilder.BuildWireframe(model),
                    Visible = false
                };
                partRoot.AddChild(wireframe);
                wireframe.AddToGroup(DebugCamera.WireframeMeshGroup);

                var absoluteTransform = BuildMechTransform(chassisObject.Transform);
                var partBounds = absoluteTransform * mesh.GetAabb();
                bounds = hasBounds ? bounds.Merge(partBounds) : partBounds;
                hasBounds = true;
                renderedParts++;
                renderedPolygons += model.Polygons.Count;
            }

            if (!hasBounds)
            {
                throw new InvalidDataException(
                    $"{gamePiece.ChassisEntry.Path} contains no supported renderable mech parts.");
            }

            var configuredWeaponSections = mechDefinition.Weapons
                .Select(weapon => weapon.Section)
                .ToHashSet();
            var authoredWeaponMounts = BuildWeaponMounts(chassis, objectsById, torsoObjectId);
            var weaponMounts = authoredWeaponMounts
                .Where(mount => configuredWeaponSections.Contains(mount.Section))
                .ToArray();
            if (weaponMounts.Length == 0)
            {
                weaponMounts = authoredWeaponMounts
                    .Where(mount => mount.Section is MechDamageSection.LeftArm or MechDamageSection.RightArm)
                    .ToArray();
            }
            enemy.ConfigureVisuals(bounds, torsoPivot, weaponMounts);
            var spawnPosition = MechWarriorCoordinateSystem.ToGodotPosition(gamePiece.SpawnPoint.Position);
            spawnPosition.Y = FindDeploymentSurfaceHeight(terrainSurface, spawnPosition) - bounds.Position.Y;
            enemy.Position = spawnPosition;
            enemy.RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(
                new System.Numerics.Vector3(0.0f, gamePiece.SpawnPoint.StartingAngle, 0.0f));
            enemies.Add(enemy);
            if (animatedGaitParts == 0 && !enemy.IsStationaryEmplacement)
            {
                GD.PushWarning(
                    $"MechRewired: {enemy.Description} has no recognized gait parts among " +
                    $"[{string.Join(", ", renderedModelNames)}].");
            }

        }

        var emplacementCount = enemies.Count(enemy => enemy.IsStationaryEmplacement);
        GD.Print(
            $"MechRewired: deployed hostile force ({enemies.Count - emplacementCount} mechs, " +
            $"{emplacementCount} emplacements).");
        return enemies.AsReadOnly();
    }

    private static MechDamageSilhouette LoadDamageSilhouette(
        MechWarriorProjectArchive archive,
        string chassisName,
        MechWarriorMechChassis chassis)
    {
        if (!DamageShapePrefixes.TryGetValue(chassisName.Replace(" ", string.Empty), out var prefix))
        {
            throw new InvalidDataException(
                $"No original damage silhouette is mapped for chassis '{chassisName}'.");
        }

        var entry = archive.GetEntry($"SHP/{prefix}DMG6.SHP");
        var shape = MechWarriorShapeImage.Load(archive.ReadEntry(entry));
        var silhouette = MechDamageSilhouetteBuilder.Build(archive, shape, chassis);
        return silhouette;
    }

    private static void LoadMaterialImages(
        MechWarriorProjectArchive archive,
        MechWarriorProjectEntry materialMapEntry,
        MechWarriorMaterialMap materialMap,
        Dictionary<byte, MechWarriorIndexedImage> materialImages,
        IEnumerable<byte> materialIndices,
        bool useJadeFalconDecal = false,
        bool isDecalModel = false,
        MissionRuntimeContent runtimeContent = null,
        string modelPath = null)
    {
        foreach (var materialIndex in materialIndices.Distinct())
        {
            var textureMaterialIndex = ResolveMechTextureMaterialIndex(
                materialIndex,
                useJadeFalconDecal);
            var isLargeClanInsigniaMaterial = materialIndex is
                WolfLargeInsigniaMaterialIndex or JadeFalconLargeInsigniaMaterialIndex;
            var usesLargeClanInsignia = isDecalModel && isLargeClanInsigniaMaterial;
            var isSupportedTextureSlot =
                textureMaterialIndex <= MaximumTexturedMechMaterialIndex || usesLargeClanInsignia;
            if (!isSupportedTextureSlot || isLargeClanInsigniaMaterial && !isDecalModel ||
                materialImages.ContainsKey(materialIndex))
            {
                continue;
            }
            if (!materialMap.Images.TryGetValue(textureMaterialIndex, out var materialImage))
            {
                runtimeContent?.Report(
                    MissionFidelityFindingKind.MissingMaterialMapping,
                    modelPath ?? materialMapEntry.Path,
                    $"material {materialIndex}",
                    $"No {materialMapEntry.Path} mapping exists for resolved material {textureMaterialIndex}.");
                continue;
            }

            var imageEntry = archive.GetEntry("CEL", materialImage.ImageResourceIndex);
            materialImages.Add(materialIndex, MechWarriorIndexedImage.Load(archive.ReadEntry(imageEntry)));
        }
    }

    private static Texture2D LoadOriginalIndexedTexture(
        MechWarriorProjectArchive archive,
        MechWarriorMaterialMap materialMap,
        MechWarriorPalette palette,
        byte materialIndex)
    {
        if (!materialMap.Images.TryGetValue(materialIndex, out var materialImage))
        {
            GD.PushWarning(
                $"MechRewired: material {materialIndex} has no original image; using the procedural VFX fallback.");
            return null;
        }

        var entry = archive.GetEntry("CEL", materialImage.ImageResourceIndex);
        var source = MechWarriorIndexedImage.Load(archive.ReadEntry(entry));
        var pixels = new byte[source.Width * source.Height * 4];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                // XEL rows are stored bottom-to-top, and palette index 255 is transparent.
                var paletteIndex = source.GetPixel(x, source.Height - y - 1);
                if (paletteIndex == byte.MaxValue)
                {
                    continue;
                }

                var destinationOffset = (y * source.Width + x) * 4;
                var color = palette[paletteIndex];
                pixels[destinationOffset] = color.R;
                pixels[destinationOffset + 1] = color.G;
                pixels[destinationOffset + 2] = color.B;
                pixels[destinationOffset + 3] = byte.MaxValue;
            }
        }

        var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
        GD.Print(
            $"MechRewired: loaded original material {materialIndex} artwork {entry.Path} " +
            $"({source.Width}x{source.Height}).");
        return ImageTexture.CreateFromImage(image);
    }

    // Timber Wolf arm barrels use 0x70 for camouflaged housing sides; their separate end caps retain
    // material 15 (V1DGNHOL), which supplies the original twin gun openings.
    private static byte ResolveMechTextureMaterialIndex(
        byte materialIndex,
        bool useJadeFalconDecal)
    {
        if (materialIndex == FlaggedCamoMechMaterialIndex)
        {
            return CamoMechMaterialIndex;
        }

        // MW2_MAP1 provides small and large clan-insignia pairs. Chassis WTBs consistently author
        // the small Wolf slot and large Jade Falcon slot, so resolve both pairs for the active side.
        return materialIndex switch
        {
            WolfSmallInsigniaMaterialIndex when useJadeFalconDecal => JadeFalconSmallInsigniaMaterialIndex,
            JadeFalconSmallInsigniaMaterialIndex when !useJadeFalconDecal => WolfSmallInsigniaMaterialIndex,
            WolfLargeInsigniaMaterialIndex when useJadeFalconDecal => JadeFalconLargeInsigniaMaterialIndex,
            JadeFalconLargeInsigniaMaterialIndex when !useJadeFalconDecal => WolfLargeInsigniaMaterialIndex,
            _ => materialIndex
        };
    }

    private static IReadOnlyList<MechWeaponMountDefinition> BuildWeaponMounts(
        MechWarriorMechChassis chassis,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById,
        int torsoObjectId)
    {
        var mounts = new List<MechWeaponMountDefinition>(chassis.PointsOfFire.Count);
        foreach (var point in chassis.PointsOfFire)
        {
            if (!objectsById.TryGetValue(point.ObjectId, out var chassisObject))
            {
                GD.PushWarning(
                    $"MechRewired: chassis POFO {point.Id} refers to missing object {point.ObjectId}.");
                continue;
            }

            mounts.Add(new MechWeaponMountDefinition(
                point.Id,
                point.Section,
                MechWarriorCoordinateSystem.ToGodotPosition(chassisObject.Transform.Translation),
                torsoObjectId != 0 && IsDescendantOf(chassisObject.Id, torsoObjectId, objectsById)));
        }

        return mounts.AsReadOnly();
    }

    private static bool IsDescendantOf(
        int objectId,
        int ancestorId,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById)
    {
        for (var currentId = objectId; objectsById.TryGetValue(currentId, out var current); currentId = current.RelativeToId)
        {
            if (currentId == ancestorId)
            {
                return true;
            }

            if (current.RelativeToId < 0)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the original BWD object tree so that animated joint rotations propagate through
    /// the chassis exactly as authored, including invisible DUMMY joint objects.
    /// </summary>
    private static IReadOnlyDictionary<int, Node3D> BuildChassisObjectHierarchy(
        MechWarriorMechChassis chassis,
        Node3D legs,
        Node3D torso,
        int torsoObjectId)
    {
        ArgumentNullException.ThrowIfNull(chassis);
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentNullException.ThrowIfNull(torso);

        var objectsById = chassis.Objects.ToDictionary(chassisObject => chassisObject.Id);
        var torsoPivot = torsoObjectId != 0 && objectsById.TryGetValue(torsoObjectId, out var torsoObject)
            ? MechWarriorCoordinateSystem.ToGodotPosition(torsoObject.Transform.Translation)
            : Vector3.Zero;
        var roots = new Dictionary<int, Node3D>(chassis.Objects.Count);
        foreach (var chassisObject in chassis.Objects)
        {
            var isTorsoRoot = chassisObject.Id == torsoObjectId && torsoObjectId != 0;
            var isTorsoPart = torsoObjectId != 0 &&
                              IsDescendantOf(chassisObject.Id, torsoObjectId, objectsById);
            var hasAuthoredParent = roots.TryGetValue(chassisObject.RelativeToId, out var authoredParent);
            var parent = isTorsoRoot
                ? torso
                : hasAuthoredParent
                    ? authoredParent
                    : isTorsoPart
                        ? torso
                        : legs;
            var sourceTransform = hasAuthoredParent && !isTorsoRoot
                ? chassisObject.LocalTransform ?? chassisObject.Transform
                : chassisObject.Transform;
            var transform = BuildMechTransform(sourceTransform);
            if (isTorsoRoot || (isTorsoPart && !hasAuthoredParent))
            {
                transform.Origin -= torsoPivot;
            }

            var root = new Node3D
            {
                Name = $"ChassisObject{chassisObject.Id}",
                Transform = transform
            };
            parent.AddChild(root);
            roots.Add(chassisObject.Id, root);
        }

        return roots;
    }

    private static Transform3D BuildMechTransform(MechWarriorWorldTransform source)
    {
        var rotation = MechWarriorCoordinateSystem.ToGodotRotation(source.RotationDegrees) *
                       (Mathf.Pi / 180.0f);
        var basis = Basis.FromEuler(rotation).Scaled(
            MechWarriorCoordinateSystem.ToGodotScale(source.Scale));
        return new Transform3D(
            basis,
            MechWarriorCoordinateSystem.ToGodotPosition(source.Translation));
    }

    private static IReadOnlyList<MissionDropShipSetPiece> LoadMissionDropShips(
        MechWarriorProjectArchive archive,
        string levelPath,
        Node3D levelRoot,
        BattlefieldEffects battlefieldEffects,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        Vector3 deploymentAnchor,
        Vector3 extractionAnchor,
        Vector3 deploymentDirection,
        MissionRuntimeContent runtimeContent)
    {
        var levelEntry = archive.GetEntry(levelPath);
        var levelWorld = MechWarriorWorldFile.Load(archive.ReadEntry(levelEntry));
        var dropShips = new List<MissionDropShipSetPiece>();
        foreach (var include in levelWorld.Includes)
        {
            var setPieceEntry = archive.GetEntry("BWD", include.ResourceIndex);
            var setPieceWorld = MechWarriorWorldFile.Load(archive.ReadEntry(setPieceEntry));
            if (!WorldHasTaskArgument(setPieceWorld, "drop"))
            {
                continue;
            }

            var dropShipSound = LoadDropShipTaskSound(archive, setPieceWorld);
            var animatedColors = LoadDropShipColorTasks(setPieceWorld, palette);
            var dropShip = new MissionDropShipSetPiece(
                setPieceEntry.Name,
                deploymentAnchor,
                extractionAnchor,
                deploymentDirection,
                dropShipSound,
                battlefieldEffects)
            {
                Name = $"DropShip-{setPieceEntry.Name}"
            };
            var renderedObjectCount = 0;
            var assemblyBounds = new Aabb();
            var hasAssemblyBounds = false;
            foreach (var worldObject in setPieceWorld.Objects)
            {
                var modelEntry = archive.GetEntry("POLY", worldObject.ModelResourceIndex);
                if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
                {
                    // DUMMY.WTB entries are locator records, not renderable WTB geometry. Some
                    // carry an animated light; the rest only define attachment points.
                    if (animatedColors.TryGetValue(worldObject.Id, out var locatorColors))
                    {
                        var locator = new Node3D
                        {
                            Name = $"{modelEntry.Name}LightLocator",
                            Position = MechWarriorCoordinateSystem.ToGodotPosition(worldObject.Transform.Translation)
                        };
                        locator.AddChild(CreateAnimatedLocatorLight(locatorColors));
                        dropShip.AddChild(locator);
                        renderedObjectCount++;
                    }

                    continue;
                }

                try
                {
                    var models = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry));
                    var highestDetailModel = models.MaxBy(model => model.Polygons.Count);
                    var objectRoot = new Node3D
                    {
                        Name = modelEntry.Name,
                        Position = MechWarriorCoordinateSystem.ToGodotPosition(worldObject.Transform.Translation),
                        RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(worldObject.Transform.RotationDegrees),
                        Scale = MechWarriorCoordinateSystem.ToGodotScale(worldObject.Transform.Scale)
                    };
                    var renderMesh = MechWarriorModelMeshBuilder.Build(
                        highestDetailModel,
                        palette,
                        luminosityTable,
                        ObjectIlluminationLevel);
                    MechWarriorModelMeshBuilder.ApplyStructureSurfaceFinish(renderMesh);
                    MakeMeshDoubleSided(renderMesh);
                    var meshInstance = new MeshInstance3D
                    {
                        Mesh = renderMesh,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided
                    };
                    objectRoot.AddChild(meshInstance);
                    var objectBounds = objectRoot.Transform * renderMesh.GetAabb();
                    assemblyBounds = hasAssemblyBounds ? assemblyBounds.Merge(objectBounds) : objectBounds;
                    hasAssemblyBounds = true;
                    if (animatedColors.TryGetValue(worldObject.Id, out var colors))
                    {
                        var light = new OmniLight3D
                        {
                            Name = "EngineFlameLight",
                            LightColor = colors[0],
                            LightEnergy = 4.0f,
                            OmniRange = 28.0f,
                            ShadowEnabled = false
                        };
                        objectRoot.AddChild(light);
                        dropShip.ConfigureAnimatedColor(meshInstance, light, colors);
                    }
                    dropShip.AddChild(objectRoot);
                    renderedObjectCount++;
                }
                catch (InvalidDataException exception)
                {
                    GD.PushWarning(
                        $"MechRewired: skipped unsupported dropship model {modelEntry.Path} object {worldObject.Id}: " +
                        exception.Message);
                }
            }

            if (renderedObjectCount == 0)
            {
                dropShip.QueueFree();
                continue;
            }

            dropShip.ConfigureAssemblyBounds(assemblyBounds);
            levelRoot.AddChild(dropShip);
            dropShip.BeginDeployment();
            dropShips.Add(dropShip);
            runtimeContent.ReportInfo(
                MissionFidelityFindingKind.ReservedSetPiece,
                setPieceEntry.Path,
                "drop task",
                "Reserved map set piece was instantiated through the dedicated animated DropShip path.");
        }

        GD.Print($"MechRewired: staged {dropShips.Count} map-authored dropship set pieces.");
        return dropShips.AsReadOnly();
    }

    private static bool WorldHasTaskArgument(MechWarriorWorldFile world, string argument) =>
        world.Tasks.Any(task =>
            task.Command.Split([';', ','], StringSplitOptions.TrimEntries)
                .Any(candidate => candidate.Equals(argument, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<BattlefieldActor> LoadAuthoredAircraft(
        MechWarriorProjectArchive archive,
        MechWarriorLevel level,
        IReadOnlyList<BattlefieldActor> actors,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), Node3D> renderedRootsByObject,
        IList<DebugTriangle> sceneTriangles,
        BattlefieldEffects battlefieldEffects,
        MissionRuntimeContent runtimeContent)
    {
        var hostileAircraft = new List<BattlefieldActor>();
        foreach (var plan in MechWarriorAuthoredAircraftResolver.Resolve(level))
        {
            AudioStreamWav engineSound = null;
            var soundPath = $"SNDS/{plan.SoundResourceName}.WAV";
            if (archive.Entries.Any(entry => entry.Path.Equals(soundPath, StringComparison.OrdinalIgnoreCase)))
            {
                engineSound = PlayerMechSounds.LoadWaveResource(
                    archive,
                    soundPath,
                    plan.LoopSound,
                    $"{plan.Source.Entry.Name} authored aircraft engine");
            }

            var actor = actors.FirstOrDefault(candidate => ReferenceEquals(candidate.Definition, plan.Actor)) ??
                        throw new InvalidDataException(
                            $"{plan.Source.Entry.Path} resolved aircraft actor {plan.Actor.ObjectId} was not rendered.");
            var rotor = renderedRootsByObject.GetValueOrDefault(
                            (plan.Source.Entry.Path, plan.RotorComponent.Id)) ??
                        throw new InvalidDataException(
                            $"{plan.Source.Entry.Path} recon actor {actor.Definition.ObjectId} has no rendered rotor component.");
            actor.AddChild(new AuthoredAircraftController(
                actor,
                plan.MotionObject.Transform,
                plan.Path.Points,
                plan.RotateWithPath,
                sceneTriangles,
                rotor,
                engineSound,
                plan.MaximumSoundDistance,
                battlefieldEffects));
            hostileAircraft.Add(actor);
            runtimeContent.AddAircraft(plan.Actor);
            if (engineSound == null)
            {
                runtimeContent.Report(
                    MissionFidelityFindingKind.PartialSupport,
                    plan.Source.Entry.Path,
                    $"TSK sound {plan.SoundResourceName}",
                    "Authored aircraft sound resource was not available at runtime.");
            }
        }

        if (hostileAircraft.Count > 0)
        {
            GD.Print($"MechRewired: activated {hostileAircraft.Count} authored aircraft path(s).");
        }

        return hostileAircraft.AsReadOnly();
    }

    /// <summary>
    /// Instantiates the archive's ordinary type-5 path tasks.  Recon aircraft and the dropship
    /// remain specialised presentation systems because they add rotor and destruction behaviour
    /// beyond the raw PTBL motion described here.
    /// </summary>
    private static void LoadAuthoredWorldPaths(
        MechWarriorProjectArchive archive,
        MechWarriorLevel level,
        Node3D levelRoot,
        IReadOnlyList<BattlefieldActor> actors,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), (BattlefieldActor Actor, bool Destroyed)> actorComponents,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), Node3D> renderedRootsByObject,
        IList<DebugTriangle> sceneTriangles,
        IList<SceneryObstacle> staticObstacles,
        IDictionary<(string SourcePath, int ObjectId), int> staticObstacleIndicesByObject,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), IReadOnlyList<SceneryWallTriangle>> collisionWallsByObject,
        MissionRuntimeContent runtimeContent)
    {
        var activatedCount = 0;
        foreach (var source in level.Sources)
        {
            var sourceObjectsById = source.World.Objects.ToDictionary(worldObject => worldObject.Id);
            foreach (var task in source.World.Tasks.Where(candidate => candidate.Type == 5))
            {
                if (!MechWarriorWorldPathTask.TryResolve(source.World, task, out var plan, out var error))
                {
                    runtimeContent.Report(
                        MissionFidelityFindingKind.PartialSupport,
                        source.Entry.Path,
                        $"TSK {task.Command}",
                        error);
                    continue;
                }

                if (plan.Path.Name.Equals("recon", StringComparison.OrdinalIgnoreCase) ||
                    plan.Path.Name.Equals("drop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A path may target a movable component inside a larger, damageable assembly.
                // Only an entity root owns the BattlefieldActor transform; moving an owning actor
                // for one child component drags the entire building with it.
                var actor = actors.FirstOrDefault(candidate =>
                    candidate.Definition.SourceEntry.Path.Equals(source.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Definition.ObjectId == plan.MotionObjectId);
                var lifetimeOwner = FindOwningActorByObjectId(
                    source.Entry.Path,
                    plan.MotionObjectId,
                    actorComponents,
                    sourceObjectsById);
                var descendants = source.World.Objects
                    .Where(worldObject => IsWorldObjectDescendant(
                        worldObject,
                        plan.MotionObjectId,
                        sourceObjectsById))
                    .ToArray();
                var movedRoots = actor == null
                    ? CreatePathRootsForStaticObjects(
                        levelRoot,
                        source.Entry.Path,
                        descendants,
                        renderedRootsByObject)
                    : actor.Definition.Components
                        .Select(componentObject => renderedRootsByObject.GetValueOrDefault(
                            (componentObject.SourceEntry.Path, componentObject.Id)))
                        .Where(root => root != null)
                        .ToArray();
                if (actor == null && movedRoots.Count == 0)
                {
                    runtimeContent.Report(
                        MissionFidelityFindingKind.PartialSupport,
                        source.Entry.Path,
                        $"TSK {task.Command}",
                        "The path target has neither rendered geometry nor a usable locator.");
                    continue;
                }

                var parentTransform = Transform3D.Identity;
                if (plan.MotionObject.RelativeToId >= 0 &&
                    renderedRootsByObject.TryGetValue(
                        (source.Entry.Path, plan.MotionObject.RelativeToId),
                        out var parentRoot))
                {
                    parentTransform = parentRoot.GlobalTransform;
                }

                var isComponentOfStaticActor = actorComponents.ContainsKey(
                    (source.Entry.Path, plan.MotionObjectId));
                var obstacleSlots = actor == null && !isComponentOfStaticActor
                    ? EnsureDynamicPathObstacles(
                        source.Entry.Path,
                        descendants,
                        staticObstacles,
                        staticObstacleIndicesByObject,
                        collisionWallsByObject)
                    : Array.Empty<int>();
                var controller = new AuthoredWorldPathController(
                    plan,
                    source.Entry.Path,
                    actor,
                    lifetimeOwner,
                    movedRoots,
                    parentTransform,
                    sceneTriangles,
                    staticObstacles,
                    obstacleSlots);
                levelRoot.AddChild(controller);
                if (plan.Path.Name.Equals("pulshot", StringComparison.OrdinalIgnoreCase))
                {
                    AttachAuthoredPulseEffect(
                        source.Entry.Path,
                        plan,
                        movedRoots,
                        controller,
                        runtimeContent);
                }
                AttachPathTaskSounds(archive, source, plan, descendants, controller, runtimeContent);
                activatedCount++;
            }
        }

        if (activatedCount > 0)
        {
            GD.Print($"MechRewired: activated {activatedCount} archive-authored type-5 path(s).");
        }
    }

    private static void AttachAuthoredPulseEffect(
        string sourcePath,
        MechWarriorWorldPathTask plan,
        IReadOnlyList<Node3D> movedRoots,
        Node3D controller,
        MissionRuntimeContent runtimeContent)
    {
        var pulseRoot = movedRoots.FirstOrDefault(root =>
            root.GetMeta("mechrewired_object_id", -1).AsInt32() == plan.MotionObjectId);
        var originalMesh = pulseRoot?.GetChildren()
            .OfType<MeshInstance3D>()
            .FirstOrDefault(instance => instance.Visible && instance.Mesh != null)
            ?.Mesh;
        if (pulseRoot == null || originalMesh == null)
        {
            runtimeContent.Report(
                MissionFidelityFindingKind.PartialSupport,
                sourcePath,
                $"PTBL {plan.Path.Name}",
                "The authored PULSE.WTB geometry was unavailable for the HPG projectile effect.");
            return;
        }

        pulseRoot.Visible = false;
        controller.AddChild(new AuthoredPulseEffect(originalMesh));
    }

    private static IReadOnlyList<int> EnsureDynamicPathObstacles(
        string sourcePath,
        IReadOnlyList<MechWarriorWorldObject> descendants,
        IList<SceneryObstacle> staticObstacles,
        IDictionary<(string SourcePath, int ObjectId), int> obstacleIndicesByObject,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), IReadOnlyList<SceneryWallTriangle>> collisionWallsByObject)
    {
        var slots = new List<int>();
        foreach (var worldObject in descendants)
        {
            var key = (sourcePath, worldObject.Id);
            if (!obstacleIndicesByObject.TryGetValue(key, out var slot) &&
                collisionWallsByObject.TryGetValue(key, out var walls) &&
                TryCreateSceneryObstacle($"path object {worldObject.Id}", walls, out var obstacle))
            {
                slot = staticObstacles.Count;
                staticObstacles.Add(obstacle);
                obstacleIndicesByObject.Add(key, slot);
            }

            if (slot >= 0)
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    private static IReadOnlyList<Node3D> CreatePathRootsForStaticObjects(
        Node3D levelRoot,
        string sourcePath,
        IReadOnlyList<MechWarriorWorldObject> descendants,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), Node3D> renderedRootsByObject)
    {
        var roots = new List<Node3D>();
        foreach (var worldObject in descendants)
        {
            if (renderedRootsByObject.TryGetValue((sourcePath, worldObject.Id), out var renderedRoot))
            {
                roots.Add(renderedRoot);
                continue;
            }

            // DUMMY objects are locators, but their child type-4 tasks can carry positional audio.
            var locator = new Node3D { Name = $"PathLocator-{worldObject.Id}" };
            locator.SetMeta("mechrewired_object_id", worldObject.Id);
            locator.GlobalTransform = ToGodotWorldTransform(worldObject.Transform);
            levelRoot.AddChild(locator);
            roots.Add(locator);
        }

        return roots;
    }

    private static void AttachPathTaskSounds(
        MechWarriorProjectArchive archive,
        MechWarriorLevelSource source,
        MechWarriorWorldPathTask plan,
        IReadOnlyList<MechWarriorWorldObject> descendants,
        Node3D controller,
        MissionRuntimeContent runtimeContent)
    {
        var descendantIds = descendants.Select(worldObject => worldObject.Id).ToHashSet();
        foreach (var task in source.World.Tasks.Where(candidate => candidate.Type == 4))
        {
            var semicolon = task.Command.IndexOf(';');
            if (semicolon <= 0 ||
                !int.TryParse(task.Command.AsSpan(0, semicolon), out var soundObjectId) ||
                !descendantIds.Contains(soundObjectId))
            {
                continue;
            }

            var arguments = task.Command[(semicolon + 1)..]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (arguments.Length < 2)
            {
                continue;
            }

            var soundName = arguments[1];
            var entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.DirectoryName.Equals("SNDS", StringComparison.OrdinalIgnoreCase) &&
                (candidate.Name.Equals($"{soundName}.WAV", StringComparison.OrdinalIgnoreCase) ||
                 candidate.Name.Equals($"{soundName}.SFL", StringComparison.OrdinalIgnoreCase)));
            if (entry == null)
            {
                runtimeContent.Report(
                    MissionFidelityFindingKind.PartialSupport,
                    source.Entry.Path,
                    $"TSK sound {soundName}",
                    "The path-attached sound resource was not available at runtime.");
                continue;
            }

            var stream = entry.Name.EndsWith(".WAV", StringComparison.OrdinalIgnoreCase)
                ? PlayerMechSounds.LoadWaveResource(archive, entry.Path, true, "map-authored path sound")
                : PlayerMechSounds.LoadResource(archive, entry.Path, true, "map-authored path sound");
            var player = new AudioStreamPlayer3D
            {
                Name = $"AuthoredPathSound-{soundName}",
                Stream = stream,
                UnitSize = 30.0f,
                MaxDistance = 500.0f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
            };
            controller.AddChild(player);
            player.Play();
        }
    }

    private static bool IsWorldObjectDescendant(
        MechWarriorWorldObject candidate,
        int ancestorId,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById)
    {
        for (var current = candidate; ;)
        {
            if (current.Id == ancestorId)
            {
                return true;
            }

            if (current.RelativeToId < 0 ||
                !objectsById.TryGetValue(current.RelativeToId, out current))
            {
                return false;
            }
        }
    }

    private static BattlefieldActor FindOwningActorByObjectId(
        string sourcePath,
        int objectId,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), (BattlefieldActor Actor, bool Destroyed)> actorComponents,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById)
    {
        var visited = new HashSet<int>();
        for (var currentId = objectId; currentId >= 0 && visited.Add(currentId);)
        {
            if (actorComponents.TryGetValue((sourcePath, currentId), out var component))
            {
                return component.Actor;
            }

            if (!objectsById.TryGetValue(currentId, out var worldObject))
            {
                break;
            }

            currentId = worldObject.RelativeToId;
        }

        return null;
    }

    private static Transform3D ToGodotWorldTransform(MechWarriorWorldTransform transform) => new(
        Basis.FromEuler(MechWarriorCoordinateSystem.ToGodotRotation(transform.RotationDegrees) *
                        (Mathf.Pi / 180.0f)).Scaled(MechWarriorCoordinateSystem.ToGodotScale(transform.Scale)),
        MechWarriorCoordinateSystem.ToGodotPosition(transform.Translation));

    private static void MakeMeshDoubleSided(ArrayMesh mesh)
    {
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            if (mesh.SurfaceGetMaterial(surfaceIndex) is BaseMaterial3D material)
            {
                material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            }
        }
    }

    private static bool TryFindOwningActor(
        IReadOnlyDictionary<(string SourcePath, int ObjectId), (BattlefieldActor Actor, bool Destroyed)> actorComponents,
        MechWarriorLevelObject levelObject,
        out BattlefieldActor actor,
        out bool destroyed)
    {
        if (actorComponents.TryGetValue(
                (levelObject.SourceEntry.Path, levelObject.Id),
                out var directComponent) ||
            levelObject.RelativeToId >= 0 && actorComponents.TryGetValue(
                (levelObject.SourceEntry.Path, levelObject.RelativeToId),
                out directComponent))
        {
            actor = directComponent.Actor;
            destroyed = directComponent.Destroyed;
            return true;
        }

        actor = null;
        destroyed = false;
        return false;
    }

    private static IReadOnlyDictionary<int, Color[]> LoadDropShipColorTasks(
        MechWarriorWorldFile setPieceWorld,
        MechWarriorPalette palette)
    {
        var colors = new Dictionary<int, Color[]>();
        foreach (var task in setPieceWorld.Tasks.Where(IsColorAnimationTask))
        {
            var arguments = task.Command.Split([';', ','], StringSplitOptions.TrimEntries);
            if (arguments.Length < 2 || !int.TryParse(arguments[0], out var objectId))
            {
                continue;
            }

            var paletteIndices = arguments[1..]
                .Select(argument => byte.TryParse(argument, out var index) ? index : (byte?)null)
                .Where(index => index.HasValue)
                .Select(index => index.Value)
                .ToArray();
            if (paletteIndices.Length == 0)
            {
                continue;
            }

            colors[objectId] = paletteIndices
                .Select(index => ToGodotColor(palette[index]))
                .ToArray();
        }

        return colors;
    }

    /// <summary>
    /// Reads original BWD palette-cycle tasks, including locator-only DUMMY objects.
    /// </summary>
    private static IReadOnlyDictionary<(string SourcePath, int ObjectId), Color[]> LoadAuthoredColorTasks(
        IReadOnlyList<MechWarriorLevelSource> sources,
        MechWarriorPalette palette)
    {
        var animations = new Dictionary<(string SourcePath, int ObjectId), Color[]>();
        foreach (var source in sources)
        {
            foreach (var (objectId, colors) in LoadDropShipColorTasks(source.World, palette))
            {
                animations[(source.Entry.Path, objectId)] = colors;
            }
        }

        return animations;
    }

    private static bool IsColorAnimationTask(MechWarriorWorldTask task) =>
        task.Type == 1;

    private static AnimatedLocatorLight CreateAnimatedLocatorLight(Color[] colors)
    {
        var light = new AnimatedLocatorLight(colors)
        {
            Name = "AuthoredIndicatorLight",
            LightEnergy = 5.0f,
            OmniRange = 14.0f,
            ShadowEnabled = false
        };
        return light;
    }

    private static AudioStreamWav LoadDropShipTaskSound(
        MechWarriorProjectArchive archive,
        MechWarriorWorldFile setPieceWorld)
    {
        var soundName = setPieceWorld.Tasks
            .Where(task => task.Type == 4)
            .SelectMany(task => task.Command.Split([';', ','], StringSplitOptions.TrimEntries))
            .FirstOrDefault(argument => archive.Entries.Any(entry =>
                entry.DirectoryName.Equals("SNDS", StringComparison.OrdinalIgnoreCase) &&
                entry.Name.Equals($"{argument}.WAV", StringComparison.OrdinalIgnoreCase)));
        return string.IsNullOrWhiteSpace(soundName)
            ? null
            : PlayerMechSounds.LoadWaveResource(
                archive,
                $"SNDS/{soundName}.WAV",
                true,
                "map-authored dropship engine");
    }

    private static IReadOnlyList<MechWarriorLevelObject> LoadAmbientEffects(
        MechWarriorProjectArchive archive,
        MechWarriorLevel level,
        BattlefieldEffects battlefieldEffects,
        IReadOnlyDictionary<(string SourcePath, int ObjectId), (BattlefieldActor Actor, bool Destroyed)> actorComponents,
        IReadOnlyDictionary<string, AudioStreamWav> ambientSounds,
        MissionRuntimeContent runtimeContent)
    {
        var instantiatedEffects = new List<MechWarriorLevelObject>();
        var totalLoadedCount = 0;
        foreach (var source in level.Sources)
        {
            var effectsEntry = source.Entry;
            var effectsWorld = source.World;
            var sourceObjectsById = effectsWorld.Objects.ToDictionary(worldObject => worldObject.Id);
            var hasAuthoredHpgPulse = effectsWorld.Tasks.Any(task =>
                task.Type == 5 &&
                task.Command.Split([';', ','], StringSplitOptions.TrimEntries)
                    .Any(argument => argument.Equals("pulshot", StringComparison.OrdinalIgnoreCase)));
            var flameObjects = effectsWorld.Objects
                .Where(effectObject => effectObject.ObjectType == 0x10)
                .ToArray();
            if (flameObjects.Length == 0)
            {
                continue;
            }

            var soundNamesByObject = new Dictionary<int, string>();
            foreach (var task in effectsWorld.Tasks.Where(task => task.Type == 4))
            {
                var semicolon = task.Command.IndexOf(';');
                var arguments = semicolon >= 0
                    ? task.Command[(semicolon + 1)..].Split(',', StringSplitOptions.TrimEntries)
                    : Array.Empty<string>();
                if (semicolon > 0 &&
                    int.TryParse(task.Command.AsSpan(0, semicolon), out var soundObjectId) &&
                    arguments.Length >= 2)
                {
                    soundNamesByObject[soundObjectId] = arguments[1];
                }
            }

            var effectDefinitions = flameObjects
                .Select(effectObject =>
                {
                    var modelEntry = archive.GetEntry("POLY", effectObject.ModelResourceIndex);
                    var effectModel = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry))[0];
                    return (
                        Object: effectObject,
                        ModelEntry: modelEntry,
                        Bounds: GetEffectWorldBounds(effectObject, effectModel));
                })
                .ToArray();
            var fireDefinitions = effectDefinitions
                .Where(effect => !effect.ModelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var elevatedSmokeByFireId = new Dictionary<int, List<(MechWarriorWorldObject Object, Aabb Bounds)>>();
            var foldedSmokeIds = new HashSet<int>();
            foreach (var smoke in effectDefinitions.Where(effect =>
                         effect.ModelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase)))
            {
                var supportingFire = fireDefinitions
                    .Where(fire => IsElevatedSmokeAboveFire(smoke.Bounds, fire.Bounds))
                    .OrderBy(fire => HorizontalDistance(smoke.Bounds, fire.Bounds))
                    .FirstOrDefault();
                if (supportingFire.Object == null)
                {
                    continue;
                }

                if (!elevatedSmokeByFireId.TryGetValue(supportingFire.Object.Id, out var smokeVolumes))
                {
                    smokeVolumes = [];
                    elevatedSmokeByFireId.Add(supportingFire.Object.Id, smokeVolumes);
                }

                smokeVolumes.Add((smoke.Object, smoke.Bounds));
                foldedSmokeIds.Add(smoke.Object.Id);
            }

            var renderedCount = 0;
            foreach (var effect in effectDefinitions)
            {
                var effectObject = effect.Object;
                var modelEntry = effect.ModelEntry;
                var effectBounds = effect.Bounds;
                var lifetimeOwner = FindOwningActorByObjectId(
                    effectsEntry.Path,
                    effectObject.RelativeToId,
                    actorComponents,
                    sourceObjectsById);
                if (hasAuthoredHpgPulse &&
                    modelEntry.Name.StartsWith("FIR", StringComparison.OrdinalIgnoreCase))
                {
                    // PINKARE1 reuses an FIR control volume for the HPG launch flash.  A standing
                    // combustion plume misrepresents the machinery; AuthoredPulseEffect provides
                    // the timed energy packet and illumination from the same authored location.
                    instantiatedEffects.Add(new MechWarriorLevelObject(
                        effectObject.Id, effectObject.RelativeToId, effectObject.CollisionType,
                        effectObject.ObjectType, MechWarriorLevelObjectKind.Effect,
                        effectsEntry, modelEntry, effectObject.Transform));
                    continue;
                }

                if (modelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase) &&
                    foldedSmokeIds.Contains(effectObject.Id))
                {
                    instantiatedEffects.Add(new MechWarriorLevelObject(
                        effectObject.Id, effectObject.RelativeToId, effectObject.CollisionType, effectObject.ObjectType,
                        MechWarriorLevelObjectKind.Effect, effectsEntry, modelEntry, effectObject.Transform));
                    continue;
                }

                var heightOffset = effectObject.Transform.Translation.Y;
                var ambientSound = soundNamesByObject.TryGetValue(effectObject.Id, out var soundName) &&
                                   ambientSounds.TryGetValue(soundName, out var mappedSound)
                    ? mappedSound
                    : null;
                if (soundNamesByObject.TryGetValue(effectObject.Id, out soundName) && ambientSound == null)
                {
                    runtimeContent.Report(
                        MissionFidelityFindingKind.PartialSupport,
                        effectsEntry.Path,
                        $"TSK sound {soundName}",
                        "Authored ambient sound was not mapped to a runtime resource.");
                }
                if (modelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase))
                {
                    battlefieldEffects.AddAmbientSmoke(
                        effectBounds,
                        MechWarriorCoordinateSystem.ToGodotPosition(effectObject.Transform.Translation),
                        0.0f,
                        $"{effectsEntry.Name}-{effectObject.Id}",
                        ambientSound,
                        lifetimeOwner);
                }
                else
                {
                    var plumeBounds = effectBounds;
                    if (elevatedSmokeByFireId.TryGetValue(effectObject.Id, out var smokeVolumes))
                    {
                        foreach (var smokeVolume in smokeVolumes)
                        {
                            plumeBounds = MergeBounds(plumeBounds, smokeVolume.Bounds);
                        }
                    }

                    battlefieldEffects.AddAmbientFire(
                        effectBounds,
                        plumeBounds,
                        0.0f,
                        $"{effectsEntry.Name}-{effectObject.Id}",
                        ambientSound,
                        lifetimeOwner);
                }

                totalLoadedCount++;
                renderedCount++;
                instantiatedEffects.Add(new MechWarriorLevelObject(
                    effectObject.Id, effectObject.RelativeToId, effectObject.CollisionType, effectObject.ObjectType,
                    MechWarriorLevelObjectKind.Effect, effectsEntry, modelEntry, effectObject.Transform));
            }

        }

        GD.Print($"MechRewired: loaded {totalLoadedCount} authored battlefield effect objects.");
        return instantiatedEffects.AsReadOnly();
    }

    private static bool IsElevatedSmokeAboveFire(Aabb smoke, Aabb fire)
    {
        var horizontalDistance = HorizontalDistance(smoke, fire);
        return horizontalDistance <= Math.Max(smoke.Size.X, fire.Size.X) * 0.65f &&
               smoke.GetCenter().Y > fire.GetCenter().Y + fire.Size.Y * 0.12f;
    }

    private static float HorizontalDistance(Aabb first, Aabb second) =>
        new Vector2(
            first.GetCenter().X - second.GetCenter().X,
            first.GetCenter().Z - second.GetCenter().Z).Length();

    private static Aabb MergeBounds(Aabb first, Aabb second)
    {
        var minimum = first.Position.Min(second.Position);
        var maximum = first.End.Max(second.End);
        return new Aabb(minimum, maximum - minimum);
    }

    private static Aabb GetEffectWorldBounds(
        MechWarriorWorldObject effectObject,
        MechWarriorModel model)
    {
        var position = MechWarriorCoordinateSystem.ToGodotPosition(effectObject.Transform.Translation);
        var scale = MechWarriorCoordinateSystem.ToGodotScale(effectObject.Transform.Scale);
        var rotation = MechWarriorCoordinateSystem.ToGodotRotation(effectObject.Transform.RotationDegrees);
        var basis = Basis.FromEuler(rotation * (Mathf.Pi / 180.0f)).Scaled(scale);
        var transform = new Transform3D(basis, position);
        var hasBounds = false;
        var bounds = new Aabb();
        foreach (var vertex in model.Vertices)
        {
            var transformed = transform *
                (MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
                 MechWarriorModelMeshBuilder.SourceUnitScale);
            var point = new Aabb(transformed, Vector3.Zero);
            bounds = hasBounds ? bounds.Merge(point) : point;
            hasBounds = true;
        }

        return bounds;
    }

    private static float CalculateVisibilityDistance(float? viewDistance) =>
        Mathf.Clamp(
            viewDistance ?? DefaultFogDistance,
            MinimumFogDistance,
            MaximumFogDistance);

    private static float CalculateDepthCueDistance(float? shadeDistance, float visibilityDistance)
    {
        var depthCueDistance = shadeDistance is > 0.0f
            ? Mathf.Min(shadeDistance.Value, visibilityDistance)
            : visibilityDistance;

        // MW2 applies its palette depth cue from the viewer outward. The authored LITE shade
        // distance controls when terrain has fully converged on the horizon colour. Sky3D maps
        // this range to its screen-space fog; VDIST remains the outer visibility limit and can
        // provide a less aggressive endpoint for biomes without airborne sand.
        return depthCueDistance;
    }

    private static float FindDeploymentSurfaceHeight(
        TerrainSurfaceIndex terrainSurface,
        Vector3 deploymentPosition)
    {
        if (terrainSurface.TryGetHeight(deploymentPosition, out var surfaceHeight))
        {
            return surfaceHeight;
        }

        GD.PushWarning("MechRewired: no rendered surface found beneath the player deployment; using NAVP Y.");
        return deploymentPosition.Y;
    }

    private static void SettleActorOnTerrain(
        BattlefieldActor actor,
        Node3D rootRepresentation,
        IReadOnlyList<MechWarriorModel> models,
        TerrainSurfaceIndex terrainSurface,
        IList<DebugTriangle> sceneTriangles)
    {
        var lowestY = models
            .SelectMany(model => model.Vertices)
            .Select(vertex => rootRepresentation.GlobalTransform *
                              (MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
                               MechWarriorModelMeshBuilder.SourceUnitScale))
            .Min(position => position.Y);
        var surfaceHeight = terrainSurface.TryGetHeight(rootRepresentation.GlobalPosition, out var terrainHeight)
            ? terrainHeight
            : DerivedTerrainSurfaceBuilder.ImplicitGroundHeight;
        var adjustment = surfaceHeight - lowestY;
        actor.Position += Vector3.Up * adjustment;
        TranslateActorTriangles(actor, sceneTriangles, Vector3.Up * adjustment);
    }

    private static void TranslateActorTriangles(
        BattlefieldActor actor,
        IList<DebugTriangle> sceneTriangles,
        Vector3 translation)
    {
        if (translation.IsZeroApprox())
        {
            return;
        }

        var componentKeys = actor.Definition.Components
            .Select(component => (component.SourceEntry.Path, component.Id))
            .ToHashSet();
        for (var index = 0; index < sceneTriangles.Count; index++)
        {
            var triangle = sceneTriangles[index];
            if (!componentKeys.Contains((triangle.SourceResourcePath, triangle.ObjectId)))
            {
                continue;
            }

            sceneTriangles[index] = triangle with
            {
                A = triangle.A + translation,
                B = triangle.B + translation,
                C = triangle.C + translation
            };
        }
    }

    private static IReadOnlyList<SceneryObstacle> GetSceneryObstacles(
        IReadOnlyList<SceneryObstacle> staticObstacles,
        IEnumerable<BattlefieldActor> actors)
    {
        var obstacles = new List<SceneryObstacle>(staticObstacles);
        foreach (var actor in actors)
        {
            if (actor.SceneryObstacle == null)
            {
                continue;
            }

            obstacles.Add(actor.SceneryObstacle);
        }

        return obstacles;
    }

    private static bool TryCreateSceneryObstacle(
        string name,
        IReadOnlyList<SceneryWallTriangle> walls,
        out SceneryObstacle obstacle)
    {
        if (walls.Count == 0)
        {
            obstacle = null;
            return false;
        }

        var points = walls.SelectMany(wall => new[] { wall.A, wall.B, wall.C }).ToArray();
        obstacle = new SceneryObstacle(
            name,
            new System.Numerics.Vector2(points.Min(point => point.X), points.Min(point => point.Y)),
            new System.Numerics.Vector2(points.Max(point => point.X), points.Max(point => point.Y)),
            walls);
        return true;
    }

    private static IReadOnlyList<SceneryWallTriangle> BuildSceneryWalls(
        Transform3D transform,
        IReadOnlyList<MechWarriorModel> models)
    {
        const float maximumFloorNormal = 0.8f;
        var walls = new List<SceneryWallTriangle>();
        foreach (var model in models)
        {
            if (model.Vertices.Count == 0)
            {
                continue;
            }

            var transformedVertices = model.Vertices
                .Select(vertex => TransformVertex(transform, vertex))
                .ToArray();
            var modelHeight = transformedVertices.Max(vertex => vertex.Y) -
                              transformedVertices.Min(vertex => vertex.Y);
            if (modelHeight < MinimumSceneryObstacleHeight)
            {
                continue;
            }

            foreach (var polygon in model.Polygons)
            {
                for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
                {
                    var first = transformedVertices[polygon.VertexIndices[0]];
                    var second = transformedVertices[polygon.VertexIndices[triangleIndex]];
                    var third = transformedVertices[polygon.VertexIndices[triangleIndex + 1]];
                    var normal = (second - first).Cross(third - first);
                    if (normal.LengthSquared() <= 0.000001f ||
                        Mathf.Abs(normal.Normalized().Y) > maximumFloorNormal)
                    {
                        continue;
                    }

                    walls.Add(new SceneryWallTriangle(
                        new System.Numerics.Vector2(first.X, first.Z),
                        new System.Numerics.Vector2(second.X, second.Z),
                        new System.Numerics.Vector2(third.X, third.Z)));
                }
            }
        }

        return walls.AsReadOnly();
    }

    private static Color ToGodotColor(DTC.Core.Rgb color) =>
        new(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f);

    private static void AddDebugTriangles(
        ICollection<DebugTriangle> triangles,
        MechWarriorLevelObject levelObject,
        Transform3D transform,
        IReadOnlyList<MechWarriorModel> models)
    {
        for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            var model = models[modelIndex];
            for (var polygonIndex = 0; polygonIndex < model.Polygons.Count; polygonIndex++)
            {
                var polygon = model.Polygons[polygonIndex];
                for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
                {
                    triangles.Add(new DebugTriangle(
                        levelObject.SourceEntry.Path,
                        levelObject.ModelEntry.Path,
                        levelObject.Id,
                        modelIndex,
                        polygonIndex,
                        TransformVertex(transform, model.Vertices[polygon.VertexIndices[0]]),
                        TransformVertex(transform, model.Vertices[polygon.VertexIndices[triangleIndex]]),
                        TransformVertex(transform, model.Vertices[polygon.VertexIndices[triangleIndex + 1]])));
                }
            }
        }
    }

    private static Vector3 TransformVertex(Transform3D transform, MechWarriorModelVertex vertex) =>
        transform * (MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
                     MechWarriorModelMeshBuilder.SourceUnitScale);

    private static void AccumulateGroundPaletteWeights(
        IDictionary<byte, double> paletteWeights,
        Transform3D transform,
        IReadOnlyList<MechWarriorModel> models)
    {
        const float minimumProjectedArea = 0.01f;
        foreach (var model in models)
        {
            foreach (var polygon in model.Polygons)
            {
                for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
                {
                    var first = TransformVertex(transform, model.Vertices[polygon.VertexIndices[0]]);
                    var second = TransformVertex(transform, model.Vertices[polygon.VertexIndices[triangleIndex]]);
                    var third = TransformVertex(transform, model.Vertices[polygon.VertexIndices[triangleIndex + 1]]);
                    // Terrain pieces contain dark sealing faces at Y=0 but their actual land
                    // surfaces sit above that. Select the consistently wound terrain tops and use
                    // the visible terrain as a whole for the fallback's representative colour.
                    var projectedArea = (second - first).Cross(third - first).Y * 0.5;
                    if (projectedArea < minimumProjectedArea)
                    {
                        continue;
                    }

                    paletteWeights.TryGetValue(polygon.PaletteIndex, out var currentWeight);
                    paletteWeights[polygon.PaletteIndex] = currentWeight + projectedArea;
                }
            }
        }
    }

    private static Aabb GetTerrainBounds(IEnumerable<DebugTriangle> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        var terrainVertices = triangles
            .Where(triangle => triangle.SourceResourcePath == "DERIVED/TERRAIN")
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();
        if (terrainVertices.Length == 0)
        {
            throw new InvalidOperationException("MechRewired cannot scatter rocks without terrain bounds.");
        }

        var minimum = terrainVertices.Aggregate(Vector3.Inf, (current, next) => current.Min(next));
        var maximum = terrainVertices.Aggregate(-Vector3.Inf, (current, next) => current.Max(next));
        // Let deposits spill a short way onto the implicit desert floor, but never scan the
        // fallback plane's much larger visibility margin for rocks.
        var spill = new Vector3(96.0f, 0.0f, 96.0f);
        return new Aabb(minimum - spill, maximum - minimum + spill * 2.0f);
    }

    private static IReadOnlyList<DebugTriangle> AddImplicitGround(
        Node3D levelRoot,
        Aabb worldBounds,
        IReadOnlyDictionary<byte, double> groundPaletteWeights,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        ICollection<DebugTriangle> debugTriangles,
        IReadOnlyList<DebugTriangle> terrainTriangles,
        TerrainDiagnostics terrainDiagnostics,
        MechWarriorTerrainBiome terrainBiome)
    {
        const float margin = 1000.0f;
        var usesDesertTerrain = terrainBiome == MechWarriorTerrainBiome.Desert;
        var groundColor = usesDesertTerrain
            ? CalculateRepresentativeGroundColor(
                groundPaletteWeights,
                palette,
                luminosityTable,
                out var representativePaletteIndex)
            : CalculateRepresentativeGroundColor(
                groundPaletteWeights,
                palette,
                sourcePaletteIndex => sourcePaletteIndex,
                "raw visible rocky-plains terrain",
                out representativePaletteIndex,
                returnSampledAverage: true);
        var groundSurfaceKind = usesDesertTerrain
            ? TerrainSurfaceKind.Desert
            : TerrainSurfaceKind.RockyGround;
        var center = worldBounds.GetCenter();
        var size = new Vector2(worldBounds.Size.X + margin * 2.0f, worldBounds.Size.Z + margin * 2.0f);
        var groundReliefKind = usesDesertTerrain
            ? TerrainGroundReliefKind.Desert
            : TerrainGroundReliefKind.Rocky;
        Func<System.Numerics.Vector2, float> groundHeightAt = position =>
            DerivedTerrainSurfaceBuilder.ImplicitGroundHeight +
            TerrainGroundRelief.SampleOffset(position, groundReliefKind);
        var sparseGround = ImplicitGroundMeshBuilder.Build(
            size,
            center,
            DerivedTerrainSurfaceBuilder.ImplicitGroundHeight,
            groundColor,
            terrainTriangles,
            groundHeightAt);
        var ground = new MeshInstance3D
        {
            Name = "ImplicitGround",
            Position = new Vector3(
                center.X,
                DerivedTerrainSurfaceBuilder.ImplicitGroundHeight,
                center.Z),
            Mesh = sparseGround.Mesh,
            MaterialOverride = TerrainSurfaceMaterial.Create(
                groundSurfaceKind,
                usesDesertTerrain ? null : groundColor)
        };
        levelRoot.AddChild(ground);
        ground.AddToGroup(DebugCamera.SolidMeshGroup);
        var groundWireframe = new MeshInstance3D
        {
            Name = "ImplicitGroundWireframe",
            Position = ground.Position,
            Mesh = ground.Mesh,
            MaterialOverride = TerrainSurfaceMaterial.CreateWireframe(
                groundSurfaceKind,
                usesDesertTerrain ? null : groundColor),
            Visible = false
        };
        levelRoot.AddChild(groundWireframe);
        groundWireframe.AddToGroup(DebugCamera.WireframeMeshGroup);
#if DEBUG
        var rawGroundColor = CalculateRepresentativeGroundColor(
            groundPaletteWeights,
            palette,
            sourcePaletteIndex => sourcePaletteIndex,
            "raw visible terrain",
            out _);
        var rawSparseGround = ImplicitGroundMeshBuilder.Build(
            size,
            center,
            DerivedTerrainSurfaceBuilder.ImplicitGroundHeight,
            rawGroundColor,
            terrainTriangles,
            groundHeightAt);
        terrainDiagnostics.Register(
            ground,
            rawSparseGround.Mesh,
            sparseGround.Mesh);
#endif

        var renderedGroundTriangles = sparseGround.WorldTriangles
            .Select((triangle, index) => new DebugTriangle(
                "IMPLICIT/GROUND_RENDER",
                "IMPLICIT/GROUND_RENDER",
                -1,
                0,
                index,
                new Vector3(triangle.A.X, triangle.A.Y, triangle.A.Z),
                new Vector3(triangle.B.X, triangle.B.Y, triangle.B.Z),
                new Vector3(triangle.C.X, triangle.C.Y, triangle.C.Z)))
            .ToArray();
        foreach (var (triangle, index) in renderedGroundTriangles.Select((triangle, index) => (triangle, index)))
        {
            debugTriangles.Add(triangle with
            {
                ResourcePath = "IMPLICIT/GROUND",
                SourceResourcePath = "IMPLICIT/GROUND",
                PolygonIndex = index
            });
        }

        return renderedGroundTriangles;
    }

    private static DerivedTerrainSurface AddDerivedTerrain(
        Node3D levelRoot,
        List<DebugTriangle> sceneTriangles,
        ShaderMaterial terrainMaterial,
        ShaderMaterial terrainWireframeMaterial,
        TerrainDiagnostics terrainDiagnostics,
        bool useMacroRelief,
        TerrainGroundReliefKind groundReliefKind,
        bool snapLowExteriorVertices,
        bool sealToImplicitGround)
    {
        var derived = DerivedTerrainSurfaceBuilder.Build(
            sceneTriangles,
            useMacroRelief,
            groundReliefKind,
            snapLowExteriorVertices,
            sealToImplicitGround);
        if (derived.RenderMesh.GetSurfaceCount() == 0)
        {
            throw new InvalidDataException("The decoded level did not produce an upward-facing terrain surface.");
        }

        var instance = new MeshInstance3D
        {
            Name = "DerivedTerrain",
            Mesh = derived.RenderMesh,
            MaterialOverride = terrainMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        levelRoot.AddChild(instance);
        instance.AddToGroup(DebugCamera.SolidMeshGroup);
        var wireframe = new MeshInstance3D
        {
            Name = "DerivedTerrainWireframe",
            Mesh = derived.RenderMesh,
            MaterialOverride = terrainWireframeMaterial,
            Visible = false
        };
        levelRoot.AddChild(wireframe);
        wireframe.AddToGroup(DebugCamera.WireframeMeshGroup);
#if DEBUG
        terrainDiagnostics.Register(instance, derived.RenderMesh, derived.RenderMesh);
#endif

        sceneTriangles.RemoveAll(DerivedTerrainSurfaceBuilder.IsAuthoredTerrain);
        sceneTriangles.AddRange(derived.CollisionTriangles);
        return derived;
    }

    private static Color CalculateRepresentativeGroundColor(
        IReadOnlyDictionary<byte, double> groundPaletteWeights,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        out byte representativePaletteIndex) =>
        CalculateRepresentativeGroundColor(
            groundPaletteWeights,
            palette,
            sourcePaletteIndex => luminosityTable.GetPaletteIndex(sourcePaletteIndex, GeneralIlluminationLevel),
            "LUMA-adjusted visible terrain",
            out representativePaletteIndex);

    private static Color CalculateRepresentativeGroundColor(
        IReadOnlyDictionary<byte, double> groundPaletteWeights,
        MechWarriorPalette palette,
        Func<byte, byte> mapPaletteIndex,
        string description,
        out byte representativePaletteIndex,
        bool returnSampledAverage = false)
    {
        if (groundPaletteWeights.Count == 0)
        {
            throw new InvalidOperationException("The mission contains no terrain polygons from which to colour the fallback ground.");
        }

        double totalWeight = 0;
        double red = 0;
        double green = 0;
        double blue = 0;
        foreach (var (sourcePaletteIndex, weight) in groundPaletteWeights)
        {
            var color = palette[mapPaletteIndex(sourcePaletteIndex)];
            totalWeight += weight;
            red += color.R * weight;
            green += color.G * weight;
            blue += color.B * weight;
        }

        var sampledAverage = new Rgb(
            (byte)Math.Round(red / totalWeight),
            (byte)Math.Round(green / totalWeight),
            (byte)Math.Round(blue / totalWeight));
        representativePaletteIndex = Enumerable.Range(0, MechWarriorPalette.ColorCount)
            .Select(index => (Index: (byte)index, Color: palette[index]))
            .MinBy(candidate =>
            {
                var redDifference = candidate.Color.R - sampledAverage.R;
                var greenDifference = candidate.Color.G - sampledAverage.G;
                var blueDifference = candidate.Color.B - sampledAverage.B;
                return redDifference * redDifference +
                       greenDifference * greenDifference +
                       blueDifference * blueDifference;
            })
            .Index;
        var representativePaletteColor = palette[representativePaletteIndex];
        return ToGodotColor(returnSampledAverage ? sampledAverage : representativePaletteColor);
    }

    /// <summary>
    /// Presents map-authored dropship geometry for deployment and extraction.
    /// </summary>
    /// <summary>
    /// Applies an original palette-cycle task to a small point light.
    /// </summary>
    private sealed partial class AnimatedLocatorLight : OmniLight3D
    {
        private const float FrameSeconds = 0.09f;
        private readonly Color[] m_colors;
        private readonly StandardMaterial3D m_bulbMaterial;
        private float m_elapsed;

        public AnimatedLocatorLight(Color[] colors)
        {
            ArgumentNullException.ThrowIfNull(colors);
            if (colors.Length == 0)
            {
                throw new ArgumentException("An animated light needs at least one palette colour.", nameof(colors));
            }

            m_colors = colors;
            LightColor = colors[0];
            m_bulbMaterial = new StandardMaterial3D
            {
                AlbedoColor = colors[0],
                EmissionEnabled = true,
                Emission = colors[0],
                EmissionEnergyMultiplier = 5.0f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            AddChild(new MeshInstance3D
            {
                Name = "IndicatorBulb",
                Mesh = new SphereMesh { Radius = 0.35f, Height = 0.70f },
                MaterialOverride = m_bulbMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        public override void _Process(double delta)
        {
            m_elapsed += (float)delta;
            var color = m_colors[(int)(m_elapsed / FrameSeconds) % m_colors.Length];
            LightColor = color;
            m_bulbMaterial.AlbedoColor = color;
            m_bulbMaterial.Emission = color;
        }
    }

    /// <remarks>
    /// The set piece is discovered from a BWD <c>drop</c> task rather than a
    /// mission name. Its child models retain their offsets around the authored
    /// include anchor, while mission start and final-navigation data provide
    /// the deployment and extraction anchors.
    /// </remarks>
    private sealed partial class MissionDropShipSetPiece : Node3D
    {
        private const float FlightHeight = 80.0f;
        private const float DepartureHoverSeconds = 2.5f;
        private const float LiftSeconds = 9.0f;
        private const float DepartureForwardStartLiftFraction = 0.30f;
        private const float DepartureAcceleration = 25.0f;
        private const float DepartureInitialSpeed = 12.0f;
        private const float DepartureTopSpeed = 550.0f;
        private const float DepartureCullDistance = 6000.0f;
        private const float DepartureBankDegrees = -7.0f;
        private const float DepartureBankSeconds = 1.5f;
        private const float ExtractionApproachDistance = 1600.0f;
        private const float ExtractionApproachHeight = 300.0f;
        private const float ExtractionFinalHeight = 80.0f;
        private const float ExtractionApproachSpeed = 110.0f;
        private const float ExtractionDescentSpeed = 10.0f;
        private const float ExtractionBankDegrees = 4.0f;
        private const float ColorFrameSeconds = 0.09f;
        private const float DownwashStartHeight = 105.0f;
        private const float DownwashEmissionInterval = 0.16f;

        private readonly string m_sourceName;
        private readonly Vector3 m_deploymentAnchor;
        private readonly Vector3 m_extractionAnchor;
        private readonly Vector3 m_deploymentDirection;
        private readonly Vector3 m_flightRotation;
        private readonly AudioStreamPlayer3D m_engine;
        private readonly BattlefieldEffects m_battlefieldEffects;
        private readonly List<(
            MeshInstance3D Mesh,
            Light3D Light,
            StandardMaterial3D Material,
            Color[] Colors)> m_animatedColors = [];
        private float m_elapsed;
        private float m_colorElapsed;
        private float m_landingOffset;
        private float m_downwashElapsed;
        private bool m_extracting;
        private bool m_active;

        public event Action ExtractionLanded;

        public MissionDropShipSetPiece(
            string sourceName,
            Vector3 deploymentAnchor,
            Vector3 extractionAnchor,
            Vector3 deploymentDirection,
            AudioStreamWav engineSound,
            BattlefieldEffects battlefieldEffects)
        {
            m_sourceName = sourceName;
            m_deploymentAnchor = deploymentAnchor;
            m_extractionAnchor = extractionAnchor;
            m_deploymentDirection = deploymentDirection.Normalized();
            m_flightRotation = new Vector3(
                0.0f,
                Mathf.RadToDeg(Mathf.Atan2(m_deploymentDirection.X, m_deploymentDirection.Z)),
                0.0f);
            m_battlefieldEffects = battlefieldEffects ?? throw new ArgumentNullException(nameof(battlefieldEffects));
            RotationDegrees = m_flightRotation;
            if (engineSound != null)
            {
                m_engine = new AudioStreamPlayer3D
                {
                    Name = "Engine",
                    Stream = engineSound,
                    UnitSize = 30.0f,
                    MaxDistance = 1200.0f,
                    VolumeDb = -3.0f,
                    AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
                };
                AddChild(m_engine);
            }
        }

        public void BeginDeployment()
        {
            Activate(false);
        }

        public void BeginExtraction()
        {
            Activate(true);
        }

        public void ConfigureAssemblyBounds(Aabb bounds)
        {
            m_landingOffset = Math.Max(0.0f, -bounds.Position.Y) + 0.15f;
        }

        public void ConfigureAnimatedColor(
            MeshInstance3D meshInstance,
            Light3D light,
            Color[] colors)
        {
            ArgumentNullException.ThrowIfNull(meshInstance);
            ArgumentNullException.ThrowIfNull(light);
            ArgumentNullException.ThrowIfNull(colors);
            if (colors.Length == 0)
            {
                return;
            }

            var material = new StandardMaterial3D
            {
                AlbedoColor = colors[0],
                EmissionEnabled = true,
                Emission = colors[0],
                EmissionEnergyMultiplier = 3.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            meshInstance.MaterialOverride = material;
            meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            m_animatedColors.Add((meshInstance, light, material, colors));
        }

        public override void _Process(double delta)
        {
            if (!m_active)
            {
                return;
            }

            m_elapsed += (float)delta;
            UpdateAnimatedColors((float)delta);
            if (!m_extracting)
            {
                UpdateDeparture();
                return;
            }

            UpdateExtraction();
        }

        private void Activate(bool extracting)
        {
            m_extracting = extracting;
            m_elapsed = 0.0f;
            // Prime the emitter so the first lift frame kicks up dust immediately;
            // thrust precedes the visible climb rather than trailing it.
            m_downwashElapsed = DownwashEmissionInterval;
            m_active = true;
            Visible = true;
            SetEngineFlamesVisible(true);
            RotationDegrees = m_flightRotation;
            var anchor = GetLandingPosition(extracting ? m_extractionAnchor : m_deploymentAnchor);
            Position = extracting
                ? anchor - m_deploymentDirection * ExtractionApproachDistance +
                  Vector3.Up * ExtractionApproachHeight
                : anchor;
            m_engine?.Play();
        }

        private void UpdateExtraction()
        {
            var approachSeconds = ExtractionApproachDistance / ExtractionApproachSpeed;
            var landingPosition = GetLandingPosition(m_extractionAnchor);
            if (m_elapsed <= approachSeconds)
            {
                var approachProgress = m_elapsed / approachSeconds;
                var bankProgress = Mathf.Sin(approachProgress * Mathf.Pi);
                RotationDegrees = m_flightRotation + new Vector3(
                    0.0f,
                    0.0f,
                    ExtractionBankDegrees * bankProgress);
                Position = landingPosition -
                           m_deploymentDirection * ExtractionApproachDistance * (1.0f - approachProgress) +
                           Vector3.Up * Mathf.Lerp(
                               ExtractionApproachHeight,
                               ExtractionFinalHeight,
                               approachProgress);
                EmitDownwash(landingPosition);
                return;
            }

            var descentSeconds = (m_elapsed - approachSeconds);
            var height = Math.Max(0.0f, ExtractionFinalHeight - descentSeconds * ExtractionDescentSpeed);
            RotationDegrees = m_flightRotation;
            Position = landingPosition + Vector3.Up * height;
            EmitDownwash(landingPosition);
            if (height > 0.0f)
            {
                return;
            }

            m_active = false;
            m_engine?.Stop();
            SetEngineFlamesVisible(false);
            GD.Print($"MechRewired: extraction dropship {m_sourceName} landed.");
            ExtractionLanded?.Invoke();
        }

        private void UpdateAnimatedColors(float delta)
        {
            m_colorElapsed += delta;
            foreach (var (_, _, material, colors) in m_animatedColors)
            {
                var index = (int)(m_colorElapsed / ColorFrameSeconds) % colors.Length;
                material.AlbedoColor = colors[index];
                material.Emission = colors[index];
            }
        }

        private void UpdateDeparture()
        {
            var landingPosition = GetLandingPosition(m_deploymentAnchor);
            if (m_elapsed <= DepartureHoverSeconds)
            {
                // Let the engines build a visible, ground-hugging downwash before
                // a heavy DropShip begins its vertical departure.
                Position = landingPosition;
                RotationDegrees = m_flightRotation;
                EmitDownwash(landingPosition);
                return;
            }

            var liftElapsed = m_elapsed - DepartureHoverSeconds;
            if (liftElapsed <= LiftSeconds)
            {
                var liftProgress = Mathf.SmoothStep(0.0f, 1.0f, liftElapsed / LiftSeconds);
                var forwardElapsed = Math.Max(
                    0.0f,
                    liftElapsed - LiftSeconds * DepartureForwardStartLiftFraction);
                Position = landingPosition + Vector3.Up * (FlightHeight * liftProgress) +
                           m_deploymentDirection * GetDepartureDistance(forwardElapsed);
                RotationDegrees = m_flightRotation;
                EmitDownwash(landingPosition);
                return;
            }

            var flightSeconds = liftElapsed - LiftSeconds;
            var forwardFlightSeconds = flightSeconds +
                                      LiftSeconds * (1.0f - DepartureForwardStartLiftFraction);
            var bankProgress = Mathf.SmoothStep(
                0.0f,
                1.0f,
                Math.Clamp(flightSeconds / DepartureBankSeconds, 0.0f, 1.0f));
            RotationDegrees = m_flightRotation +
                              new Vector3(0.0f, 0.0f, DepartureBankDegrees * bankProgress);
            var departureDistance = GetDepartureDistance(forwardFlightSeconds);
            Position = landingPosition + Vector3.Up * FlightHeight +
                       m_deploymentDirection * departureDistance;
            if (departureDistance < DepartureCullDistance)
            {
                return;
            }

            Visible = false;
            m_active = false;
            m_engine?.Stop();
            GD.Print($"MechRewired: deployment dropship {m_sourceName} departed beyond view range.");
        }

        private static float GetDepartureDistance(float elapsed)
        {
            var accelerationSeconds =
                (DepartureTopSpeed - DepartureInitialSpeed) / DepartureAcceleration;
            var acceleratedSeconds = Math.Min(elapsed, accelerationSeconds);
            var acceleratedDistance = DepartureInitialSpeed * acceleratedSeconds +
                                      0.5f * DepartureAcceleration * acceleratedSeconds * acceleratedSeconds;
            return acceleratedDistance + DepartureTopSpeed * Math.Max(0.0f, elapsed - accelerationSeconds);
        }

        private Vector3 GetLandingPosition(Vector3 groundAnchor) =>
            groundAnchor + Vector3.Up * m_landingOffset;

        private void EmitDownwash(Vector3 landingPosition)
        {
            var altitude = Math.Max(0.0f, Position.Y - landingPosition.Y);
            if (altitude > DownwashStartHeight)
            {
                return;
            }

            m_downwashElapsed += (float)GetProcessDeltaTime();
            if (m_downwashElapsed < DownwashEmissionInterval)
            {
                return;
            }

            m_downwashElapsed = 0.0f;
            var intensity = 1.0f - altitude / DownwashStartHeight;
            var downwashPosition = new Vector3(Position.X, landingPosition.Y, Position.Z);
            m_battlefieldEffects.SpawnDropShipDownwash(downwashPosition, intensity);
        }

        private void SetEngineFlamesVisible(bool visible)
        {
            foreach (var (mesh, light, _, _) in m_animatedColors)
            {
                mesh.Visible = visible;
                light.Visible = visible;
            }
        }
    }
}
