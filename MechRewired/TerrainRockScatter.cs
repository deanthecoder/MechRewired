// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

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
    private const int OuterCellRadius = 3;
    private const int DenseCellRadius = 2;
    private const float SparseCandidateSpacingMetres = 6.0f;
    // Three-metre candidates cut new-cell terrain sampling by 56%. The increased acceptance
    // preserves roughly 70% of the former rock count while avoiding the movement hitch.
    private const float DenseCandidateSpacingMetres = 3.0f;
    private const float DensePlacementMultiplier = 9.75f;
    private const float HillProbeDistanceMetres = 14.0f;
    private const float MaximumSlopeDegrees = 18.0f;
    private const float MinimumPlacementChance = 0.006f;
    private const float MaximumPlacementChance = 0.18f;
    private const float MaximumDensePlacementChance = 0.255f;
    private const float MaximumRockGroundEmbedMetres = 0.14f;
    private const int Seed = 0x4D573252;
    private static readonly string[] RockMeshPaths =
    [
        "res://Assets/Props/Rocks/rock_00.obj",
        "res://Assets/Props/Rocks/rock_01.obj",
        "res://Assets/Props/Rocks/rock_02.obj"
    ];
    private static readonly Vector2[] HillProbeDirections =
    [
        new(1.0f, 0.0f), new(0.7071f, 0.7071f), new(0.0f, 1.0f), new(-0.7071f, 0.7071f),
        new(-1.0f, 0.0f), new(-0.7071f, -0.7071f), new(0.0f, -1.0f), new(0.7071f, -0.7071f)
    ];

    private readonly record struct RockPlacement(Transform3D Transform, int ShapeIndex, bool CastsShadow);
    private sealed record ActiveCell(Node3D Node, bool IsDense);
    private readonly record struct CellRequest(Vector2I Cell, bool IsDense, bool AllowsShadows);

    private readonly Dictionary<Vector2I, ActiveCell> m_activeCells = new();
    private readonly Dictionary<Vector2I, CellRequest> m_pendingCells = new();
    private TerrainSurfaceIndex m_terrainSurface;
    private Aabb m_terrainBounds;
    private IReadOnlyList<Mesh> m_meshes;
    private Mesh m_contactShadowMesh;
    private StandardMaterial3D m_sparseMaterial;
    private StandardMaterial3D m_denseMaterial;
    private StandardMaterial3D m_contactShadowMaterial;
    private Node3D m_observer;
    private Vector2I m_observerCell;
    private bool m_hasObserverCell;

    public static TerrainRockScatter Create(TerrainSurfaceIndex terrainSurface, Aabb terrainBounds)
    {
        ArgumentNullException.ThrowIfNull(terrainSurface);
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
            m_sparseMaterial = CreateRockMaterial(),
            m_denseMaterial = CreateDenseRockMaterial(),
            m_contactShadowMaterial = CreateContactShadowMaterial()
        };
    }

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
                // Prebuild a 5x5 dense block two cells ahead. New dense cells are therefore
                // created around 192m away, never at the player's feet.
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
            dense ? DenseCandidateSpacingMetres : SparseCandidateSpacingMetres,
            dense ? DensePlacementMultiplier : 1.0f,
            allowShadows);
        if (placements.Count == 0)
        {
            return null;
        }

        var node = new Node3D { Name = dense ? $"DenseRockCell_{cell.X}_{cell.Y}" : $"RockCell_{cell.X}_{cell.Y}" };
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
        var transforms = placements
            .Select(placement =>
            {
                var scale = placement.Transform.Basis.Scale.X;
                var groundOffset = RockGroundEmbed(scale);
                var position = placement.Transform.Origin + new Vector3(0.0f, groundOffset + 0.006f, 0.0f);
                return new Transform3D(
                    Basis.Identity.Scaled(new Vector3(scale * 0.50f, 1.0f, scale * 0.34f)),
                    position);
            })
            .ToArray();
        if (transforms.Length == 0)
        {
            return;
        }

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = m_contactShadowMesh,
            InstanceCount = transforms.Length,
            VisibleInstanceCount = transforms.Length
        };
        for (var index = 0; index < transforms.Length; index++)
        {
            multiMesh.SetInstanceTransform(index, transforms[index]);
        }

        parent.AddChild(new MultiMeshInstance3D
        {
            Name = "RockContactShadows",
            Multimesh = multiMesh,
            MaterialOverride = m_contactShadowMaterial,
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
        var transforms = placements
            .Where(placement => placement.ShapeIndex == shapeIndex && placement.CastsShadow == castsShadow)
            .Select(placement => placement.Transform)
            .ToArray();
        if (transforms.Length == 0)
        {
            return;
        }

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = m_meshes[shapeIndex],
            InstanceCount = transforms.Length,
            VisibleInstanceCount = transforms.Length
        };
        for (var index = 0; index < transforms.Length; index++)
        {
            multiMesh.SetInstanceTransform(index, transforms[index]);
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
        float candidateSpacing, float densityMultiplier, bool allowShadows)
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
                    cellOriginX + (x + 0.5f + SignedRandom(candidateSeed) * 0.34f) * candidateSpacing,
                    0.0f,
                    cellOriginZ + (z + 0.5f + SignedRandom(candidateSeed + 1) * 0.34f) * candidateSpacing);
                if (!ContainsXZ(terrainBounds, position) ||
                    !terrainSurface.TryGetSurface(position, out var height, out _) ||
                    !terrainSurface.TryGetSurfaceNormal(position, out var normal))
                {
                    continue;
                }

                var slopeDegrees = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(normal.Y, -1.0f, 1.0f)));
                if (slopeDegrees > MaximumSlopeDegrees)
                {
                    continue;
                }

                var depositWeight = CalculateDepositWeight(terrainSurface, position, height);
                var slopeWeight = 1.0f - Mathf.SmoothStep(MaximumSlopeDegrees * 0.45f, MaximumSlopeDegrees, slopeDegrees);
                var chance = Mathf.Lerp(MinimumPlacementChance, MaximumPlacementChance, depositWeight * slopeWeight);
                // A low-frequency coherent mask produces talus patches and bare ground rather
                // than an evenly peppered surface. Its mean is one, preserving density on average.
                chance *= ClusterWeight(position);
                chance = Mathf.Min(chance * densityMultiplier, densityMultiplier > 1.0f ? MaximumDensePlacementChance : MaximumPlacementChance);
                if (UnitRandom(candidateSeed + 2) > chance)
                {
                    continue;
                }

                var scale = Mathf.Lerp(0.28f, 2.0f, UnitRandom(candidateSeed + 3));
                // Sink the base slightly into the terrain so uneven source bottoms do not float.
                position.Y = height - RockGroundEmbed(scale);
                var rotation = Basis.FromEuler(new Vector3(0.0f, UnitRandom(candidateSeed + 4) * Mathf.Tau, 0.0f));
                var castsShadow = allowShadows && scale >= 1.25f && UnitRandom(candidateSeed + 6) < 0.035f;
                placements.Add(new RockPlacement(
                    new Transform3D(rotation.Scaled(Vector3.One * scale), position),
                    SelectShapeIndex(candidateSeed + 5),
                    castsShadow));
            }
        }

        return placements;
    }

    private static float CalculateDepositWeight(TerrainSurfaceIndex terrainSurface, Vector3 position, float height)
    {
        var highestNearbyHeight = height;
        var totalNearbyHeight = 0.0f;
        var sampleCount = 0;
        foreach (var direction in HillProbeDirections)
        {
            var probe = position + new Vector3(direction.X, 0.0f, direction.Y) * HillProbeDistanceMetres;
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

        var hillFoot = Mathf.SmoothStep(0.75f, 7.0f, highestNearbyHeight - height);
        var basin = Mathf.SmoothStep(0.15f, 2.0f, totalNearbyHeight / sampleCount - height);
        return Mathf.Clamp(hillFoot * 0.78f + basin * 0.22f, 0.0f, 1.0f);
    }

    private static StandardMaterial3D CreateRockMaterial() => new()
    {
        AlbedoTexture = GD.Load<Texture2D>("res://Assets/Props/Rocks/rock_color.jpg"),
        AlbedoColor = new Color(1.28f, 1.20f, 1.08f),
        NormalTexture = GD.Load<Texture2D>("res://Assets/Props/Rocks/rock_normal.jpg"),
        NormalScale = 0.42f,
        RoughnessTexture = GD.Load<Texture2D>("res://Assets/Props/Rocks/rock_roughness.jpg"),
        Roughness = 0.92f,
        Metallic = 0.0f,
        // Desert rock in a bright 3pm sky retains a warm fill even when it faces away from the sun.
        // This is deliberately subtle: it softens the black side without making the rock unlit.
        EmissionEnabled = true,
        Emission = new Color(0.24f, 0.18f, 0.11f),
        EmissionEnergyMultiplier = 0.32f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
        CullMode = BaseMaterial3D.CullModeEnum.Back
    };

    private static StandardMaterial3D CreateDenseRockMaterial()
    {
        // Pixel distance fade also culls the layer as the camera gets close. Streaming already
        // keeps construction well beyond the player, so retain opaque near-field rocks instead.
        return CreateRockMaterial();
    }

    private static ArrayMesh CreateContactShadowMesh()
    {
        const int segments = 10;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < segments; index++)
        {
            var start = Mathf.Tau * index / segments;
            var end = Mathf.Tau * (index + 1) / segments;
            tool.AddVertex(Vector3.Zero);
            tool.AddVertex(new Vector3(Mathf.Cos(start), 0.0f, Mathf.Sin(start)));
            tool.AddVertex(new Vector3(Mathf.Cos(end), 0.0f, Mathf.Sin(end)));
        }

        var mesh = new ArrayMesh();
        if (tool.Commit(mesh) == null)
        {
            throw new InvalidOperationException("Godot did not create the rock contact-shadow mesh.");
        }

        return mesh;
    }

    private static StandardMaterial3D CreateContactShadowMaterial() => new()
    {
        AlbedoColor = new Color(0.035f, 0.025f, 0.015f, 0.18f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest
    };

    private static int SelectShapeIndex(int seed)
    {
        var value = UnitRandom(seed);
        return value < 0.42f ? 0 : value < 0.75f ? 1 : 2;
    }

    private static float ClusterWeight(Vector3 position)
    {
        var broad = ValueNoise(position.X * 0.015f, position.Z * 0.015f);
        var detail = ValueNoise(position.X * 0.050f + 17.3f, position.Z * 0.050f - 9.1f);
        var cluster = broad * 0.84f + detail * 0.16f;
        // Most ground is bare; only the highest coherent regions become talus-like deposits.
        return Mathf.Lerp(0.0f, 3.8f, Mathf.SmoothStep(0.62f, 0.80f, cluster));
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
