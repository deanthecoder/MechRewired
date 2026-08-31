// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>
/// Streams deterministic, visual-only rock deposits around the player.
/// </summary>
/// <remarks>
/// The player cell receives a dense near-field layer. A small outer ring receives sparse deposits.
/// Cells are generated deterministically on entry and freed on exit, so a large desert never keeps
/// its full rock population in memory or draw submission.
/// </remarks>
public sealed partial class TerrainRockScatter : Node3D
{
    private const float CellSizeMetres = 96.0f;
    private const int OuterCellRadius = 4;
    private const int DenseCellRadius = 3;
    private const float MaximumRockGroundEmbedMetres = 0.14f;
    private const int Seed = 0x4D573252;
    private static readonly string[] RockMeshPaths =
    [
        "res://Assets/Props/Rocks/rock_00.obj",
        "res://Assets/Props/Rocks/rock_01.obj",
        "res://Assets/Props/Rocks/rock_02.obj",
        "res://Assets/Props/Rocks/rock_03.obj",
        "res://Assets/Props/Rocks/rock_04.obj",
        "res://Assets/Props/Rocks/rock_05.obj"
    ];
    private static readonly Vector2[] HillProbeDirections =
    [
        new(1.0f, 0.0f), new(0.7071f, 0.7071f), new(0.0f, 1.0f), new(-0.7071f, 0.7071f),
        new(-1.0f, 0.0f), new(-0.7071f, -0.7071f), new(0.0f, -1.0f), new(0.7071f, -0.7071f)
    ];

    private readonly record struct RockPlacement(
        Transform3D Transform,
        Vector3 SurfacePosition,
        Vector3 SurfaceNormal,
        Color RockColor,
        Color GroundBlendColor,
        int ShapeIndex,
        bool CastsShadow);
    private sealed record ActiveCell(Node3D Node, bool IsDense);
    private readonly record struct CellRequest(Vector2I Cell, bool IsDense, bool AllowsShadows);
    private readonly record struct ScatterProfile(
        string CellNamePrefix,
        float SparseCandidateSpacingMetres,
        float DenseCandidateSpacingMetres,
        float DensePlacementMultiplier,
        float HillProbeDistanceMetres,
        float MaximumSlopeDegrees,
        float MinimumPlacementChance,
        float MaximumPlacementChance,
        float MaximumDensePlacementChance,
        float MinimumRockScale,
        float MaximumRockScale,
        float ShadowMinimumScale,
        float ShadowChance,
        float HillFootWeight,
        float BasinWeight,
        float HillFootStartMetres,
        float HillFootEndMetres,
        float BasinStartMetres,
        float BasinEndMetres,
        float ClusterStart,
        float ClusterEnd,
        float MinimumClusterWeight,
        float ClusterMultiplier,
        bool UsesRockyMountainMaterial);

    // Desert keeps the existing close-range, wind-deposited feel. The mountain profile trades
    // count for scale and slope-foot clustering, leaving broad areas of bare bedrock visible.
    private static readonly ScatterProfile DesertProfile = new(
        "DesertRockCell", 6.0f, 3.0f, 9.75f, 14.0f, 18.0f,
        0.006f, 0.18f, 0.255f, 0.28f, 2.0f, 1.25f, 0.035f,
        0.78f, 0.22f, 0.75f, 7.0f, 0.15f, 2.0f, 0.62f, 0.80f, 0.0f, 3.8f, false);
    private static readonly ScatterProfile RockyMountainProfile = new(
        "MountainRockCell", 10.0f, 5.0f, 5.2f, 20.0f, 28.0f,
        0.003f, 0.115f, 0.18f, 0.34f, 3.8f, 2.0f, 0.14f,
        0.86f, 0.14f, 0.35f, 5.5f, 0.08f, 1.7f, 0.54f, 0.75f, 0.18f, 4.6f, true);

    private readonly Dictionary<Vector2I, ActiveCell> m_activeCells = new();
    private readonly Dictionary<Vector2I, CellRequest> m_pendingCells = new();
    private TerrainSurfaceIndex m_terrainSurface;
    private Aabb m_terrainBounds;
    private IReadOnlyList<Mesh> m_meshes;
    private Mesh m_contactShadowMesh;
    private Mesh m_groundBlendMesh;
    private StandardMaterial3D m_sparseMaterial;
    private StandardMaterial3D m_denseMaterial;
    private StandardMaterial3D m_contactShadowMaterial;
    private StandardMaterial3D m_groundBlendMaterial;
    private ScatterProfile m_profile;
    private Node3D m_observer;
    private Vector2I m_observerCell;
    private bool m_hasObserverCell;

    /// <summary>Creates a biome-tuned, deterministic scatter stream.</summary>
    public static TerrainRockScatter Create(
        TerrainSurfaceIndex terrainSurface,
        Aabb terrainBounds,
        MechWarriorTerrainBiome biome)
    {
        ArgumentNullException.ThrowIfNull(terrainSurface);
        var profile = biome == MechWarriorTerrainBiome.RockyMountain
            ? RockyMountainProfile
            : DesertProfile;
        var meshes = RockMeshPaths.Select(LoadRockMesh).ToArray();
        if (meshes.Any(mesh => mesh == null))
        {
            var missing = RockMeshPaths.Where((_, index) => meshes[index] == null);
            throw new InvalidOperationException(
                $"MechRewired: missing generated rock assets: {string.Join(", ", missing)}. " +
                "Open the project once to import Assets/Props/Rocks, then launch the mission.");
        }

        return new TerrainRockScatter
        {
            Name = "TerrainRockScatter",
            m_terrainSurface = terrainSurface,
            m_terrainBounds = terrainBounds,
            m_meshes = meshes!,
            m_contactShadowMesh = CreateContactShadowMesh(),
            m_groundBlendMesh = CreateGroundBlendMesh(),
            m_sparseMaterial = CreateRockMaterial(profile),
            m_denseMaterial = CreateDenseRockMaterial(profile),
            m_contactShadowMaterial = CreateContactShadowMaterial(profile),
            m_groundBlendMaterial = CreateGroundBlendMaterial(profile),
            m_profile = profile
        };
    }

    /// <summary>Creates the original desert stream when no biome is supplied.</summary>
    public static TerrainRockScatter Create(TerrainSurfaceIndex terrainSurface, Aabb terrainBounds) =>
        Create(terrainSurface, terrainBounds, MechWarriorTerrainBiome.Desert);

    /// <summary>Starts streaming cells around the deployed player.</summary>
    public void ConfigureObserver(Node3D observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        m_observer = observer;
        m_hasObserverCell = false;
        UpdateActiveCells();
        // This runs during scene construction, before PlayerHud.BeginPowerUp() fades from black.
        // Pay the initial population cost while the loading screen is still opaque; traversal later
        // remains time-sliced by _Process().
        while (m_pendingCells.Count > 0)
        {
            ProcessOnePendingCell();
        }
    }

    public override void _Process(double delta)
    {
        if (m_observer != null)
        {
            UpdateActiveCells();
            ProcessOnePendingCell();
        }
    }

    private static Mesh LoadRockMesh(string resourcePath)
    {
        var resource = GD.Load<Resource>(resourcePath);
        if (resource is Mesh mesh)
        {
            return mesh;
        }

        if (resource is not PackedScene scene)
        {
            return null;
        }

        var root = scene.Instantiate<Node>();
        var meshInstance = FindMeshInstance(root);
        root.QueueFree();
        return meshInstance?.Mesh;
    }

    private static MeshInstance3D FindMeshInstance(Node node)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            return meshInstance;
        }

        foreach (var child in node.GetChildren())
        {
            var result = FindMeshInstance(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private void UpdateActiveCells()
    {
        var observerCell = ToCell(m_observer.GlobalPosition);
        if (m_hasObserverCell && observerCell == m_observerCell)
        {
            return;
        }

        m_observerCell = observerCell;
        m_hasObserverCell = true;
        var wantedCells = new HashSet<Vector2I>();
        for (var z = -OuterCellRadius; z <= OuterCellRadius; z++)
        {
            for (var x = -OuterCellRadius; x <= OuterCellRadius; x++)
            {
                var cell = observerCell + new Vector2I(x, z);
                wantedCells.Add(cell);
                // Prebuild a 7x7 dense block three cells ahead. New dense cells are therefore
                // created around 288m away, never at the player's feet.
                var shouldBeDense = Mathf.Abs(x) <= DenseCellRadius && Mathf.Abs(z) <= DenseCellRadius;
                if (m_activeCells.TryGetValue(cell, out var activeCell) &&
                    activeCell.IsDense == shouldBeDense)
                {
                    continue;
                }

                m_pendingCells[cell] = new CellRequest(cell, shouldBeDense, cell == observerCell);
            }
        }

        foreach (var (cell, activeCell) in m_activeCells.Where(pair => !wantedCells.Contains(pair.Key)).ToArray())
        {
            activeCell.Node.QueueFree();
            m_activeCells.Remove(cell);
        }
        foreach (var cell in m_pendingCells.Keys.Where(cell => !wantedCells.Contains(cell)).ToArray())
        {
            m_pendingCells.Remove(cell);
        }

        GD.Print(
            $"MechRewired: streamed {m_activeCells.Count:N0} active and {m_pendingCells.Count:N0} queued " +
            $"rock cells around player cell ({observerCell.X}, {observerCell.Y}).");
    }

    private void ProcessOnePendingCell()
    {
        if (m_pendingCells.Count == 0)
        {
            return;
        }

        // Build one cell per frame and prioritize the cell under the player. This prevents a
        // noticeable movement hitch when several cells enter the streaming window together.
        var request = m_pendingCells.Values
            .OrderByDescending(candidate => candidate.AllowsShadows)
            .First();
        m_pendingCells.Remove(request.Cell);
        var cellNode = BuildCell(request.Cell, request.IsDense, request.AllowsShadows);
        if (m_activeCells.Remove(request.Cell, out var previous))
        {
            previous.Node.QueueFree();
        }

        if (cellNode != null)
        {
            AddChild(cellNode);
            m_activeCells.Add(request.Cell, new ActiveCell(cellNode, request.IsDense));
        }
    }

    private Node3D BuildCell(Vector2I cell, bool dense, bool allowShadows)
    {
        var placements = BuildCellPlacements(
            m_terrainSurface, m_terrainBounds, cell.X, cell.Y,
            dense ? m_profile.DenseCandidateSpacingMetres : m_profile.SparseCandidateSpacingMetres,
            dense ? m_profile.DensePlacementMultiplier : 1.0f,
            m_profile,
            allowShadows);
        if (placements.Count == 0)
        {
            return null;
        }

        var node = new Node3D
        {
            Name = dense
                ? $"Dense{m_profile.CellNamePrefix}_{cell.X}_{cell.Y}"
                : $"{m_profile.CellNamePrefix}_{cell.X}_{cell.Y}"
        };
        for (var shapeIndex = 0; shapeIndex < m_meshes.Count; shapeIndex++)
        {
            AddInstances(node, placements, shapeIndex, castsShadow: false, dense ? m_denseMaterial : m_sparseMaterial);
            AddInstances(node, placements, shapeIndex, castsShadow: true, dense ? m_denseMaterial : m_sparseMaterial);
        }
        AddContactShadows(node, placements);

        return node;
    }

    private void AddContactShadows(Node3D parent, IReadOnlyList<RockPlacement> placements)
    {
        if (placements.Count == 0)
        {
            return;
        }

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = m_contactShadowMesh,
            InstanceCount = placements.Count,
            VisibleInstanceCount = placements.Count
        };
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            var scale = placement.Transform.Basis.Scale;
            var shadowBasis = SurfaceAlignedBasis(placement.SurfaceNormal, index * 0.87f)
                .Scaled(new Vector3(scale.X * 0.68f, 1.0f, scale.Z * 0.52f));
            multiMesh.SetInstanceTransform(index, new Transform3D(
                shadowBasis,
                placement.SurfacePosition + placement.SurfaceNormal * 0.012f));
            // A radial vertex-alpha fade keeps this grounding cue soft rather than a stamped
            // opaque ellipse. Large outcrops receive a little more contact weight.
            multiMesh.SetInstanceColor(index, new Color(1.0f, 1.0f, 1.0f,
                Mathf.Clamp(0.72f + Mathf.Max(scale.X, scale.Z) * 0.07f, 0.72f, 1.0f)));
        }

        parent.AddChild(new MultiMeshInstance3D
        {
            Name = "RockContactShadows",
            Multimesh = multiMesh,
            MaterialOverride = m_contactShadowMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });

        AddGroundBlends(parent, placements);
    }

    private void AddGroundBlends(Node3D parent, IReadOnlyList<RockPlacement> placements)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = m_groundBlendMesh,
            InstanceCount = placements.Count,
            VisibleInstanceCount = placements.Count
        };
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            var scale = placement.Transform.Basis.Scale;
            // A broad, very transparent terrain-tinted skirt disguises the hard object/ground
            // seam. Its radial falloff is baked into the mesh, so it costs one batched draw.
            var skirtBasis = SurfaceAlignedBasis(placement.SurfaceNormal, index * 1.31f)
                .Scaled(new Vector3(scale.X * 1.16f, 1.0f, scale.Z * 1.10f));
            multiMesh.SetInstanceTransform(index, new Transform3D(
                skirtBasis,
                placement.SurfacePosition + placement.SurfaceNormal * 0.007f));
            multiMesh.SetInstanceColor(index, placement.GroundBlendColor);
        }

        parent.AddChild(new MultiMeshInstance3D
        {
            Name = "RockGroundBlends",
            Multimesh = multiMesh,
            MaterialOverride = m_groundBlendMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private void AddInstances(
        Node3D parent,
        IReadOnlyList<RockPlacement> placements,
        int shapeIndex,
        bool castsShadow,
        Godot.Material material)
    {
        var instancePlacements = placements
            .Where(placement => placement.ShapeIndex == shapeIndex && placement.CastsShadow == castsShadow)
            .ToArray();
        if (instancePlacements.Length == 0)
        {
            return;
        }

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = m_meshes[shapeIndex],
            InstanceCount = instancePlacements.Length,
            VisibleInstanceCount = instancePlacements.Length
        };
        for (var index = 0; index < instancePlacements.Length; index++)
        {
            multiMesh.SetInstanceTransform(index, instancePlacements[index].Transform);
            multiMesh.SetInstanceColor(index, instancePlacements[index].RockColor);
        }

        parent.AddChild(new MultiMeshInstance3D
        {
            Name = castsShadow ? $"RockShadow_{shapeIndex}" : $"Rock_{shapeIndex}",
            Multimesh = multiMesh,
            MaterialOverride = material,
            CastShadow = castsShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private static List<RockPlacement> BuildCellPlacements(
        TerrainSurfaceIndex terrainSurface, Aabb terrainBounds, int cellX, int cellZ,
        float candidateSpacing, float densityMultiplier, ScatterProfile profile, bool allowShadows)
    {
        var placements = new List<RockPlacement>();
        var candidatesPerEdge = Mathf.RoundToInt(CellSizeMetres / candidateSpacing);
        var cellOriginX = cellX * CellSizeMetres;
        var cellOriginZ = cellZ * CellSizeMetres;
        for (var z = 0; z < candidatesPerEdge; z++)
        {
            for (var x = 0; x < candidatesPerEdge; x++)
            {
                var candidateSeed = Hash(cellX, cellZ, x, z, candidatesPerEdge);
                var position = new Vector3(
                    // This deliberately crosses candidate-cell boundaries. Deposits retain a
                    // predictable average density, but no longer reveal the generator's rows.
                    cellOriginX + (x + 0.5f + SignedRandom(candidateSeed) * 0.82f) * candidateSpacing,
                    0.0f,
                    cellOriginZ + (z + 0.5f + SignedRandom(candidateSeed + 1) * 0.82f) * candidateSpacing);
                if (!TryAddPlacement(
                        placements, terrainSurface, terrainBounds, position, candidateSeed,
                        densityMultiplier, profile, allowShadows, out var depositWeight))
                {
                    continue;
                }

                // A restrained second stone makes selected deposits read as naturally settled
                // pairs/trios. It is capped to one extra instance and terrain-validated, so the
                // stream remains predictable and roughly 6-17% denser only where deposits form.
                var clusterChance = Mathf.Lerp(0.055f, 0.17f, depositWeight);
                if (UnitRandom(candidateSeed + 27) >= clusterChance)
                {
                    continue;
                }

                var source = placements[^1];
                var direction = new Vector3(
                    Mathf.Cos(UnitRandom(candidateSeed + 28) * Mathf.Tau),
                    0.0f,
                    Mathf.Sin(UnitRandom(candidateSeed + 28) * Mathf.Tau));
                var sourceScale = source.Transform.Basis.Scale;
                var spacing = Mathf.Max(sourceScale.X, sourceScale.Z) *
                    Mathf.Lerp(1.00f, 1.85f, UnitRandom(candidateSeed + 29));
                var clusterPosition = source.SurfacePosition + direction * spacing;
                TryAddPlacement(
                    placements, terrainSurface, terrainBounds, clusterPosition, candidateSeed + 31,
                    densityMultiplier, profile, allowShadows, out _, bypassPlacementChance: true,
                    scaleMultiplier: Mathf.Lerp(0.48f, 0.82f, UnitRandom(candidateSeed + 32)));
            }
        }

        return placements;
    }

    private static bool TryAddPlacement(
        ICollection<RockPlacement> placements,
        TerrainSurfaceIndex terrainSurface,
        Aabb terrainBounds,
        Vector3 surfacePosition,
        int seed,
        float densityMultiplier,
        ScatterProfile profile,
        bool allowShadows,
        out float depositWeight,
        bool bypassPlacementChance = false,
        float scaleMultiplier = 1.0f)
    {
        depositWeight = 0.0f;
        if (!ContainsXZ(terrainBounds, surfacePosition) ||
            !terrainSurface.TryGetSurface(surfacePosition, out var height, out _) ||
            !terrainSurface.TryGetSurfaceNormal(surfacePosition, out var normal))
        {
            return false;
        }

        normal = normal.Normalized();
        var slopeDegrees = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(normal.Y, -1.0f, 1.0f)));
        if (slopeDegrees > profile.MaximumSlopeDegrees)
        {
            return false;
        }

        depositWeight = CalculateDepositWeight(terrainSurface, surfacePosition, height, profile);
        var slopeWeight = 1.0f - Mathf.SmoothStep(
            profile.MaximumSlopeDegrees * 0.45f, profile.MaximumSlopeDegrees, slopeDegrees);
        var chance = Mathf.Lerp(
            profile.MinimumPlacementChance, profile.MaximumPlacementChance, depositWeight * slopeWeight);
        // A low-frequency coherent mask produces talus patches and bare ground rather than an
        // evenly peppered surface. In mountain terrain those patches are loose slope-foot scree.
        chance *= ClusterWeight(surfacePosition, profile);
        chance = Mathf.Min(
            chance * densityMultiplier,
            densityMultiplier > 1.0f ? profile.MaximumDensePlacementChance : profile.MaximumPlacementChance);
        if (!bypassPlacementChance && UnitRandom(seed + 2) > chance)
        {
            return false;
        }

        var scaleWeight = UnitRandom(seed + 3);
        if (profile.UsesRockyMountainMaterial)
        {
            // Larger forms belong at terrain feet; small loose stones remain sparse on flatter
            // ground. This keeps the mountain floor readable rather than carpeted.
            scaleWeight = Mathf.Clamp(scaleWeight * 0.52f + depositWeight * 0.62f, 0.0f, 1.0f);
        }

        var baseScale = Mathf.Lerp(profile.MinimumRockScale, profile.MaximumRockScale, scaleWeight) * scaleMultiplier;
        var scale = new Vector3(
            baseScale * Mathf.Lerp(0.72f, 1.26f, UnitRandom(seed + 7)),
            baseScale * Mathf.Lerp(0.78f, 1.18f, UnitRandom(seed + 8)),
            baseScale * Mathf.Lerp(0.70f, 1.30f, UnitRandom(seed + 9)));
        var yaw = UnitRandom(seed + 4) * Mathf.Tau;
        var leanLimit = profile.UsesRockyMountainMaterial ? 6.0f : 4.0f;
        var lean = Mathf.DegToRad(leanLimit);
        var rotation = SurfaceAlignedBasis(normal, yaw) * Basis.FromEuler(new Vector3(
            SignedRandom(seed + 10) * lean,
            0.0f,
            SignedRandom(seed + 11) * lean));
        var maxScale = Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z));
        surfacePosition.Y = height;
        var rockPosition = surfacePosition - normal * RockGroundEmbed(maxScale);
        var castsShadow = allowShadows &&
            maxScale >= profile.ShadowMinimumScale &&
            UnitRandom(seed + 6) < profile.ShadowChance;
        placements.Add(new RockPlacement(
            new Transform3D(rotation.Scaled(scale), rockPosition),
            surfacePosition,
            normal,
            CreateRockColor(surfacePosition, depositWeight, profile, seed),
            CreateGroundBlendColor(surfacePosition, depositWeight, profile, seed),
            SelectShapeIndex(seed + 5, profile),
            castsShadow));
        return true;
    }

    private static float CalculateDepositWeight(
        TerrainSurfaceIndex terrainSurface,
        Vector3 position,
        float height,
        ScatterProfile profile)
    {
        var highestNearbyHeight = height;
        var totalNearbyHeight = 0.0f;
        var sampleCount = 0;
        foreach (var direction in HillProbeDirections)
        {
            var probe = position + new Vector3(direction.X, 0.0f, direction.Y) * profile.HillProbeDistanceMetres;
            if (!terrainSurface.TryGetHeight(probe, out var probeHeight))
            {
                continue;
            }

            highestNearbyHeight = Mathf.Max(highestNearbyHeight, probeHeight);
            totalNearbyHeight += probeHeight;
            sampleCount++;
        }

        if (sampleCount == 0)
        {
            return 0.0f;
        }

        var hillFoot = Mathf.SmoothStep(
            profile.HillFootStartMetres, profile.HillFootEndMetres, highestNearbyHeight - height);
        var basin = Mathf.SmoothStep(
            profile.BasinStartMetres, profile.BasinEndMetres, totalNearbyHeight / sampleCount - height);
        return Mathf.Clamp(hillFoot * profile.HillFootWeight + basin * profile.BasinWeight, 0.0f, 1.0f);
    }

    private static StandardMaterial3D CreateRockMaterial(ScatterProfile profile) => new()
    {
        AlbedoTexture = GD.Load<Texture2D>("res://Assets/Props/Rocks/rock_color.jpg"),
        AlbedoColor = profile.UsesRockyMountainMaterial
            ? new Color(0.95f, 0.98f, 1.02f)
            : new Color(1.28f, 1.20f, 1.08f),
        NormalTexture = GD.Load<Texture2D>("res://Assets/Props/Rocks/rock_normal.jpg"),
        NormalScale = 0.42f,
        RoughnessTexture = GD.Load<Texture2D>("res://Assets/Props/Rocks/rock_roughness.jpg"),
        Roughness = 0.92f,
        Metallic = 0.0f,
        VertexColorUseAsAlbedo = true,
        // The small emission lift keeps the unshadowed MultiMesh layer grounded without letting
        // it flatten the selectively shadowed mountain outcrops.
        EmissionEnabled = true,
        Emission = profile.UsesRockyMountainMaterial
            ? new Color(0.12f, 0.14f, 0.17f)
            : new Color(0.24f, 0.18f, 0.11f),
        EmissionEnergyMultiplier = profile.UsesRockyMountainMaterial ? 0.16f : 0.32f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
        CullMode = BaseMaterial3D.CullModeEnum.Back
    };

    private static StandardMaterial3D CreateDenseRockMaterial(ScatterProfile profile)
    {
        // Pixel distance fade also culls the layer as the camera gets close. Streaming already
        // keeps construction well beyond the player, so retain opaque near-field rocks instead.
        return CreateRockMaterial(profile);
    }

    private static ArrayMesh CreateContactShadowMesh() => CreateRadialDecalMesh(10, 0.72f);

    private static ArrayMesh CreateGroundBlendMesh() => CreateRadialDecalMesh(12, 0.56f);

    private static ArrayMesh CreateRadialDecalMesh(int segments, float centreAlpha)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < segments; index++)
        {
            var start = Mathf.Tau * index / segments;
            var end = Mathf.Tau * (index + 1) / segments;
            tool.SetColor(new Color(1.0f, 1.0f, 1.0f, centreAlpha));
            tool.AddVertex(Vector3.Zero);
            tool.SetColor(new Color(1.0f, 1.0f, 1.0f, 0.0f));
            tool.AddVertex(new Vector3(Mathf.Cos(start), 0.0f, Mathf.Sin(start)));
            tool.SetColor(new Color(1.0f, 1.0f, 1.0f, 0.0f));
            tool.AddVertex(new Vector3(Mathf.Cos(end), 0.0f, Mathf.Sin(end)));
        }

        var mesh = new ArrayMesh();
        if (tool.Commit(mesh) == null)
        {
            throw new InvalidOperationException("Godot did not create the rock contact-shadow mesh.");
        }

        return mesh;
    }

    private static StandardMaterial3D CreateContactShadowMaterial(ScatterProfile profile) => new()
    {
        AlbedoColor = profile.UsesRockyMountainMaterial
            ? new Color(0.018f, 0.022f, 0.030f, 0.16f)
            : new Color(0.035f, 0.025f, 0.015f, 0.18f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest
    };

    private static StandardMaterial3D CreateGroundBlendMaterial(ScatterProfile profile) => new()
    {
        AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        RenderPriority = -1
    };

    private static Color CreateRockColor(
        Vector3 position,
        float depositWeight,
        ScatterProfile profile,
        int seed)
    {
        // Two low-frequency masks avoid noisy per-instance confetti while still allowing talus
        // at a cliff foot to inherit a slightly different tone from exposed rock higher up.
        var localVariation = (ValueNoise(position.X * 0.018f, position.Z * 0.018f) - 0.5f) * 0.13f +
            SignedRandom(seed + 19) * 0.026f;
        if (profile.UsesRockyMountainMaterial)
        {
            return new Color(
                0.91f + localVariation + depositWeight * 0.055f,
                0.93f + localVariation * 0.80f + depositWeight * 0.025f,
                0.95f + localVariation * 0.62f,
                1.0f);
        }

        return new Color(
            0.94f + localVariation + depositWeight * 0.070f,
            0.89f + localVariation * 0.72f + depositWeight * 0.046f,
            0.79f + localVariation * 0.45f + depositWeight * 0.025f,
            1.0f);
    }

    private static Color CreateGroundBlendColor(
        Vector3 position,
        float depositWeight,
        ScatterProfile profile,
        int seed)
    {
        var localVariation = (ValueNoise(position.X * 0.011f + 41.7f, position.Z * 0.011f - 23.4f) - 0.5f) * 0.08f +
            SignedRandom(seed + 23) * 0.018f;
        if (profile.UsesRockyMountainMaterial)
        {
            return new Color(
                0.29f + localVariation + depositWeight * 0.045f,
                0.25f + localVariation * 0.82f + depositWeight * 0.030f,
                0.21f + localVariation * 0.60f,
                0.075f + depositWeight * 0.040f);
        }

        return new Color(
            0.62f + localVariation + depositWeight * 0.045f,
            0.47f + localVariation * 0.80f + depositWeight * 0.035f,
            0.28f + localVariation * 0.55f,
            0.070f + depositWeight * 0.035f);
    }

    private static Basis SurfaceAlignedBasis(Vector3 surfaceNormal, float yaw)
    {
        var up = surfaceNormal.Normalized();
        var reference = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.94f ? Vector3.Right : Vector3.Forward;
        var right = up.Cross(reference).Normalized();
        var forward = right.Cross(up).Normalized();
        return new Basis(right, up, forward) * Basis.FromEuler(new Vector3(0.0f, yaw, 0.0f));
    }

    private static int SelectShapeIndex(int seed, ScatterProfile profile)
    {
        var value = UnitRandom(seed);
        if (profile.UsesRockyMountainMaterial)
        {
            // Taller, broken forms lead rocky deposits, while every source model remains present.
            return value < 0.13f ? 0 : value < 0.27f ? 1 : value < 0.40f ? 2 :
                value < 0.63f ? 3 : value < 0.78f ? 4 : 5;
        }

        return value < 0.20f ? 0 : value < 0.38f ? 1 : value < 0.55f ? 2 :
            value < 0.70f ? 3 : value < 0.85f ? 4 : 5;
    }

    private static float ClusterWeight(Vector3 position, ScatterProfile profile)
    {
        var broad = ValueNoise(position.X * 0.015f, position.Z * 0.015f);
        var detail = ValueNoise(position.X * 0.050f + 17.3f, position.Z * 0.050f - 9.1f);
        var cluster = broad * 0.84f + detail * 0.16f;
        // Most ground is bare; mountain terrain retains a tiny loose-stone baseline while only
        // the highest coherent regions become talus-like deposits.
        return Mathf.Lerp(
            profile.MinimumClusterWeight,
            profile.ClusterMultiplier,
            Mathf.SmoothStep(profile.ClusterStart, profile.ClusterEnd, cluster));
    }

    private static float ValueNoise(float x, float z)
    {
        var cellX = Mathf.FloorToInt(x);
        var cellZ = Mathf.FloorToInt(z);
        var localX = SmoothInterpolation(x - cellX);
        var localZ = SmoothInterpolation(z - cellZ);
        return Mathf.Lerp(
            Mathf.Lerp(HashUnit(cellX, cellZ), HashUnit(cellX + 1, cellZ), localX),
            Mathf.Lerp(HashUnit(cellX, cellZ + 1), HashUnit(cellX + 1, cellZ + 1), localX),
            localZ);
    }

    private static float SmoothInterpolation(float value) => value * value * (3.0f - 2.0f * value);

    private static float HashUnit(int x, int z) => UnitRandom(Hash(x, z, 0, 0, 0));

    private static Vector2I ToCell(Vector3 position) => new(
        Mathf.FloorToInt(position.X / CellSizeMetres), Mathf.FloorToInt(position.Z / CellSizeMetres));

    private static bool ContainsXZ(Aabb bounds, Vector3 position) =>
        position.X >= bounds.Position.X && position.X <= bounds.End.X &&
        position.Z >= bounds.Position.Z && position.Z <= bounds.End.Z;

    private static int Hash(int cellX, int cellZ, int x, int z, int resolution)
    {
        unchecked
        {
            var value = Seed;
            value = value * 486187739 + cellX;
            value = value * 486187739 + cellZ;
            value = value * 486187739 + x;
            value = value * 486187739 + z;
            value = value * 486187739 + resolution;
            return value;
        }
    }

    private static float UnitRandom(int seed)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= value >> 16;
            value *= 0x7FEB352D;
            value ^= value >> 15;
            value *= 0x846CA68B;
            value ^= value >> 16;
            return value / (float)uint.MaxValue;
        }
    }

    private static float SignedRandom(int seed) => UnitRandom(seed) * 2.0f - 1.0f;

    private static float RockGroundEmbed(float scale) =>
        Mathf.Min(MaximumRockGroundEmbedMetres, scale * 0.12f);
}
