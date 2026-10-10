// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>
/// Switches only cockpit mesh resources and materials. The original scene, moving cockpit
/// transform, glazing and emissive instruments remain intact. No world-space lightmap is used.
/// </summary>
internal sealed class QuestCockpitMaterials
{
    private const string AssetRoot = "res://Assets/Models/Cockpit/Quest/";
    private readonly List<Entry> m_entries = [];
    private sealed record Entry(MeshInstance3D Instance, Mesh OriginalMesh, Godot.Material OriginalOverride,
        Godot.Material OriginalSurface, Mesh UvMesh, StandardMaterial3D UvMaterial);

    public StandardMaterial3D FrameMaterial { get; private set; }

    public QuestCockpitMaterials(Node3D model)
    {
        var resource = GD.Load<PackedScene>(AssetRoot + "quest-cockpit.glb")
            ?? throw new InvalidOperationException("Quest cockpit bake is missing. Reimport its assets before running.");
        var baked = resource.Instantiate<Node3D>();
        try
        {
            foreach (var child in baked.FindChildren("*", "MeshInstance3D", true, false))
            {
                var replacement = (MeshInstance3D)child;
                var name = replacement.Name.ToString();
                var original = model.FindChild(name, true, false) as MeshInstance3D;
                if (original == null || original.Mesh.GetSurfaceCount() != 1 ||
                    replacement.Mesh.GetSurfaceCount() != 1 ||
                    !original.Transform.IsEqualApprox(replacement.Transform))
                    throw new InvalidOperationException($"Quest cockpit bake no longer matches source mesh {name}.");
                if (name != "CockpitFrame" &&
                    replacement.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil)
                    throw new InvalidOperationException($"Quest cockpit bake is missing the interior UV2 atlas on {name}.");

                var source = (name == "CockpitFrame" ? original.GetSurfaceOverrideMaterial(0) : original.MaterialOverride) ??
                    original.GetSurfaceOverrideMaterial(0) ??
                    original.Mesh.SurfaceGetMaterial(0);
                if (source is not StandardMaterial3D standard)
                    throw new InvalidOperationException($"Quest cockpit bake requires a standard source material for {name}.");

                var material = (StandardMaterial3D)standard.Duplicate();
                material.ResourceName = name + "QuestUV";
                if (name == "CockpitFrame")
                {
                    material.AlbedoColor = Colors.White; // Source colour is included in the atlas.
                    material.AlbedoTexture = Texture(name, "albedo");
                    material.Uv1Triplanar = false;
                    material.Uv1WorldTriplanar = false;
                    material.Uv1Scale = Vector3.One;
                    material.Uv1Offset = Vector3.Zero;
                    material.NormalTexture = Texture(name, "normal");
                    material.NormalScale = 1.0f;
                }
                // Armor and fittings already use cheap UV materials. Preserve their full
                // texture detail on UV1 and use a separate UV2 atlas only for the lamp bake.
                material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                material.EmissionEnabled = false;
                material.Emission = Colors.White;
                material.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
                material.EmissionTexture = Texture(name, "interior-strength1");
                material.EmissionOnUV2 = name != "CockpitFrame";
                material.EmissionEnergyMultiplier = 1.0f;
                m_entries.Add(new Entry(original, original.Mesh, original.MaterialOverride,
                    original.GetSurfaceOverrideMaterial(0), replacement.Mesh, material));
                if (name == "CockpitFrame") FrameMaterial = material;
            }
            if (FrameMaterial == null || !m_entries.Any(e => e.Instance.Name == "CockpitArmor"))
                throw new InvalidOperationException("Quest cockpit bake is missing its frame or armor.");
        }
        finally
        {
            baked.Free();
        }
    }

    private static Texture2D Texture(string mesh, string suffix) =>
        GD.Load<Texture2D>($"{AssetRoot}{mesh}-{suffix}.png") ??
        throw new InvalidOperationException($"Quest cockpit texture is missing: {mesh}-{suffix}.png");

    public void Apply(bool enabled)
    {
        foreach (var entry in m_entries)
        {
            entry.Instance.Mesh = enabled ? entry.UvMesh : entry.OriginalMesh;
            entry.Instance.SetSurfaceOverrideMaterial(0, enabled ? null : entry.OriginalSurface);
            entry.Instance.MaterialOverride = enabled ? entry.UvMaterial : entry.OriginalOverride;
        }
    }

    public void SetFrameProperties(float metallic, float roughness)
    {
        FrameMaterial.Metallic = metallic;
        FrameMaterial.Roughness = roughness;
    }

    public void SetInteriorLighting(bool enabled, float strength)
    {
        // This bake records the complete fixed-lamp contribution at the normal strength of 1.
        // Scaling it is an approximation to the original lamps' individual baseline/lift curves.
        foreach (var entry in m_entries)
        {
            entry.UvMaterial.EmissionEnabled = enabled;
            entry.UvMaterial.EmissionEnergyMultiplier = strength;
        }
    }
}
