"""Bake the expensive cockpit materials and local lamp lift into Quest-ready assets.

Run from the repository root:
  & 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background \
      --python scripts/bake-quest-cockpit.py

The source .blend and cockpit.glb are read only.  This script writes only
Assets/Models/Cockpit/Quest.  It deliberately has no sun, world lighting, or
shadowing in the lamp pass: Godot still supplies the moving exterior sun.
"""

import bpy
import os


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SOURCE = os.path.join(ROOT, "MechRewired", "Assets", "Models", "Cockpit", "cockpit.glb")
OUT = os.path.join(ROOT, "MechRewired", "Assets", "Models", "Cockpit", "Quest")
TEXTURES = os.path.join(ROOT, "MechRewired", "Assets", "Textures", "Cockpit")
ATLAS_UV = "QuestUV"

# These are deliberately the exact data used in PlayerCockpit.LoadCockpitModel.
LAMPS = (
    ("PortRailLamp", (-0.30, -0.016, -0.42), (1.00, 0.40, 0.12), 0.035, 0.025, 0.45),
    ("RearCanopyLamp", (0.00, 0.24, 0.70), (1.00, 0.68, 0.40), 0.065, 0.035, 1.60),
    ("PhaseModuleGlow", (0.285, -0.168, -0.0555), (0.38, 0.73, 1.00), 0.016, 0.024, 0.38),
    ("CabinBounce", (0.00, 0.10, 0.10), (0.72, 0.80, 1.00), 0.000, 0.080, 1.40),
)

FRAME = "CockpitFrame"
ARMOR = "CockpitArmor"
SKIP = {"CockpitGlass", "CockpitInstrumentGlass", "CockpitAmber", "CockpitCoreGlow"}


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.materials, bpy.data.images, bpy.data.meshes):
        # Leave Blender-owned startup data alone; this only prevents a stale background session.
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def image_path(*parts):
    return os.path.join(TEXTURES, *parts)


def load_image(path):
    return bpy.data.images.load(path, check_existing=True)


def srgb_to_linear(value):
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def new_image(name, size, path, colorspace="sRGB"):
    image = bpy.data.images.new(name, width=size, height=size, alpha=False, float_buffer=False)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.colorspace_settings.name = colorspace
    return image


def metallic_input(principled):
    """Blender 4/5 renamed this socket differently across point releases."""
    return principled.inputs.get("Metallic") or principled.inputs["Metallic IOR Level"]


def tex(nodes, image, vector):
    node = nodes.new("ShaderNodeTexImage")
    node.image = image
    node.extension = "REPEAT"
    nodes.id_data.links.new(vector, node.inputs["Vector"])
    return node


def triplanar_color(nodes, links, image, scale=1.5):
    """Return a Blender colour socket matching Godot's object-local triplanar colour."""
    # glTF import maps Godot (x, y, z) to Blender (x, -z, y).  Use metres, not
    # Generated coordinates (which normalise every mesh's bounding box), so this
    # reproduces the original 1.5 repetitions/metre material scale.
    geometry = nodes.new("ShaderNodeNewGeometry")
    position = nodes.new("ShaderNodeSeparateXYZ")
    links.new(geometry.outputs["Position"], position.inputs[0])
    godot_position = nodes.new("ShaderNodeCombineXYZ")
    links.new(position.outputs["X"], godot_position.inputs[0])
    links.new(position.outputs["Z"], godot_position.inputs[1])
    links.new(position.outputs["Y"], godot_position.inputs[2])
    separate_position = nodes.new("ShaderNodeSeparateXYZ")
    links.new(godot_position.outputs[0], separate_position.inputs[0])
    separate_normal = nodes.new("ShaderNodeSeparateXYZ")
    godot_normal = nodes.new("ShaderNodeCombineXYZ")
    blender_normal = nodes.new("ShaderNodeSeparateXYZ")
    links.new(geometry.outputs["Normal"], blender_normal.inputs[0])
    links.new(blender_normal.outputs["X"], godot_normal.inputs[0])
    links.new(blender_normal.outputs["Z"], godot_normal.inputs[1])
    links.new(blender_normal.outputs["Y"], godot_normal.inputs[2])
    links.new(godot_normal.outputs[0], separate_normal.inputs[0])

    # Godot samples XY*z + XZ*y + ZY*(-1,1)*x after flipping its position Y.
    # Converting Godot Z=-Blender Y and the image's top/bottom UV convention
    # leaves Blender projections (Y,Z), (X,Y), (X,Z), all positive.
    projections = (("Z", "Y", 1.0), ("X", "Z", 1.0), ("X", "Y", 1.0))
    samples = []
    weights = []
    for index, (u, v, u_sign) in enumerate(projections):
        combine = nodes.new("ShaderNodeCombineXYZ")
        multiply_u = nodes.new("ShaderNodeMath")
        multiply_u.operation = "MULTIPLY"
        multiply_u.inputs[1].default_value = scale * u_sign
        multiply_v = nodes.new("ShaderNodeMath")
        multiply_v.operation = "MULTIPLY"
        multiply_v.inputs[1].default_value = scale
        links.new(separate_position.outputs[u], multiply_u.inputs[0])
        links.new(separate_position.outputs[v], multiply_v.inputs[0])
        links.new(multiply_u.outputs[0], combine.inputs[0])
        links.new(multiply_v.outputs[0], combine.inputs[1])
        samples.append(tex(nodes, image, combine.outputs[0]).outputs["Color"])

        absolute = nodes.new("ShaderNodeMath")
        absolute.operation = "ABSOLUTE"
        power = nodes.new("ShaderNodeMath")
        power.operation = "POWER"
        power.inputs[1].default_value = 12.0
        links.new(separate_normal.outputs[("X", "Y", "Z")[index]], absolute.inputs[0])
        links.new(absolute.outputs[0], power.inputs[0])
        weights.append(power.outputs[0])

    weighted = []
    for sample, weight in zip(samples, weights):
        multiply = nodes.new("ShaderNodeMixRGB")
        multiply.blend_type = "MULTIPLY"
        multiply.inputs[0].default_value = 1.0
        links.new(sample, multiply.inputs[1])
        links.new(weight, multiply.inputs[2])
        weighted.append(multiply.outputs[0])
    total = nodes.new("ShaderNodeMath")
    total.operation = "ADD"
    links.new(weights[0], total.inputs[0])
    links.new(weights[1], total.inputs[1])
    total2 = nodes.new("ShaderNodeMath")
    total2.operation = "ADD"
    links.new(total.outputs[0], total2.inputs[0])
    links.new(weights[2], total2.inputs[1])
    # Normalise the weights.  The old shader graph added them directly, creating
    # dark seams wherever more than one projection contributed.
    normalised = []
    for item in weighted:
        divide = nodes.new("ShaderNodeMixRGB")
        divide.blend_type = "DIVIDE"
        divide.inputs[0].default_value = 1.0
        links.new(item, divide.inputs[1])
        links.new(total2.outputs[0], divide.inputs[2])
        normalised.append(divide.outputs[0])
    add = nodes.new("ShaderNodeMixRGB")
    add.blend_type = "ADD"
    add.inputs[0].default_value = 1.0
    links.new(normalised[0], add.inputs[1])
    links.new(normalised[1], add.inputs[2])
    add2 = nodes.new("ShaderNodeMixRGB")
    add2.blend_type = "ADD"
    add2.inputs[0].default_value = 1.0
    links.new(add.outputs[0], add2.inputs[1])
    links.new(normalised[2], add2.inputs[2])
    return add2.outputs[0]


def frame_triplanar_normal(nodes, links, image):
    """Rebuild Godot's triplanar TBN before Cycles writes a tangent-space atlas."""
    sampled = triplanar_color(nodes, links, image)
    rgb = nodes.new("ShaderNodeSeparateColor")
    rgb.mode = "RGB"
    links.new(sampled, rgb.inputs[0])
    decoded = []
    for channel in (rgb.outputs["Red"], rgb.outputs["Green"]):
        value = nodes.new("ShaderNodeMath")
        value.operation = "MULTIPLY_ADD"
        value.inputs[1].default_value = 2.0
        value.inputs[2].default_value = -1.0
        links.new(channel, value.inputs[0])
        decoded.append(value.outputs[0])
    geometry = nodes.new("ShaderNodeNewGeometry")
    normal = nodes.new("ShaderNodeSeparateXYZ")
    links.new(geometry.outputs["Normal"], normal.inputs[0])
    absolute = []
    for socket in (normal.outputs["X"], normal.outputs["Y"], normal.outputs["Z"]):
        value = nodes.new("ShaderNodeMath")
        value.operation = "ABSOLUTE"
        links.new(socket, value.inputs[0])
        absolute.append(value.outputs[0])
    # Exact Blender-coordinate form of Godot's generated triplanar tangent/binormal.
    tangent_xy = []
    for first, second in ((absolute[2], absolute[1]), (absolute[0], None)):
        value = nodes.new("ShaderNodeMath")
        value.operation = "ADD"
        value.inputs[1].default_value = 0.0
        links.new(first, value.inputs[0])
        if second:
            links.new(second, value.inputs[1])
        tangent_xy.append(value.outputs[0])
    tangent = nodes.new("ShaderNodeCombineXYZ")
    links.new(tangent_xy[0], tangent.inputs[0])
    links.new(tangent_xy[1], tangent.inputs[1])
    tangent_normalized = nodes.new("ShaderNodeVectorMath")
    tangent_normalized.operation = "NORMALIZE"
    links.new(tangent.outputs[0], tangent_normalized.inputs[0])
    binormal = nodes.new("ShaderNodeCombineXYZ")
    links.new(absolute[2], binormal.inputs[1])
    binormal_z = nodes.new("ShaderNodeMath")
    binormal_z.operation = "ADD"
    links.new(absolute[0], binormal_z.inputs[0])
    links.new(absolute[1], binormal_z.inputs[1])
    links.new(binormal_z.outputs[0], binormal.inputs[2])
    binormal_normalized = nodes.new("ShaderNodeVectorMath")
    binormal_normalized.operation = "NORMALIZE"
    links.new(binormal.outputs[0], binormal_normalized.inputs[0])
    scaled_t = nodes.new("ShaderNodeVectorMath")
    scaled_t.operation = "SCALE"
    links.new(tangent_normalized.outputs[0], scaled_t.inputs[0])
    links.new(decoded[0], scaled_t.inputs[3])
    scaled_b = nodes.new("ShaderNodeVectorMath")
    scaled_b.operation = "SCALE"
    links.new(binormal_normalized.outputs[0], scaled_b.inputs[0])
    links.new(decoded[1], scaled_b.inputs[3])
    r2 = nodes.new("ShaderNodeMath"); r2.operation = "MULTIPLY"
    links.new(decoded[0], r2.inputs[0]); links.new(decoded[0], r2.inputs[1])
    g2 = nodes.new("ShaderNodeMath"); g2.operation = "MULTIPLY"
    links.new(decoded[1], g2.inputs[0]); links.new(decoded[1], g2.inputs[1])
    remainder = nodes.new("ShaderNodeMath"); remainder.operation = "SUBTRACT"; remainder.inputs[0].default_value = 1.0
    sum_sq = nodes.new("ShaderNodeMath"); sum_sq.operation = "ADD"
    links.new(r2.outputs[0], sum_sq.inputs[0]); links.new(g2.outputs[0], sum_sq.inputs[1])
    links.new(sum_sq.outputs[0], remainder.inputs[1])
    positive = nodes.new("ShaderNodeMath"); positive.operation = "MAXIMUM"; positive.inputs[1].default_value = 0.0
    links.new(remainder.outputs[0], positive.inputs[0])
    z = nodes.new("ShaderNodeMath"); z.operation = "SQRT"; links.new(positive.outputs[0], z.inputs[0])
    scaled_n = nodes.new("ShaderNodeVectorMath"); scaled_n.operation = "SCALE"
    links.new(geometry.outputs["Normal"], scaled_n.inputs[0]); links.new(z.outputs[0], scaled_n.inputs[3])
    add = nodes.new("ShaderNodeVectorMath"); add.operation = "ADD"
    links.new(scaled_t.outputs[0], add.inputs[0]); links.new(scaled_b.outputs[0], add.inputs[1])
    add2 = nodes.new("ShaderNodeVectorMath"); add2.operation = "ADD"
    links.new(add.outputs[0], add2.inputs[0]); links.new(scaled_n.outputs[0], add2.inputs[1])
    result = nodes.new("ShaderNodeVectorMath"); result.operation = "NORMALIZE"
    links.new(add2.outputs[0], result.inputs[0])
    return result.outputs[0]


def frame_or_armor_material(obj):
    material = bpy.data.materials.new(f"QuestSource_{obj.name}")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    principled = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    if obj.name == FRAME:
        color = load_image(image_path("Metal029", "Metal029_1K-PNG_Color.png"))
        links.new(triplanar_color(nodes, links, color), principled.inputs["Base Color"])
        normal_image = load_image(image_path("Metal029", "Metal029_1K-PNG_NormalGL.png"))
        normal_image.colorspace_settings.name = "Non-Color"
        links.new(frame_triplanar_normal(nodes, links, normal_image), principled.inputs["Normal"])
        metallic_input(principled).default_value = 0.75
        principled.inputs["Roughness"].default_value = 0.60
    else:
        color = load_image(image_path("MetalPlates013", "MetalPlates013_1K-PNG_Color.png"))
        source_uv = nodes.new("ShaderNodeUVMap")
        source_uv.uv_map = "SourceUV"
        color_node = tex(nodes, color, source_uv.outputs["UV"])
        tint = nodes.new("ShaderNodeMixRGB")
        tint.blend_type = "MULTIPLY"
        tint.inputs[0].default_value = 1.0
        tint.inputs[2].default_value = (*(srgb_to_linear(c) for c in (0.58, 0.55, 0.52)), 1.0)
        links.new(color_node.outputs["Color"], tint.inputs[1])
        links.new(tint.outputs[0], principled.inputs["Base Color"])
        armor_normal = load_image(image_path("MetalPlates013", "MetalPlates013_1K-PNG_NormalGL.png"))
        armor_normal.colorspace_settings.name = "Non-Color"
        armor_normal_node = tex(nodes, armor_normal, source_uv.outputs["UV"])
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.uv_map = "SourceUV"
        normal_map.inputs["Strength"].default_value = 0.72
        links.new(armor_normal_node.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
        metallic_input(principled).default_value = 0.72
        principled.inputs["Roughness"].default_value = 0.88
    return material


def source_material(obj):
    if obj.name in (FRAME, ARMOR):
        return frame_or_armor_material(obj)
    if obj.data.materials and obj.data.materials[0]:
        material = obj.data.materials[0].copy()
        material.name = f"QuestSource_{obj.name}"
        material.use_nodes = True
        return material
    material = bpy.data.materials.new(f"QuestSource_{obj.name}")
    material.use_nodes = True
    return material


def unwrap(obj):
    # The armor has authored UVs for MetalPlates013.  Preserve them for the source
    # material while its separate QuestUV layer becomes glTF TEXCOORD_0 on export.
    original = obj.data.uv_layers.active
    if original is not None:
        original.name = "SourceUV"
    quest = obj.data.uv_layers.get(ATLAS_UV) or obj.data.uv_layers.new(name=ATLAS_UV)
    obj.data.uv_layers.active = quest
    quest.active_render = True
    # glTF import selects every object. Avoid entering multi-object edit mode and
    # accidentally repacking the other meshes' original UV maps.
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    # At this density a moderate island margin removes mip bleeding without wasting the atlas.
    size = 2048 if obj.name == FRAME else 1024 if obj.name == ARMOR else 512
    bpy.ops.uv.smart_project(angle_limit=1.1519, island_margin=4.0 / size,
                            margin_method="FRACTION", area_weight=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    layer = obj.data.uv_layers.active
    layer.name = ATLAS_UV
    obj.select_set(False)


def bake_target(material, image):
    nodes = material.node_tree.nodes
    node = nodes.new("ShaderNodeTexImage")
    node.name = "QuestBakeTarget"
    node.label = "Quest bake target"
    node.image = image
    for other in nodes:
        other.select = False
    node.select = True
    nodes.active = node
    return node


def bake(obj, material, image, bake_type, *, direct=False):
    target = bake_target(material, image)
    obj.data.materials.clear()
    obj.data.materials.append(material)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.context.scene.render.bake.use_clear = True
    bpy.context.scene.render.bake.margin = 8
    bpy.context.scene.render.bake.use_selected_to_active = False
    if bake_type == "DIFFUSE":
        bpy.context.scene.render.bake.use_pass_direct = direct
        bpy.context.scene.render.bake.use_pass_indirect = False
        # The emission overlay is added directly by Godot, so include the source
        # material colour in the lamp pass rather than exporting bare irradiance.
        bpy.context.scene.render.bake.use_pass_color = True
    bpy.ops.object.bake(type=bake_type)
    obj.select_set(False)
    image.save()
    material.node_tree.nodes.remove(target)


def bake_godot_interior(obj, albedo, target):
    """Bake Godot's finite OmniLight contribution without Blender's watt conversion.

    Godot Forward+ uses ``max(1 - (distance / range)^4, 0)^2 / distance`` for
    the default OmniLight decay.  Baking that expression into an emission shader
    makes a texel's value deterministic and includes source albedo, which is the
    correct input for StandardMaterial3D's additive emission texture.
    """
    material = bpy.data.materials.new(f"QuestLampBake_{obj.name}")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    uv = nodes.new("ShaderNodeUVMap")
    uv.uv_map = ATLAS_UV
    base = tex(nodes, albedo, uv.outputs["UV"])
    geometry = nodes.new("ShaderNodeNewGeometry")
    total = None
    metallic_response = 1.0 - float(obj.data.materials[0].get("quest_source_metallic", 0.0))
    for _, godot_position, color, baseline, lift, range_metres in LAMPS:
        # Godot (x,y,z) -> Blender (x,-z,y).
        lamp = nodes.new("ShaderNodeCombineXYZ")
        lamp.inputs[0].default_value = godot_position[0]
        lamp.inputs[1].default_value = -godot_position[2]
        lamp.inputs[2].default_value = godot_position[1]
        direction = nodes.new("ShaderNodeVectorMath")
        direction.operation = "SUBTRACT"
        links.new(lamp.outputs[0], direction.inputs[0])
        links.new(geometry.outputs["Position"], direction.inputs[1])
        distance = nodes.new("ShaderNodeVectorMath")
        distance.operation = "LENGTH"
        links.new(direction.outputs[0], distance.inputs[0])
        unit_direction = nodes.new("ShaderNodeVectorMath")
        unit_direction.operation = "NORMALIZE"
        links.new(direction.outputs[0], unit_direction.inputs[0])
        ndotl = nodes.new("ShaderNodeVectorMath")
        ndotl.operation = "DOT_PRODUCT"
        links.new(geometry.outputs["Normal"], ndotl.inputs[0])
        links.new(unit_direction.outputs[0], ndotl.inputs[1])
        positive_ndotl = nodes.new("ShaderNodeMath")
        positive_ndotl.operation = "MAXIMUM"
        positive_ndotl.inputs[1].default_value = 0.0
        links.new(ndotl.outputs[1], positive_ndotl.inputs[0])
        ratio = nodes.new("ShaderNodeMath")
        ratio.operation = "DIVIDE"
        ratio.inputs[1].default_value = range_metres
        links.new(distance.outputs[1], ratio.inputs[0])
        power4 = nodes.new("ShaderNodeMath")
        power4.operation = "POWER"
        power4.inputs[1].default_value = 4.0
        links.new(ratio.outputs[0], power4.inputs[0])
        one_minus = nodes.new("ShaderNodeMath")
        one_minus.operation = "SUBTRACT"
        one_minus.inputs[0].default_value = 1.0
        links.new(power4.outputs[0], one_minus.inputs[1])
        cutoff = nodes.new("ShaderNodeMath")
        cutoff.operation = "MAXIMUM"
        cutoff.inputs[1].default_value = 0.0
        links.new(one_minus.outputs[0], cutoff.inputs[0])
        squared = nodes.new("ShaderNodeMath")
        squared.operation = "MULTIPLY"
        links.new(cutoff.outputs[0], squared.inputs[0])
        links.new(cutoff.outputs[0], squared.inputs[1])
        inverse_distance = nodes.new("ShaderNodeMath")
        inverse_distance.operation = "DIVIDE"
        inverse_distance.inputs[0].default_value = 1.0
        safe_distance = nodes.new("ShaderNodeMath")
        safe_distance.operation = "MAXIMUM"
        safe_distance.inputs[1].default_value = 0.0001
        links.new(distance.outputs[1], safe_distance.inputs[0])
        links.new(safe_distance.outputs[0], inverse_distance.inputs[1])
        strength = nodes.new("ShaderNodeMath")
        strength.operation = "MULTIPLY"
        links.new(squared.outputs[0], strength.inputs[0])
        links.new(inverse_distance.outputs[0], strength.inputs[1])
        lambert = nodes.new("ShaderNodeMath")
        lambert.operation = "MULTIPLY"
        links.new(strength.outputs[0], lambert.inputs[0])
        links.new(positive_ndotl.outputs[0], lambert.inputs[1])
        lamp_colour = nodes.new("ShaderNodeRGB")
        # C# Color literals are authored sRGB-like values; Godot shades in linear space.
        lamp_colour.outputs[0].default_value = (*(srgb_to_linear(component) for component in color), 1.0)
        energy = nodes.new("ShaderNodeMixRGB")
        energy.blend_type = "MULTIPLY"
        energy.inputs[0].default_value = 1.0
        lamp_energy = (baseline + lift) * metallic_response
        energy.inputs[2].default_value = (lamp_energy, lamp_energy, lamp_energy, 1.0)
        links.new(lamp_colour.outputs[0], energy.inputs[1])
        contribution = nodes.new("ShaderNodeMixRGB")
        contribution.blend_type = "MULTIPLY"
        contribution.inputs[0].default_value = 1.0
        links.new(energy.outputs[0], contribution.inputs[1])
        links.new(lambert.outputs[0], contribution.inputs[2])
        if total is None:
            total = contribution.outputs[0]
        else:
            add = nodes.new("ShaderNodeMixRGB")
            add.blend_type = "ADD"
            add.inputs[0].default_value = 1.0
            links.new(total, add.inputs[1])
            links.new(contribution.outputs[0], add.inputs[2])
            total = add.outputs[0]
    colour = nodes.new("ShaderNodeMixRGB")
    colour.blend_type = "MULTIPLY"
    colour.inputs[0].default_value = 1.0
    links.new(base.outputs["Color"], colour.inputs[1])
    links.new(total, colour.inputs[2])
    links.new(colour.outputs[0], emission.inputs["Color"])
    links.new(emission.outputs[0], output.inputs["Surface"])
    bake(obj, material, target, "EMIT")
    bpy.data.materials.remove(material)


def bake_object(obj):
    size = 2048 if obj.name == FRAME else 1024 if obj.name == ARMOR else 512
    unwrap(obj)
    material = source_material(obj)
    source_principled = next((node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    material["quest_source_metallic"] = (
        metallic_input(source_principled).default_value if source_principled else 0.0)
    # Keep the node name verbatim: PlayerCockpit matches assets by mesh name.
    safe = obj.name
    albedo = new_image(f"{obj.name}-albedo", size, os.path.join(OUT, f"{safe}-albedo.png"))
    normal = new_image(f"{obj.name}-normal", size, os.path.join(OUT, f"{safe}-normal.png"), "Non-Color")
    lamp = new_image(f"{obj.name}-interior-strength1", size,
                     os.path.join(OUT, f"{safe}-interior-strength1.png"))
    # Cycles' diffuse-colour pass applies (1 - metallic).  We need the raw
    # albedo atlas because Godot restores the original metallic response at runtime.
    principled = source_principled
    original_metallic = metallic_input(principled).default_value if principled else None
    if principled:
        metallic_input(principled).default_value = 0.0
    bake(obj, material, albedo, "DIFFUSE", direct=False)
    if principled:
        metallic_input(principled).default_value = original_metallic
    bake(obj, material, normal, "NORMAL")

    # The exported material is deliberately simple: Godot can use it as a direct fallback,
    # while PlayerCockpit installs its own material using the three documented atlas files.
    nodes, links = material.node_tree.nodes, material.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    color_node = tex(nodes, albedo, nodes.new("ShaderNodeTexCoord").outputs["UV"])
    normal_node = tex(nodes, normal, nodes.new("ShaderNodeTexCoord").outputs["UV"])
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.uv_map = ATLAS_UV
    links.new(color_node.outputs["Color"], principled.inputs["Base Color"])
    links.new(normal_node.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    metallic_input(principled).default_value = 0.65 if obj.name == ARMOR else 0.75
    principled.inputs["Roughness"].default_value = 0.75 if obj.name == ARMOR else 0.60
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    material.name = f"QuestBaked_{obj.name}"


def export_glb(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.name == FRAME:
            # Only the frame replaces triplanar with a UV0 material atlas.
            for layer in list(obj.data.uv_layers):
                if layer.name != ATLAS_UV:
                    obj.data.uv_layers.remove(layer)
            obj.data.uv_layers.active = obj.data.uv_layers[ATLAS_UV]
        else:
            # Existing UV materials keep their original high-frequency detail.
            # SourceUV remains first, while the interior-only bake uses UV2.
            obj.data.uv_layers.active = obj.data.uv_layers["SourceUV"]
        obj.data.uv_layers.active.active_render = True
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(OUT, "quest-cockpit.glb"),
        export_format="GLB",
        use_selection=True,
        export_materials="NONE",
        export_texcoords=True,
        export_normals=True,
        export_tangents=True,
        export_yup=True,
    )


def main():
    os.makedirs(OUT, exist_ok=True)
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    opaque = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.name not in SKIP]
    if not opaque or FRAME not in {o.name for o in opaque} or ARMOR not in {o.name for o in opaque}:
        raise RuntimeError("Source cockpit did not contain the required opaque CockpitFrame and CockpitArmor meshes.")
    scene = bpy.context.scene
    # Cycles is required for all bake targets in Blender 5.2.  The albedo/normal passes are
    # still cheap because the world and lamps are absent at this point.
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    for obj in opaque:
        bake_object(obj)
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    # Re-bake only the authored Godot local-light contribution.  This avoids a
    # Blender-unit conversion and intentionally excludes sun and world lighting.
    for obj in opaque:
        lamp_image = bpy.data.images[f"{obj.name}-interior-strength1"]
        albedo = bpy.data.images[f"{obj.name}-albedo"]
        baked_material = obj.data.materials[0]
        bake_godot_interior(obj, albedo, lamp_image)
        obj.data.materials.clear()
        obj.data.materials.append(baked_material)
    export_glb(opaque)
    print(f"Quest cockpit bake complete: {len(opaque)} opaque meshes -> {OUT}")


if __name__ == "__main__":
    main()
