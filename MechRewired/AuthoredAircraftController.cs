// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>Runs an archive-authored aircraft path and its attached presentation tasks.</summary>
public partial class AuthoredAircraftController : Node3D
{
    // V_BHELOA's nose points down its local -X axis; its long +X extent is the tail boom.
    private static readonly Vector3 ModelForward = Vector3.Left;

    private readonly BattlefieldActor m_actor;
    private readonly IReadOnlyList<MechWarriorWorldPathPoint> m_points;
    private readonly bool m_rotateWithPath;
    private readonly IList<DebugTriangle> m_sceneTriangles;
    private readonly int[] m_triangleIndices;
    private readonly AudioStreamPlayer3D m_engine;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private int m_segmentIndex;
    private float m_segmentElapsed;
    private bool m_destroyed;
    private Vector3 m_flightVelocity;

    public AuthoredAircraftController(
        BattlefieldActor actor,
        MechWarriorWorldTransform motionAnchor,
        IReadOnlyList<MechWarriorWorldPathPoint> points,
        bool rotateWithPath,
        IList<DebugTriangle> sceneTriangles,
        Node3D rotor,
        AudioStreamWav engineSound,
        float maximumSoundDistance,
        BattlefieldEffects battlefieldEffects)
    {
        m_actor = actor ?? throw new ArgumentNullException(nameof(actor));
        ArgumentNullException.ThrowIfNull(motionAnchor);
        m_points = points ?? throw new ArgumentNullException(nameof(points));
        if (m_points.Count == 0)
        {
            throw new ArgumentException("An aircraft path must contain at least one point.", nameof(points));
        }

        m_rotateWithPath = rotateWithPath;
        m_sceneTriangles = sceneTriangles ?? throw new ArgumentNullException(nameof(sceneTriangles));
        ConfigureRotorBlur(rotor ?? throw new ArgumentNullException(nameof(rotor)));
        m_battlefieldEffects = battlefieldEffects ?? throw new ArgumentNullException(nameof(battlefieldEffects));
        Name = $"AuthoredFlight-{actor.SourceResourceName}";
        var componentKeys = actor.Definition.Components
            .Select(component => (component.SourceEntry.Path, component.Id))
            .ToHashSet();
        m_triangleIndices = sceneTriangles
            .Select((triangle, index) => (triangle, index))
            .Where(item => componentKeys.Contains((item.triangle.SourceResourcePath, item.triangle.ObjectId)))
            .Select(item => item.index)
            .ToArray();

        actor.SuppressGenericExplosionDebris();
        actor.SetMotionAnchor(ToGodotTransform(motionAnchor));
        ApplyTransform(m_points.Count > 1
            ? GetSegmentTransform(0, 0.0f)
            : ToGodotTransform(m_points[0]));
        actor.Destroyed += OnActorDestroyed;

        if (engineSound != null)
        {
            m_engine = new AudioStreamPlayer3D
            {
                Name = "AuthoredEngine",
                Stream = engineSound,
                UnitSize = 30.0f,
                MaxDistance = Math.Max(maximumSoundDistance, 1.0f),
                VolumeDb = -3.0f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
            };
            AddChild(m_engine);
        }
    }

    public override void _Ready() => m_engine?.Play();

    public override void _PhysicsProcess(double delta)
    {
        if (m_destroyed)
        {
            return;
        }

        AdvancePath((float)delta);
    }

    public override void _ExitTree() => m_actor.Destroyed -= OnActorDestroyed;

    private void AdvancePath(float delta)
    {
        while (delta > 0.0f && m_segmentIndex < m_points.Count - 1)
        {
            var duration = Math.Max(m_points[m_segmentIndex].TravelSeconds, 0.001f);
            var remaining = duration - m_segmentElapsed;
            var step = Math.Min(delta, remaining);
            m_segmentElapsed += step;
            delta -= step;
            var sample = MechWarriorWorldPathInterpolator.Sample(
                m_points,
                m_segmentIndex,
                m_segmentElapsed);
            m_flightVelocity = MechWarriorCoordinateSystem.ToGodotPosition(sample.Velocity);
            ApplyTransform(GetSegmentTransform(m_segmentIndex, m_segmentElapsed, sample));
            if (m_segmentElapsed + 0.0001f < duration)
            {
                break;
            }

            m_segmentIndex++;
            m_segmentElapsed = 0.0f;
            if (m_segmentIndex == m_points.Count - 1)
            {
                m_flightVelocity = Vector3.Zero;
            }
        }
    }

    private void ApplyTransform(Transform3D transform)
    {
        var delta = transform * m_actor.GlobalTransform.AffineInverse();
        m_actor.GlobalTransform = transform;
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

    private void OnActorDestroyed(BattlefieldActor actor, Vector3 hitPosition)
    {
        m_destroyed = true;
        m_engine?.Stop();
        var wreckageBounds = actor.WorldBounds;
        if (wreckageBounds.HasVolume())
        {
            actor.GlobalPosition += actor.DestructionBounds.GetCenter() - wreckageBounds.GetCenter();
        }

        var physicalWreckage = AircraftWreckage.TrySpawn(actor, m_flightVelocity, hitPosition);
        if (physicalWreckage == null)
        {
            GD.PushWarning(
                $"MechRewired: {actor.Description} has no rendered destroyed assembly for physical wreckage.");
            return;
        }

        m_battlefieldEffects.FollowDestruction(actor, physicalWreckage);
    }

    private Transform3D GetSegmentTransform(
        int segmentIndex,
        float segmentElapsed,
        MechWarriorWorldPathSample sample = null)
    {
        sample ??= MechWarriorWorldPathInterpolator.Sample(m_points, segmentIndex, segmentElapsed);
        var position = MechWarriorCoordinateSystem.ToGodotPosition(sample.Position);
        if (!m_rotateWithPath)
        {
            var duration = Math.Max(m_points[segmentIndex].TravelSeconds, 0.001f);
            var from = ToGodotTransform(m_points[segmentIndex]);
            var to = ToGodotTransform(m_points[segmentIndex + 1]);
            return new Transform3D(
                from.Basis.Slerp(to.Basis, Mathf.Clamp(segmentElapsed / duration, 0.0f, 1.0f)),
                position);
        }

        var direction = MechWarriorCoordinateSystem.ToGodotPosition(sample.Velocity);
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = GetPathDirection(segmentElapsed <= 0.0f
                ? segmentIndex
                : segmentIndex + 1);
        }

        direction.Y = 0.0f;
        if (direction.LengthSquared() < 0.0001f)
        {
            return new Transform3D(Basis.Identity, position);
        }

        direction = direction.Normalized();
        var up = Vector3.Up;
        var localXAxis = -direction;
        var across = localXAxis.Cross(up).Normalized();
        return new Transform3D(
            new Basis(localXAxis, up, across).Orthonormalized(),
            position);
    }

    private Vector3 GetPathDirection(int pointIndex)
    {
        if (pointIndex < m_points.Count - 1)
        {
            return MechWarriorCoordinateSystem.ToGodotPosition(m_points[pointIndex + 1].Position) -
                   MechWarriorCoordinateSystem.ToGodotPosition(m_points[pointIndex].Position);
        }

        return pointIndex > 0
            ? MechWarriorCoordinateSystem.ToGodotPosition(m_points[pointIndex].Position) -
              MechWarriorCoordinateSystem.ToGodotPosition(m_points[pointIndex - 1].Position)
            : ModelForward;
    }

    private static void ConfigureRotorBlur(Node3D root)
    {
        var bounds = new Aabb();
        var hasBounds = false;
        foreach (var meshInstance in root.GetChildren().OfType<MeshInstance3D>())
        {
            var meshBounds = meshInstance.Transform * meshInstance.GetAabb();
            bounds = hasBounds ? bounds.Merge(meshBounds) : meshBounds;
            hasBounds = true;
            meshInstance.Visible = false;
            meshInstance.RemoveFromGroup(DebugCamera.SolidMeshGroup);
            meshInstance.RemoveFromGroup(DebugCamera.WireframeMeshGroup);
        }

        if (!hasBounds)
        {
            throw new InvalidDataException($"Aircraft rotor {root.Name} contains no mesh geometry.");
        }

        var diameter = Math.Max(bounds.Size.X, bounds.Size.Z);
        var blur = new MeshInstance3D
        {
            Name = "RotorMotionBlur",
            Position = bounds.GetCenter(),
            RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new QuadMesh
            {
                Size = new Vector2(diameter, diameter),
                Material = CreateRotorBlurMaterial()
            }
        };
        root.AddChild(blur);
        blur.AddToGroup(DebugCamera.SolidMeshGroup);
    }

    private static ShaderMaterial CreateRotorBlurMaterial() => new()
    {
        Shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode blend_mix, cull_disabled, unshaded;

                void fragment() {
                    vec2 offset = UV * 2.0 - 1.0;
                    float radius = length(offset);
                    if (radius > 1.0) {
                        discard;
                    }

                    float angle = atan(offset.y, offset.x);
                    float rotating_lobes = 0.5 + 0.5 * cos(angle * 7.0 - TIME * 34.0 + radius * 9.0);
                    float blade_streaks = pow(rotating_lobes, 7.0);
                    float outer_fade = 1.0 - smoothstep(0.84, 1.0, radius);
                    float hub_fade = smoothstep(0.10, 0.30, radius);
                    float radial_texture = 0.78 + 0.22 * cos(radius * 58.0 - TIME * 8.0);

                    vec3 dark_blade = vec3(0.075, 0.085, 0.090);
                    vec3 highlight = vec3(0.30, 0.33, 0.34);
                    ALBEDO = mix(dark_blade, highlight, blade_streaks * 0.55);
                    ALPHA = (0.075 + blade_streaks * 0.14) * outer_fade * hub_fade * radial_texture;
                }
                """
        }
    };

    private static Transform3D ToGodotTransform(MechWarriorWorldPathPoint point)
    {
        var rotation = MechWarriorCoordinateSystem.ToGodotRotation(point.RotationDegrees);
        return new Transform3D(
            Basis.FromEuler(rotation * (Mathf.Pi / 180.0f)),
            MechWarriorCoordinateSystem.ToGodotPosition(point.Position));
    }

    private static Transform3D ToGodotTransform(MechWarriorWorldTransform transform)
    {
        var rotation = MechWarriorCoordinateSystem.ToGodotRotation(transform.RotationDegrees);
        return new Transform3D(
            Basis.FromEuler(rotation * (Mathf.Pi / 180.0f)).Scaled(
                MechWarriorCoordinateSystem.ToGodotScale(transform.Scale)),
            MechWarriorCoordinateSystem.ToGodotPosition(transform.Translation));
    }
}
