// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Resources;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>Runs a normal type-5 BWD path task and keeps its runtime geometry in sync.</summary>
/// <remarks>
/// BWD PTBL positions are relative to an object's authored parent when it has one.  Rendered
/// level objects are deliberately flattened, so this controller composes that parent transform
/// before applying the path's world-space delta to the corresponding rendered roots.
/// </remarks>
public partial class AuthoredWorldPathController : Node3D
{
    // Mechanical Type-5 paths in the original game run more deliberately than the recon
    // aircraft clock, but not at the raw 60 Hz frame timing used by the renderer.
    private const float MechanicalPathTicksPerSecond = 120.0f;
    private static readonly Vector3 ModelForward = Vector3.Forward;

    private readonly MechWarriorWorldPathTask m_plan;
    private readonly BattlefieldActor m_actor;
    private readonly BattlefieldActor m_lifetimeOwner;
    private readonly IReadOnlyList<Node3D> m_movedRoots;
    private readonly IList<DebugTriangle> m_sceneTriangles;
    private readonly int[] m_triangleIndices;
    private readonly IList<SceneryObstacle> m_staticObstacles;
    private readonly (int Index, SceneryObstacle Original)[] m_obstacleSlots;
    private readonly Transform3D m_parentTransform;
    private readonly Transform3D m_motionAnchor;
    private readonly Transform3D m_initialPathTransform;
    private readonly Transform3D[] m_initialRootTransforms;
    private Transform3D m_currentTransform;
    private int m_segmentIndex;
    private float m_segmentElapsed;
    private bool m_stoppedByDestruction;

    public AuthoredWorldPathController(
        MechWarriorWorldPathTask plan,
        string sourcePath,
        BattlefieldActor actor,
        BattlefieldActor lifetimeOwner,
        IReadOnlyList<Node3D> movedRoots,
        Transform3D parentTransform,
        IList<DebugTriangle> sceneTriangles,
        IList<SceneryObstacle> staticObstacles,
        IEnumerable<int> obstacleSlots)
    {
        m_plan = plan ?? throw new ArgumentNullException(nameof(plan));
        m_actor = actor;
        m_lifetimeOwner = lifetimeOwner;
        m_movedRoots = movedRoots ?? throw new ArgumentNullException(nameof(movedRoots));
        m_sceneTriangles = sceneTriangles ?? throw new ArgumentNullException(nameof(sceneTriangles));
        m_staticObstacles = staticObstacles ?? throw new ArgumentNullException(nameof(staticObstacles));
        m_parentTransform = parentTransform;
        Name = $"AuthoredPath-{plan.MotionObjectId}-{plan.Path.Name}";
        m_initialRootTransforms = m_movedRoots.Select(root => root.GlobalTransform).ToArray();

        var movedObjectIds = m_movedRoots
            .Select(root => root.GetMeta("mechrewired_object_id", -1).AsInt32())
            .Where(id => id >= 0)
            .ToHashSet();
        m_triangleIndices = sceneTriangles
            .Select((triangle, index) => (triangle, index))
            .Where(item => item.triangle.SourceResourcePath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) &&
                           movedObjectIds.Contains(item.triangle.ObjectId))
            .Select(item => item.index)
            .ToArray();
        m_obstacleSlots = obstacleSlots
            .Distinct()
            .Where(index => index >= 0 && index < staticObstacles.Count)
            .Select(index => (index, staticObstacles[index]))
            .ToArray();

        // OBJ is the assembly's authored placement; PTBL supplies its subsequent local motion.
        // Keeping these separate is important for components that begin at a different PTBL point.
        m_motionAnchor = ToGodotTransform(plan.MotionObject.Transform);
        m_initialPathTransform = GetPathTransform(0, 0.0f);
        m_currentTransform = m_motionAnchor;
        if (m_actor != null)
        {
            m_actor.SetMotionAnchor(m_motionAnchor);
        }

        ApplyTransform(m_initialPathTransform);
        if (m_lifetimeOwner != null)
        {
            m_lifetimeOwner.Destroyed += OnLifetimeOwnerDestroyed;
        }
    }

    public override void _ExitTree()
    {
        if (m_lifetimeOwner != null)
        {
            m_lifetimeOwner.Destroyed -= OnLifetimeOwnerDestroyed;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (m_stoppedByDestruction ||
            m_plan.Path.Points.Count < 2 ||
            m_plan.Playback == MechWarriorWorldPathPlayback.OneShot &&
            m_segmentIndex == m_plan.Path.Points.Count - 1)
        {
            return;
        }

        var remainingDelta = (float)delta;
        while (remainingDelta > 0.0f)
        {
            var duration = GetSegmentDuration(m_segmentIndex);
            var step = Math.Min(remainingDelta, duration - m_segmentElapsed);
            m_segmentElapsed += step;
            remainingDelta -= step;
            ApplyTransform(GetPathTransform(m_segmentIndex, m_segmentElapsed));
            if (m_segmentElapsed + 0.0001f < duration)
            {
                break;
            }

            AdvanceSegment();
            m_segmentElapsed = 0.0f;
            if (m_plan.Playback == MechWarriorWorldPathPlayback.OneShot &&
                m_segmentIndex == m_plan.Path.Points.Count - 1)
            {
                break;
            }
        }
    }

    private void OnLifetimeOwnerDestroyed(BattlefieldActor owner, Vector3 hitPosition)
    {
        _ = hitPosition;
        if (m_stoppedByDestruction)
        {
            return;
        }

        m_stoppedByDestruction = true;
        SetPhysicsProcess(false);
        var attachedSounds = GetChildren().OfType<AudioStreamPlayer3D>().ToArray();
        foreach (var audio in attachedSounds)
        {
            audio.Stop();
        }

        GD.Print(
            $"MechRewired: stopped authored path '{m_plan.Path.Name}' and {attachedSounds.Length} attached sound(s) " +
            "with destroyed " +
            $"{owner.Description} object {owner.Definition.ObjectId}.");
    }

    private void AdvanceSegment()
    {
        m_segmentIndex++;
        if (m_segmentIndex < m_plan.Path.Points.Count - 1)
        {
            return;
        }

        if (m_segmentIndex == m_plan.Path.Points.Count - 1)
        {
            m_segmentIndex = m_plan.Playback switch
            {
                MechWarriorWorldPathPlayback.Repeat => 0,
                MechWarriorWorldPathPlayback.Loop => m_segmentIndex,
                _ => m_segmentIndex
            };
            return;
        }

        m_segmentIndex = m_plan.Playback switch
        {
            MechWarriorWorldPathPlayback.Loop => 0,
            _ => m_plan.Path.Points.Count - 1
        };
    }

    private float GetSegmentDuration(int segmentIndex)
    {
        var pointIndex = segmentIndex == m_plan.Path.Points.Count - 1
            ? m_plan.Path.Points.Count - 1
            : segmentIndex;
        return Math.Max(
            m_plan.Path.Points[pointIndex].TravelTicks / MechanicalPathTicksPerSecond,
            0.001f);
    }

    private Transform3D GetPathTransform(int segmentIndex, float elapsed)
    {
        var points = m_plan.Path.Points;
        var closingSegment = segmentIndex == points.Count - 1;
        var from = points[segmentIndex];
        var to = closingSegment ? points[0] : points[segmentIndex + 1];
        var duration = GetSegmentDuration(segmentIndex);
        var weight = Mathf.Clamp(elapsed / duration, 0.0f, 1.0f);
        // Generic BWD machinery uses each PTBL point as a hard motion key.  The Hermite
        // interpolation used by aircraft invents tangents across the repeated dwell points in
        // trnmove*, which makes the tower pivot overshoot far beyond its authored endpoints.
        var sourceDelta = to.Position - from.Position;
        var position = MechWarriorCoordinateSystem.ToGodotPosition(
            from.Position + sourceDelta * weight);
        var velocity = MechWarriorCoordinateSystem.ToGodotPosition(sourceDelta) / duration;

        var basis = !m_plan.RotateWithPath
            ? ToGodotTransform(from).Basis.Slerp(ToGodotTransform(to).Basis, weight)
            : GetPathFacing(velocity, from, to);
        return m_parentTransform * new Transform3D(basis, position);
    }

    private static Basis GetPathFacing(Vector3 velocity, MechWarriorWorldPathPoint from, MechWarriorWorldPathPoint to)
    {
        var direction = velocity;
        direction.Y = 0.0f;
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = MechWarriorCoordinateSystem.ToGodotPosition(to.Position) -
                        MechWarriorCoordinateSystem.ToGodotPosition(from.Position);
            direction.Y = 0.0f;
        }

        if (direction.LengthSquared() < 0.0001f)
        {
            direction = ModelForward;
        }

        direction = direction.Normalized();
        var localZAxis = -direction;
        var localXAxis = Vector3.Up.Cross(localZAxis).Normalized();
        return new Basis(localXAxis, Vector3.Up, localZAxis).Orthonormalized();
    }

    private void ApplyTransform(Transform3D transform)
    {
        GlobalTransform = transform;
        if (m_actor != null)
        {
            var delta = transform * m_currentTransform.AffineInverse();
            m_actor.ApplyMotionTransform(transform);
            ApplyTriangles(delta);
        }
        else
        {
            ApplyStaticTransform(transform);
        }

        m_currentTransform = transform;
    }

    private void ApplyStaticTransform(Transform3D transform)
    {
        var delta = transform * m_motionAnchor.AffineInverse();
        for (var index = 0; index < m_movedRoots.Count; index++)
        {
            m_movedRoots[index].GlobalTransform = delta * m_initialRootTransforms[index];
        }

        ApplyTriangles(delta);
        foreach (var (index, original) in m_obstacleSlots)
        {
            m_staticObstacles[index] = TransformObstacle(original, delta);
        }
    }

    private void ApplyTriangles(Transform3D delta)
    {
        foreach (var triangleIndex in m_triangleIndices)
        {
            var triangle = m_sceneTriangles[triangleIndex];
            m_sceneTriangles[triangleIndex] = triangle with
            {
                A = delta * triangle.A,
                B = delta * triangle.B,
                C = delta * triangle.C
            };
        }
    }

    private static SceneryObstacle TransformObstacle(SceneryObstacle obstacle, Transform3D transform)
    {
        var walls = obstacle.Walls.Select(wall => new SceneryWallTriangle(
            ToWallPoint(transform, wall.A),
            ToWallPoint(transform, wall.B),
            ToWallPoint(transform, wall.C))).ToArray();
        var points = walls.SelectMany(wall => new[] { wall.A, wall.B, wall.C }).ToArray();
        return points.Length == 0
            ? obstacle
            : new SceneryObstacle(
                obstacle.Name,
                new System.Numerics.Vector2(points.Min(point => point.X), points.Min(point => point.Y)),
                new System.Numerics.Vector2(points.Max(point => point.X), points.Max(point => point.Y)),
                walls);
    }

    private static System.Numerics.Vector2 ToWallPoint(Transform3D transform, System.Numerics.Vector2 point)
    {
        var moved = transform * new Vector3(point.X, 0.0f, point.Y);
        return new System.Numerics.Vector2(moved.X, moved.Z);
    }

    private static Transform3D ToGodotTransform(MechWarriorWorldTransform transform) => new(
        Basis.FromEuler(MechWarriorCoordinateSystem.ToGodotRotation(transform.RotationDegrees) *
                        (Mathf.Pi / 180.0f)).Scaled(MechWarriorCoordinateSystem.ToGodotScale(transform.Scale)),
        MechWarriorCoordinateSystem.ToGodotPosition(transform.Translation));

    private static Transform3D ToGodotTransform(MechWarriorWorldPathPoint point) => new(
        Basis.FromEuler(MechWarriorCoordinateSystem.ToGodotRotation(point.RotationDegrees) *
                        (Mathf.Pi / 180.0f)),
        MechWarriorCoordinateSystem.ToGodotPosition(point.Position));
}
