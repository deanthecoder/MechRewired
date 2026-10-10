# Code authored by Dean Edis (DeanTheCoder).
# Anyone is free to copy, modify, use, compile, or distribute this software,
# either in source code form or as a compiled binary, for any purpose.

extends SceneTree

# Render an asymmetric sky marker, bake it, then verify its restored direction.
# Run with Godot's Mobile renderer; headless/dummy rendering cannot check this.
func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var viewport := SubViewport.new()
	viewport.size = Vector2i(128, 128)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	var shader := Shader.new()
	shader.code = """
shader_type sky;
void sky() {
    vec3 marker = normalize(vec3(0.6, 0.3, -0.7));
    COLOR = vec3(pow(max(dot(EYEDIR, marker), 0.0), 64.0));
}
"""
	var material := ShaderMaterial.new()
	material.shader = shader
	var sky := Sky.new()
	sky.sky_material = material
	sky.radiance_size = Sky.RADIANCE_SIZE_256
	sky.process_mode = Sky.PROCESS_MODE_QUALITY
	var environment := Environment.new()
	environment.background_mode = Environment.BG_SKY
	environment.sky = sky
	var world_environment := WorldEnvironment.new()
	world_environment.environment = environment
	viewport.add_child(world_environment)
	var camera := Camera3D.new()
	viewport.add_child(camera)
	camera.current = true
	camera.look_at(Vector3(0.6, 0.3, -0.7))
	await drawn_frames(4)
	var reference := viewport.get_texture().get_image().get_pixel(64, 64).r
	var panorama := RenderingServer.sky_bake_panorama(sky.get_rid(), 1.0, false, Vector2i(1024, 512))
	var texture := ImageTexture.create_from_image(panorama)
	var sample_shader := Shader.new()
	sample_shader.code = """
shader_type sky;
uniform sampler2D panorama: filter_linear, repeat_enable;
uniform bool correct_longitude;
void sky() {
    vec2 uv = SKY_COORDS;
    if (correct_longitude) { uv.x = 1.0 - uv.x; }
    COLOR = texture(panorama, uv).rgb;
}
"""
	var sample_material := ShaderMaterial.new()
	sample_material.shader = sample_shader
	sample_material.set_shader_parameter("panorama", texture)
	sky.sky_material = sample_material
	sample_material.set_shader_parameter("correct_longitude", false)
	await drawn_frames(4)
	var mirrored := viewport.get_texture().get_image().get_pixel(64, 64).r
	sample_material.set_shader_parameter("correct_longitude", true)
	await drawn_frames(4)
	var corrected := viewport.get_texture().get_image().get_pixel(64, 64).r
	print("QUEST_SKY_PANORAMA_CHECK reference=%f mirrored=%f corrected=%f" % [reference, mirrored, corrected])
	if reference < 0.5 or corrected < reference * 0.9 or mirrored > reference * 0.2:
		push_error("Sky panorama longitude did not round-trip to the procedural marker.")
		quit(1)
		return
	print("QUEST_SKY_PANORAMA_PASS")
	quit()

func drawn_frames(count: int) -> void:
	for index in count:
		await RenderingServer.frame_post_draw
