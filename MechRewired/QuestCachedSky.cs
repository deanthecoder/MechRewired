// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header.

using Godot;

namespace MechRewired;

/// <summary>
/// Captures Sky3D's stationary atmosphere and clouds for Quest, while keeping its radiance
/// cubemap in the original sky shader and drawing the visible sun from a baked patch.
/// </summary>
public static class QuestCachedSky
{
    public const string CacheVersion = "hdr-2048x1024-sun-colour256-analytic-disc-v3";
    private const string TimeExpression = "float current_time = TIME;";
    private const string SunExpression = "sun_disk *= sun_disk_intensity;";
    private const string SkyExpression = "col = render_sky(world_pos, clouds_pos, sun_pos, moon_pos, current_time);";
    private const string DiskExpression = "calc_disk_mask(world_pos, sun_pos, sun_disk_size)";
    private const int SunPatchSize = 256;

    public static async Task<Sky> CreateAsync(Node owner, Godot.Environment environment, Sky original)
    {
        if (original.SkyMaterial is not ShaderMaterial source || source.Shader == null)
            throw new InvalidOperationException("The mission sky does not have a Sky3D shader material.");

        using var snapshot = (ShaderMaterial)source.Duplicate();
        // Shader.Code assigned to a new in-memory resource has no include base path.
        // Inline the shipped include, preserving its code and copyright, before compilation.
        var common = GD.Load<ShaderInclude>("res://addons/sky_3d/shaders/Common.gdshaderinc");
        if (common == null) throw new InvalidOperationException("Sky3D common shader include is missing.");
        var code = snapshot.Shader.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("#include \"Common.gdshaderinc\"", common.Code, StringComparison.Ordinal);
        if (!code.Contains(TimeExpression, StringComparison.Ordinal) ||
            !code.Contains(SunExpression, StringComparison.Ordinal) ||
            !code.Contains(SkyExpression, StringComparison.Ordinal) ||
            !code.Contains(DiskExpression, StringComparison.Ordinal))
            throw new InvalidOperationException("Sky3D's shader changed; the Quest cache cannot safely transform it.");

        // TIME forces a complete radiance update every frame, even with cloud drift stopped.
        // All captures share one frozen value, so atmosphere, clouds and sun colour agree.
        var frozenTime = (float)(Time.GetTicksMsec() / 1000.0);
        var frozenCode = code.Replace(
            "shader_type sky;", "shader_type sky;\nuniform float quest_frozen_time;",
            StringComparison.Ordinal).Replace(
            TimeExpression, "float current_time = quest_frozen_time;", StringComparison.Ordinal);

        var sunLight = owner.FindChildren("SunLight", "DirectionalLight3D", true, false)
            .OfType<DirectionalLight3D>().FirstOrDefault(light => light.IsVisibleInTree());
        if (sunLight == null)
            throw new InvalidOperationException("The mission sky has no visible SunLight.");

        var sunDirection = sunLight.GlobalBasis.Z.Normalized();
        var horizonOffset = snapshot.GetShaderParameter("horizon_offset").AsSingle();
        var sunDiskSize = snapshot.GetShaderParameter("sun_disk_size").AsSingle();
        var diskCenter = sunDirection + Vector3.Up * horizonOffset;
        var centerDirection = diskCenter.Normalized();
        var referenceUp = Mathf.Abs(centerDirection.Dot(Vector3.Up)) > 0.95f
            ? Vector3.Forward
            : Vector3.Up;
        var patchRight = centerDirection.Cross(referenceUp).Normalized();
        var patchUp = patchRight.Cross(centerDirection).Normalized();
        var patchExtent = Mathf.Max(0.005f, Mathf.Max(sunDiskSize * 2.5f,
            (sunDiskSize + Mathf.Abs(1.0f - diskCenter.Length())) * 2.0f));

        using var captureMaterial = CopyMaterial(snapshot, frozenCode);
        captureMaterial.SetShaderParameter("quest_frozen_time", frozenTime);
        captureMaterial.SetShaderParameter("sun_disk_intensity", 0.0f);
        using var captureSky = new Sky
        {
            SkyMaterial = captureMaterial,
            ProcessMode = Sky.ProcessModeEnum.Quality,
            RadianceSize = Sky.RadianceSizeEnum.Size1024
        };

        var captureCode = frozenCode.Replace(
            "shader_type sky;", "shader_type sky;\n" +
            "uniform vec3 quest_sun_patch_center;\n" +
            "uniform vec3 quest_sun_patch_right;\n" +
            "uniform vec3 quest_sun_patch_up;\n" +
            "uniform float quest_sun_patch_extent;", StringComparison.Ordinal);
        captureCode = captureCode.Replace(
            "float p_time) {", "float p_time, float quest_sun_strength) {", StringComparison.Ordinal);
        captureCode = captureCode.Replace(
            SunExpression, "sun_disk *= sun_disk_intensity * quest_sun_strength;", StringComparison.Ordinal);
        // Cache smooth colour/attenuation across the patch. The visible pass retains the
        // original analytic disc boundary, avoiding a magnified low-resolution texture edge.
        captureCode = captureCode.Replace(DiskExpression, "1.0", StringComparison.Ordinal);
        captureCode = captureCode.Replace(SkyExpression,
            "vec2 quest_patch_uv = vec2(1.0 - SKY_COORDS.x, SKY_COORDS.y);\n" +
            "        vec2 quest_patch_offset = (quest_patch_uv - vec2(0.5)) * 2.0;\n" +
            "        quest_patch_offset.y = -quest_patch_offset.y;\n" +
            "        vec3 quest_patch_direction = normalize(quest_sun_patch_center +\n" +
            "            quest_sun_patch_right * quest_patch_offset.x * quest_sun_patch_extent +\n" +
            "            quest_sun_patch_up * quest_patch_offset.y * quest_sun_patch_extent);\n" +
            "        col = render_sky(quest_patch_direction, quest_patch_direction, sun_pos, moon_pos, current_time, 1.0)\n" +
            "            - render_sky(quest_patch_direction, quest_patch_direction, sun_pos, moon_pos, current_time, 0.0);",
            StringComparison.Ordinal);
        captureCode = captureCode.Replace(
            "\t// Draw overlay grids\n\t{", "\t// Draw overlay grids\n\tif (false) {",
            StringComparison.Ordinal);
        using var patchMaterial = CopyMaterial(snapshot, captureCode);
        patchMaterial.SetShaderParameter("quest_frozen_time", frozenTime);
        patchMaterial.SetShaderParameter("quest_sun_patch_center", centerDirection);
        patchMaterial.SetShaderParameter("quest_sun_patch_right", patchRight);
        patchMaterial.SetShaderParameter("quest_sun_patch_up", patchUp);
        patchMaterial.SetShaderParameter("quest_sun_patch_extent", patchExtent);
        using var patchSky = new Sky
        {
            SkyMaterial = patchMaterial,
            ProcessMode = Sky.ProcessModeEnum.Quality,
            RadianceSize = Sky.RadianceSizeEnum.Size256
        };

        // This viewport has its own world, so the mission's Environment and lighting remain
        // untouched while the panorama is rendered. The copied directional lights supply the
        // same LIGHT0/1_DIRECTION inputs used by Sky3D's sun and moon calculations.
        var viewport = new SubViewport
        {
            Name = "QuestSkyCapture",
            Size = new Vector2I(16, 16),
            OwnWorld3D = true,
            ProcessMode = Node.ProcessModeEnum.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        using var captureEnvironment = (Godot.Environment)environment.Duplicate();
        try
        {
            owner.AddChild(viewport);
            captureEnvironment.Sky = captureSky;
            viewport.AddChild(new WorldEnvironment { Environment = captureEnvironment });
            viewport.AddChild(new Camera3D { Current = true });
            CopyDirectionalLights(owner, viewport);

            // A ProcessFrame alone does not mean the render thread has drawn this sky. Give the
            // isolated viewport two actual draws before reading the sky RID's radiance map.
            await RenderingServer.Singleton.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await RenderingServer.Singleton.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
                throw new OperationCanceledException("The mission ended during Quest sky capture.");

            using var panorama = RenderingServer.SkyBakePanorama(
                captureSky.GetRid(), 1.0f, false, new Vector2I(2048, 1024));
            if (panorama == null || panorama.IsEmpty())
                throw new InvalidOperationException("The renderer returned an empty Quest sky panorama.");

            GD.Print($"QUEST_SKY_CACHE capture={panorama.GetWidth()}x{panorama.GetHeight()} " +
                     $"format={panorama.GetFormat()} source_radiance={captureSky.RadianceSize} " +
                     $"source_mode={captureSky.ProcessMode} original_radiance={original.RadianceSize} " +
                     $"original_mode={original.ProcessMode} complete=true");
            var panoramaTexture = ImageTexture.CreateFromImage(panorama);

            captureEnvironment.Sky = patchSky;
            await RenderingServer.Singleton.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await RenderingServer.Singleton.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
                throw new OperationCanceledException("The mission ended during Quest sun capture.");
            using var sunPatch = RenderingServer.SkyBakePanorama(
                patchSky.GetRid(), 1.0f, false, new Vector2I(SunPatchSize, SunPatchSize));
            if (sunPatch == null || sunPatch.IsEmpty())
                throw new InvalidOperationException("The renderer returned an empty Quest sun patch.");

            GD.Print($"QUEST_SKY_CACHE sun_patch={sunPatch.GetWidth()}x{sunPatch.GetHeight()} " +
                     $"format={sunPatch.GetFormat()} extent={patchExtent:F6} " +
                     $"source_radiance={patchSky.RadianceSize} source_mode={patchSky.ProcessMode} " +
                     $"original_radiance={original.RadianceSize} original_mode={original.ProcessMode} complete=true");
            var texture = ImageTexture.CreateFromImage(sunPatch);

            // Keep procedural cubemap lighting, but only texture lookups and a cheap disc mask
            // in the visible pass. The sun patch includes cloud attenuation and colour response.
            var finalCode = frozenCode.Replace(
                "shader_type sky;",
                "shader_type sky;\n" +
                "uniform sampler2D quest_panorama: filter_linear, repeat_enable;\n" +
                "uniform sampler2D quest_sun_patch_texture: filter_linear, repeat_disable;\n" +
                "uniform vec3 quest_sun_patch_center;\n" +
                "uniform vec3 quest_sun_patch_right;\n" +
                "uniform vec3 quest_sun_patch_up;\n" +
                "uniform float quest_sun_patch_extent;",
                StringComparison.Ordinal);
            finalCode = finalCode.Replace(
                "float p_time) {", "float p_time, float quest_sun_strength) {", StringComparison.Ordinal);
            finalCode = finalCode.Replace(
                SunExpression, "sun_disk *= sun_disk_intensity * quest_sun_strength;", StringComparison.Ordinal);
#if DEBUG
            var referenceFinalCode = finalCode.Replace(
                SkyExpression,
                "if (AT_CUBEMAP_PASS) {\n" +
                "            col = render_sky(world_pos, clouds_pos, sun_pos, moon_pos, current_time, 1.0);\n" +
                "        } else {\n" +
                // SkyBakePanorama writes -sin(azimuth) on X; SKY_COORDS uses atan(X, -Z).
                // Reverse longitude so the captured atmosphere/halo shares the live disc's direction.
                "            col = texture(quest_panorama, vec2(1.0 - SKY_COORDS.x, SKY_COORDS.y)).rgb;\n" +
                "            vec3 quest_disk_dir = world_pos;\n" +
                "            quest_disk_dir.y -= horizon_offset;\n" +
                "            if (calc_disk_mask(quest_disk_dir, sun_pos, sun_disk_size) > 0.0) {\n" +
                "                col += render_sky(world_pos, clouds_pos, sun_pos, moon_pos, current_time, 1.0)\n" +
                "                     - render_sky(world_pos, clouds_pos, sun_pos, moon_pos, current_time, 0.0);\n" +
                "            }\n" +
                "        }", StringComparison.Ordinal);
#endif
            finalCode = finalCode.Replace(SkyExpression,
                "if (AT_CUBEMAP_PASS) {\n" +
                "            col = render_sky(world_pos, clouds_pos, sun_pos, moon_pos, current_time, 1.0);\n" +
                "        } else {\n" +
                "            col = texture(quest_panorama, vec2(1.0 - SKY_COORDS.x, SKY_COORDS.y)).rgb;\n" +
                "            vec3 quest_disk_dir = world_pos - vec3(0.0, horizon_offset, 0.0);\n" +
                "            if (calc_disk_mask(quest_disk_dir, sun_pos, sun_disk_size) > 0.0) {\n" +
                "            float quest_patch_forward = dot(world_pos, quest_sun_patch_center);\n" +
                "            vec2 quest_patch_uv = vec2(\n" +
                "                dot(world_pos, quest_sun_patch_right),\n" +
                "                -dot(world_pos, quest_sun_patch_up)) / max(quest_patch_forward, 0.000001) /\n" +
                "                (2.0 * quest_sun_patch_extent) + vec2(0.5);\n" +
                "                col += texture(quest_sun_patch_texture, quest_patch_uv).rgb;\n" +
                "            }\n" +
                "        }", StringComparison.Ordinal);
            // The panorama already contains Sky3D's optional diagnostic grids.
            finalCode = finalCode.Replace(
                "\t// Draw overlay grids\n\t{", "\t// Draw overlay grids\n\tif (AT_CUBEMAP_PASS) {",
                StringComparison.Ordinal);

            var finalMaterial = CopyMaterial(snapshot, finalCode);
            finalMaterial.SetShaderParameter("quest_frozen_time", frozenTime);
            finalMaterial.SetShaderParameter("quest_panorama", panoramaTexture);
            finalMaterial.SetShaderParameter("quest_sun_patch_texture", texture);
            finalMaterial.SetShaderParameter("quest_sun_patch_center", centerDirection);
            finalMaterial.SetShaderParameter("quest_sun_patch_right", patchRight);
            finalMaterial.SetShaderParameter("quest_sun_patch_up", patchUp);
            finalMaterial.SetShaderParameter("quest_sun_patch_extent", patchExtent);
#if DEBUG
            finalMaterial.SetMeta("quest_reference_shader", new Shader { Code = referenceFinalCode.Replace(
                "\t// Draw overlay grids\n\t{", "\t// Draw overlay grids\n\tif (AT_CUBEMAP_PASS) {", StringComparison.Ordinal) });
#endif
            var result = new Sky
            {
                SkyMaterial = finalMaterial,
                RadianceSize = original.RadianceSize,
                ProcessMode = original.ProcessMode
            };
            GD.Print($"QUEST_SKY_CACHE version={CacheVersion} final_radiance={result.RadianceSize} " +
                     $"final_mode={result.ProcessMode} sun=LIGHT0_DIRECTION");
            return result;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(viewport))
            {
                if (viewport.GetParent() != null)
                    viewport.GetParent().RemoveChild(viewport);
                viewport.QueueFree();
            }
        }
    }

    private static ShaderMaterial CopyMaterial(ShaderMaterial source, string code)
    {
        var copy = new ShaderMaterial { Shader = new Shader { Code = code } };
        foreach (var uniform in source.Shader.GetShaderUniformList())
        {
            var name = uniform.AsGodotDictionary()["name"].AsStringName();
            var value = source.GetShaderParameter(name);
            if (value.VariantType != Variant.Type.Nil)
                copy.SetShaderParameter(name, value);
        }
        return copy;
    }

    private static void CopyDirectionalLights(Node owner, SubViewport viewport)
    {
        foreach (var child in owner.FindChildren("*", "DirectionalLight3D", true, false))
        {
            if (child is not DirectionalLight3D source || !source.IsVisibleInTree() ||
                !source.IsInsideTree())
                continue;
            var light = new DirectionalLight3D
            {
                Name = source.Name,
                LightColor = source.LightColor,
                LightEnergy = source.LightEnergy,
                LightAngularDistance = source.LightAngularDistance,
                SkyMode = source.SkyMode,
                Rotation = source.GlobalBasis.GetEuler(),
                ShadowEnabled = false
            };
            viewport.AddChild(light);
            GD.Print($"QUEST_SKY_CACHE light={source.Name} direction={source.GlobalBasis.Z} " +
                     $"sky_mode={source.SkyMode}");
        }
    }
}
