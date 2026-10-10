# Quest 3 export and setup

The Quest build is a seated-cockpit VR build. It starts the Wolf desert campaign
directly once compatible game data is present. If it is missing, VR shows a
setup panel with a native Android file picker to import your own `MW2.PRJ`;
the desktop drag-and-drop importer is not available in VR.
Use `-- --vr-preview` for a flat desktop preview, or
`--xr-mode on --rendering-method mobile -- --vr` for a PC OpenXR headset.

Mission startup builds the scene before starting the deployment voice and reactor
audio. Gameplay is paused until the first frame has rendered, so first-frame
resource uploads and shader preparation do not consume the announcement or
mission time. Headless checks use a process-frame boundary instead.
The terrain render and collision meshes, and initial rock-cell placements, are
calculated with at most two concurrent workers per stage; Godot resources and
scene nodes are still created on the main thread. Rock placement remains deterministic.

Startup logs prefixed `MISSION_STARTUP:` report mission parsing, world objects,
derived terrain, implicit ground/indexing, scenery/effects, player/audio, initial rocks, and
mission/HUD setup. The scene-assembled and first-frame timestamps are cumulative
from campaign selection; the other scene stages report their individual durations.
Compare those timestamps on the headset to distinguish CPU construction from
first-frame rendering costs. They exclude engine boot and opening the game archive.

## Release builds

On macOS, the repository script builds the signed release APKs with the .NET
edition of Godot 4.7.1, its matching Android export templates, JDK 17, and the
Android SDK. Godot's .NET exporter also needs
`MechRewired/MechRewired.sln` beside `project.godot`; the repository solution at
the parent level is for desktop builds and tests. The script installs the
matching Android build template when it is missing and preserves the authored
`project.godot` settings.

Run these commands from the repository root:

```sh
scripts/quest.sh build    # Build the signed release APK for local Quest install.
scripts/quest.sh install  # Build and install it; stage local game data if present.
scripts/quest.sh share    # Build the signed Alpha APK for a separate Meta upload.
```

`build` writes `MechRewired/builds/MechRewired-Quest3.apk`.
`share` writes `MechRewired/builds/MechRewired-Alpha.apk`. It only creates the
APK; publish it separately to the private Meta Alpha channel to update testers.
`install` installs the Quest3 release APK on the connected headset. It does not
upload or change a Meta channel or tester build.

The script uses Meta Quest Developer Hub's bundled ADB when available and
Android SDK ADB otherwise. Connect the Quest over USB and accept its debugging
prompt for initial setup, then run `scripts/quest.sh connect` to enable Wi-Fi
ADB. The script remembers the headset address and reconnects automatically;
use `QUEST_HOST=<headset-ip> scripts/quest.sh install` if its IP changes.

The release signing key and password are shared across these builds and stored
outside the repository at
`~/Library/Application Support/MechRewired/signing/mechrewired-release.keystore`
and `~/Library/Application Support/MechRewired/signing/keystore-password`.
Back up both files securely and preserve this same key for future updates. Set
`QUEST_SIGNING_DIR` to use the same key from another machine or location. If an
installed app was signed with a different key, Android rejects the update. The
script does not uninstall or clear that app; removing it deletes its private
app data, including an imported `MW2.PRJ`, so migration requires backing up that
data and explicit approval first.

## Install and import game data

By default, APKs built by `build`, `install`, or `share` do not include original
game data. For private Alpha testing with your own local archive, explicitly opt
in when building the Alpha APK:

```sh
QUEST_INCLUDE_TEST_DATA=1 scripts/quest.sh share
```

The command includes `local/game-data/MW2.PRJ`, or the file selected with
`MW2_PRJ=/path/to/MW2.PRJ`, in that APK. Upload an opted-in APK only to the
private Meta Alpha channel for the intended testers. Never commit the archive
to Git or distribute that APK publicly or through a Production channel. `share`
only builds the APK; it does not upload it. See [GAME_DATA.md](GAME_DATA.md)
for compatible editions and licensing notes.

By default, `install` looks for `local/game-data/MW2.PRJ`; set
`MW2_PRJ=/path/to/MW2.PRJ` to use a different file. If found, the script copies
it to `Downloads/MechRewired/MW2.PRJ` on the headset and verifies its SHA-256.
This only stages the file in Downloads. In the headset, select **IMPORT
MW2.PRJ**, choose `Downloads/MechRewired/MW2.PRJ` in the Android file picker,
and confirm the import to copy it into the app's private storage. If the script
reports that no local file was copied, place your own `MW2.PRJ` in that
Downloads folder yourself, then import it from the headset. The app starts the
mission after a successful import. An opted-in private Alpha APK imports its
bundled archive on startup only when no private archive is already present, so
an existing imported archive is preserved.

Use `scripts/quest.sh data` to stage or verify the local file without rebuilding
or reinstalling. Matching staged data is left in place; a changed local file is
copied and checked. If no local archive exists, existing headset data is
preserved. Set `GODOT_BIN`, `ANDROID_HOME`, `JAVA_HOME`, and `ADB_BIN` to
override tool locations; use `ANDROID_SERIAL` to select one of multiple
headsets, or `QUEST_ADDRESS_FILE` to change the local address cache.

## Controls exposed to the VR rig

`openxr_action_map.tres` uses Godot's default OpenXR action set. The seated rig
can read the following names from its left or right controller tracker:

| Action | Value | Quest Touch control |
| --- | --- | --- |
| `primary` | `Vector2` | Thumbstick |
| `primary_click` | boolean | Thumbstick click |
| `trigger` / `trigger_click` | float / boolean | Index trigger |
| `ax_button` | boolean | A on the right controller, X on the left |
| `by_button` | boolean | B on the right controller, Y on the left |
| `menu_button` | boolean | Left controller Menu |
| `grip`, `grip_click`, `aim_pose`, `grip_pose` | float, boolean, poses | Grip and controller poses |

The seated mapping currently uses the left thumbstick for signed, persistent
throttle: 20–100% forward, through neutral, then reverse. Click it to stop;
return it to neutral before choosing another throttle. The right thumbstick
steers the mech left/right and pitches the torso up/down. Head direction aims
the weapons and moves the reticle. Automatic torso following is disabled after
a headset camera displacement regression.
Click the right stick to align the legs with your horizontal gaze bearing.
Press the right index trigger to fire the selected weapon once; release it below
45% before firing again (the firing threshold is 65%). Squeeze the right grip
to cycle weapons. Squeeze the left index trigger to select the next target;
hold the left grip for jump jets. A and B also cycle weapons and targets,
respectively. X inspects, Y recentres, and left Menu pauses.

## WIP scope and verification

Head aim uses headset orientation, not eye tracking. The existing reticle is
constrained to the projected main cockpit glazing and the HUD surface, with an
inset keeping its full shape away from the frame. Looking beyond that area holds
the aim at its boundary. Leaning is included in the projection. Direct weapons,
unguided missiles, target picking and the missile-lock cone share that same ray;
locked missiles retain their existing homing behaviour. Head aim does not rotate
the cockpit or the player's view. It works with cockpit glass rendering off too.
If the head pose has no valid projection into the window, the reticle and firing
are suppressed until a valid aim returns.
Benchmarks keep their scripted torso aim so head movement cannot redirect shots.
Desktop aiming is unchanged. Headset comfort and edge alignment still need testing.
The seated view is raised 12 cm and moved back 10 cm. The XR camera attaches
to the seat above the synthetic camera-bob node.
Headset focus/tracking loss opens the pause menu; loss of either controller stops
throttle, steering, leg alignment and jump jets. A tracked right controller can
still fire, cycle weapons/targets and pitch the torso if the left controller is
asleep or unavailable. Head tracking, focus and an unpaused seated view remain
required. Reacquiring the right controller requires releasing its fire trigger.
Returning from the menu requires releasing the fire trigger or jump-jet grip
before either action resumes.

The existing HUD, including the original chassis damage silhouette, is drawn
onto a transparent cockpit surface. Radar, weapons, status, navigation and
targeting can be toggled separately. Its finite-depth targeting presentation
still needs binocular alignment/readability testing on Quest.
The VR lower HUD places navigation/target information to the
left of Heat, dH/dT and Jets, with the player-damage silhouette and speed label to their
right so the cockpit frame does not obscure the outer panels. Desktop HUD
coordinates are unchanged. The head-following reticle and its shared weapon
aim ray have light smoothing; recentering or reacquiring a valid aim snaps to
the new point.

Automatic torso following is disabled. The earlier tracking-origin counter-rotation
moved a real, non-zero headset position away from the seat and could invalidate
the reticle projection. Right-stick up/down retains manual torso pitch; head
movement alone does not turn the torso. Startup waits for two focused, tracked
frames before seating the camera. Focus-loss pausing begins only after the first
focused seated session. `QUEST_VR_INPUT` logs input gate changes outside benchmarks
for diagnosing blocked startup controls.

Clicking the right stick captures the current horizontal reticle bearing and
turns the legs toward it at the mech's normal steering rate. The torso motor also
approaches that bearing within its physical twist limits, finishing centered over
the aligned legs. Torso pitch is retained, and the headset pose is never rewritten. Manual right-stick steering cancels alignment. Stop, shutdown and loss of
tracking cancel it too. This is separate from left-Y seat recentering.

Graphics controls cover shadows, combined baked sky/cabin, scene glow, cockpit
glass, terrain mapping and battlefield smoke/dust. **BAKED SKY + CABIN** enables
the cached 2048x1024 HDR sky together with UV cockpit materials and baked cabin
lighting. It replaces the three separate experimental controls, avoiding the
slower UV-only cockpit combination. Capture shows **BAKING** while pending.
Turning it off restores the procedural sky, cloud drift and original cockpit
lighting. Sunlight continues to illuminate the scene in both modes. The switch
starts on and lasts for the current mission only; desktop defaults are unchanged.
The replacement sky and cabin were visually accepted on Quest on 1 October,
The 3 October longitude correction aligns the cached halo with the separate sun;
headset confirmation of that correction is pending. Results are in
[QUEST_TESTS_2026-10-01.md](QUEST_TESTS_2026-10-01.md); the shorter combined-profile
test is described in [QUEST_BENCHMARK.md](QUEST_BENCHMARK.md).

The Quest baseline keeps weapon lights, smoke/dust and nearby object sun shadows
on. Scene glow, HUD glow and cockpit glass start off. Combat performance is evaluated with
the baked sky/cabin and these effects retained.

Terrain parallax is always off in VR, including desktop VR preview, and has no menu toggle.
The **TERRAIN TRIPLANAR** graphics toggle is off by default. Off selects a separate
UV shader with two colour-texture samples and constant roughness; it does not run
triplanar projection, normal-map, height-map or procedural-noise sampling.
Terrain meshes carry metre-scaled UVs, with a single projection chosen per face when the
mesh is built so cliff faces and sealing skirts do not have collapsed UVs. This
cheaper path has less surface detail and can show seams between projection planes.
On restores the detailed biome triplanar shader, while keeping VR parallax off.
Both modes use the existing terrain textures and retain the same geometry.

Quest desert missions use a 1.75x longer distance-fog range, retaining haze while
keeping the sun-facing deployment mountains from reaching opaque fog too early.
Terrain reflectivity is unchanged. Desktop and rocky-mountain fog ranges are unchanged.

Triplanar changes apply to the loaded terrain immediately and are saved in
`user://settings.cfg`, under `[quest_graphics]` as `terrain_triplanar`. The saved
choice is restored on mission restart and app launch; missing/invalid values use
Off. Desktop play retains its detailed triplanar default regardless of this saved
Quest setting. Desktop VR preview uses the Quest choice for verification.

VR rock scattering uses a 7x7 cell window with a 5x5 dense region (desktop uses
9x9 and 7x7). Candidate spacing is 1.5 times wider, giving approximately 56%
fewer placement candidates per cell as well as reducing the active cell count.
VR also omits the transparent rock contact-shadow patches. The terrain-coloured
ground-blend skirts remain to soften rock/ground seams.
HUD instrument drawing commands are cached between 30 Hz updates, while the
reticle, target frames and projected navigation indicator update every frame.
The HUD texture retains its 1280x720 resolution; this reduces CPU command
generation rather than the texture's per-frame GPU rendering cost.
HUD settings also have a separate HUD glow switch, disabled by default on Quest.
Missile trails, fire, weapon flashes and other combat feedback remain; the
smoke/dust toggle is not a master switch for every particle in the game.
SSAO, screen-space reflections and the unverified stereo lens-flare compositor
are disabled. Mobile does not create the localized volumetric ground fog.
Other graphics options currently last for the mission session. The menu displays the most
recent running FPS/frame interval, **not GPU time or a paused benchmark**.

## Quest HUD coverage

Quest/VR preview always displays the existing HUD texture through non-overlapping
40-pixel regions in one `MultiMesh`, instead of shading its entire transparent
rectangle. Coverage is recorded from the existing drawing commands, including
original damage artwork, text, strokes and optional glow. Instrument coverage
persists between redraws; targeting coverage follows the full-rate targeting layer.
Empty regions collapse to zero-area triangles. The original, non-rendered plane
remains the reference for head aiming and world-to-HUD projection.

This reduces stereo transparent fill while retaining the 1280x720 offscreen
texture and its existing update cadence. It does not eliminate offscreen rendering.
There is no additional graphics toggle. Desktop HUD rendering is unchanged.
Quest destination-panel text uses a 21px reference font (previously 25px), with
16px additional right-side fitting margin.

Run the native Debug check with original game data and a graphics renderer:

```text
godot --path MechRewired --rendering-method mobile --xr-mode off res://QuestHudCoverageCheck.tscn -- --vr-preview
```

The check verifies every nontransparent HUD pixel is covered in normal, glow,
full-screen radar and hidden-radar modes; compares rendered sparse and original
surfaces; and checks that removed content clears its coverage. On 6 October's
desktop Mobile-renderer run, normal coverage used 188/576 regions (32.6% of the
original area) and the surface comparison had zero channel differences above
2/255 tolerance. This is correctness/coverage evidence, not a Quest FPS measurement.

## Historical debug validation

The development rendering benchmark is documented in [QUEST_BENCHMARK.md](QUEST_BENCHMARK.md).
It provides scripted viewpoints, per-feature comparisons, raw frame timings and
logcat CSV reports without saving its temporary graphics changes.

In a debug build with original data installed, run Godot with
`-- --vr-preview --vr-smoke` to exercise synthetic XR controller input, latched
throttle/stop, steering, trigger/grip controls, menu input
suppression and mission-result
presentation. A graphics run also captures cockpit/menu previews into the
ignored `artifacts/` directory. This harness does not validate real tracking,
Android drivers, stereo comfort or headset performance.

On 27 September 2026, a debug APK was installed over Wi-Fi on a Quest 3.
The private `MW2.PRJ` checksum matched the local archive, and the headset log
confirmed OpenXR startup, 7,735 indexed resources, and the Wolf mission's Mad Dog
deployment. A headset screenshot showed the cockpit and desert terrain, paused
behind a Quest system overlay. Comfort, controller feel, and sustained performance
still need hands-on testing.


### Reactor and inspect controls

Left-stick click stops the mech while online. While shut down, it requests a reactor
restart and keeps the throttle stopped until the stick returns to neutral. The
existing critical-heat check still refuses restart while too hot; let the game run
to cool down (the pause menu pauses cooling). A held click does not repeat.

Left X inspects the selected actor or the active inspect objective when applicable.
The pause menu's **PILOT CONTROLS** page exposes **REACTOR POWER**, **HEAT OVERRIDE**,
and **INSPECT TARGET (X)**. Reactor power and inspect close the menu before acting.
Override retains the existing higher heat limit and cannot be enabled while offline.

### HUD panel seams

Sparse HUD coverage now uses one indexed mesh with shared positions and texture
coordinates along neighboring cell edges, replacing independently transformed
quads. It retains the original HUD artwork, material, gaze anchor and occupied
coverage area. Geometry updates only when the set of occupied cells changes.

The coverage check compares the rendered sparse surface with the original full
quad, both straight on and tilted with 2x MSAA. Native Mac Forward+ and Mobile
checks pass for normal, glow, fullscreen radar and hidden radar modes. The reported
Quest panel banding was not reproduced on the Mac; confirming its removal still
requires viewing the radar and navigation panels while moving the headset. The
mesh rebuild cost also needs the next Quest performance capture.

### Player grounding cost (8 October 2026)

Quest keeps the normal fixed physics rate and live movement, collision/slope checks,
jump-jet/landing logic, heat, damage and input. The invisible player leg rig advances
its gait clock for footfall events but does not write animated joint poses. It also
skips the per-toe-vertex terrain adjustment used to prevent visible feet clipping;
the chassis still follows the original terrain surface through movement and landing.
A pre-existing decorative foot-clearance offset settles smoothly back to zero.
This can slightly change chassis height on uneven ground; headset movement/landing
validation is still required. Desktop animation and enemy rigs are unchanged.

Successful player surface queries reuse height/slope only at exactly the same X/Z
on the immutable terrain index, while grounded with no jump request. Movement,
airborne/jump updates and terrain reconfiguration refresh/invalidate that cache;
failed queries are never cached. Timers and fuel continue advancing every tick.

The combat benchmark now runs legacy → optimized → legacy (45 seconds of combat
plus reload/report overhead). Baselines deliberately restore the old foot work;
normal Quest play and the restored mission use the optimization. Detailed timing
includes gait, ground clearance, jump updates, surface requests and actual surface
queries, so cache hits and the expensive substep can be distinguished in saved logs.

Quest cockpit stomp feedback uses a broadened distance-driven gait pulse with
exponential smoothing to move only the cockpit mesh down by up to 2.5 cm. Each
foot alternates a gentle sideways sway (up to 1 cm) and roll (up to 0.34 degrees).
Its strength follows gait weight and smoothly settles when stopping or airborne. The tracked camera,
aim, collision and cached grounding are unaffected; no new terrain queries or
physics simulation are added. Headset feel still needs validation.

The 10 October Quest chem-plant screenshot confirms absent cast shadows with the
performance baseline's sun-shadow setting disabled. SUN SHADOWS remains available
in the graphics menu. This cockpit-motion adjustment does not change that setting.

### Nearby object shadows (10 October 2026)

SUN SHADOWS now defaults on. The mobile directional atlas is 4,096 pixels (was
2,048), with a small PCF filter, two blended cascades split at 100 m, a 500 m
camera distance limit and fading from 400 to 500 m. Quest sun angular softness
is zero; this profile uses the filtered map rather than a variable penumbra.
Reducing the covered distance concentrates shadow detail on nearby objects.
This is a headset candidate: flicker reduction and cost need on-device feedback.

Quest implicit ground and derived terrain receive building/mech shadows but
do not cast shadows. The dedicated terrain shadow proxy is omitted, and scatter
rocks also do not cast. Mech and building geometry retains shadow casting.
Desktop terrain shadows and long-range sun tuning remain unchanged. The sun
shadow menu switch still works; turning it off disables the directional pass.
