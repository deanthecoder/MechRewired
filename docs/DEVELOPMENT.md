# Development notes

This guide covers source builds, the full pilot control set, and debug capture tools. For a player-oriented overview and setup, see the [README](../README.md) and [original game setup](GAME_DATA.md).

## Build and run

Requirements:

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (the projects target .NET 8 for Godot compatibility)
- [Godot 4.7.1 .NET](https://godotengine.org/download/archive/4.7.1-stable/)

```shell
git submodule update --init --recursive
dotnet restore MechRewired.sln
dotnet build MechRewired.sln --no-restore
dotnet test MechRewired.Tests/MechRewired.Tests.csproj --no-build
```

Open `MechRewired/project.godot` in the .NET edition of Godot to run the game.

`local/game-data/` is an ignored debug-build fallback when no imported data directory exists. Reopen the importer with `--setup` after Godot's `--` separator:

```shell
/Applications/Godot.app/Contents/MacOS/Godot --path MechRewired -- --setup
```

Start a campaign directly when testing:

```shell
/Applications/Godot.app/Contents/MacOS/Godot --path MechRewired -- --campaign jade
/Applications/Godot.app/Contents/MacOS/Godot --path MechRewired -- --campaign wolf
```

`--jade` and `--wolf` are equivalent shorthands. Debug builds apply a 3× travel multiplier at full throttle (`0`) to shorten playtests; release builds preserve original speed.

## Pilot controls

| Area | Controls |
| --- | --- |
| Throttle | `1` stops, `2`–`9` select 20–90%, and `0` selects full. `-`/`=` adjust by 10%. `Backspace` or backtick toggles forward/reverse; reverse is limited to half speed. |
| Movement | `Left`/`Right` steer legs, `Up`/`Down` tilt the torso, `,`/`.` turn the torso, `M` brings the chassis to the torso bearing, and `/` centers torso and pilot view. Hold `Shift` plus arrow keys for a temporary pilot-head pivot. |
| Jump jets | Hold `J`. Jets spool dust and thrust for 0.75 seconds before lift, consume seven seconds of fuel, recharge while idle, and hard landings can damage both legs. |
| Weapons | Click the viewport to capture the mouse, then aim with it. Left-click or `Space` fires and advances the selected weapon. Right-click, `Enter`, or `Tab` select the next usable weapon. `Shift`+`1`/`2`/`3` assign the selected weapon to green/white/yellow groups; `'` selects the next populated group; `;` fires its ready weapons; `\` toggles chain fire/group fire. `S` shuts down or restarts the reactor when safe; `O` toggles shutdown override. |
| Targeting | `T`/`R` select next/previous live hostile, `Ctrl`+`T` clears target, `E` selects nearest live hostile, and `Q` or middle-click selects under the reticle. `I` inspects a selected or nearby active inspection objective. |
| Radar and mission | `F2` cycles normal, full-screen, and hidden radar. `X` and `Shift`+`X` change range; `N` and `Shift`+`N` cycle NAV points. `F12` toggles objectives and `F1` toggles concise control help. `Escape` closes either. |
| Cameras | `C` toggles cockpit/follow view. `F4` cycles cockpit, external, and inspector cameras. The inspector uses `W`/`A`/`S`/`D` to fly, `Q`/`E` to descend/ascend, and `Shift` for speed. `F10` enables the weapon view, which follows a fired missile until impact. |

`Ctrl`+`F1` toggles wireframe. `F3` logs the active camera's MW2-space transform, nearest rendered-triangle ray hit, cockpit dimensions, and movement state. The on-screen **Debug** menu offers equivalent rendering diagnostics where function keys are unavailable.

In debug builds, `F5` cycles the live fire/smoke VFX parameter, `F6`/`F7` decrease/increase it, `F8` restores the default preset, and `F9` logs it. Hold `Shift` with `F6`/`F7` for 5× steps.

## Debug console and visual capture

In a debug build, backtick opens the developer console. `help`, `commands_list`, and `version` provide the starting inventory; `Esc` or backtick closes it.

- HUD: `hud.glow`, `hud.glow.radius`.
- Cockpit PBR: `cockpit.texture_scale`, `cockpit.metallic`, `cockpit.roughness`, `cockpit.glass.visibility`, `cockpit.glass.grime`, `cockpit.glass.scratches`; use `cockpit.inspect lit|albedo|normal|normalmap|roughness|metallic|directsun`, `cockpit.inspect_all`, or `cockpit.material_sweep` for diagnostics.
- Sky: `sky.time`, `sky.cloud.coverage`, `sky.cloud.density`, `sky.cloud.height`, `sky.fog.multiplier`, `sky.fog.start`, `sky.sun.azimuth_offset`, `sky.shadow.distance`, `sky.shadow.opacity`, and `sky.exposure` report their current setting with no argument and update it with one.
- Terrain: `terrain.inspect_lit`, `terrain.inspect_albedo`, `terrain.inspect_raw`, `terrain.inspect_normal`, `terrain.inspect_rock`, `terrain.inspect_directsun`, `terrain.inspect_roughness`, `terrain.inspect_all`, `terrain.capture_stones`, and `terrain.parallax_sweep`; tune with `terrain.texture_scale`, `terrain.detail`, `terrain.normal`, `terrain.stones`, `terrain.stone_scale`, `terrain.parallax`, `terrain.rock_start`, and `terrain.rock_end`.
- `visual.capture authored|day|dusk|night` writes a PNG and manifest to `user://visual-captures`; `visual.capture_all` writes all four. `snap` saves a timestamped PNG to Downloads.

`visual.gallery` refreshes the README gallery from a Godot source checkout. It launches disposable Wolf and Jade Falcon renderers at fixed 1920×1080 resolution, waits for the deployment DropShip to depart, uses fixed camera fixtures, and writes PNGs plus manifests to `img/gallery/`. The seven images replace the previous gallery only after both renderers succeed and every expected output exists. Allow a few minutes on slower GPUs; the console reports completion. The missile-trail image uses a repeatable staged six-missile salvo, but each projectile, mesh, and smoke effect is the ordinary in-game `MissileEffect` implementation. The command is debug-only and does not move the pilot or fire weapons in the active session. Re-running it updates the same filenames, so the README needs no editing.

## Cockpit asset workflow

The editable cockpit is [Art/Cockpit/cockpit.blend](../Art/Cockpit/cockpit.blend), exported to `MechRewired/Assets/Models/Cockpit/cockpit.glb`. After editing source parts, refresh the hidden `07 | Optimized game export - hidden in source` collection with evaluated meshes grouped by material, then export only that collection as GLB with Y-up and active vertex colors. Keep `CockpitFrame`, `CockpitArmor`, and `CockpitGlass` mesh names for runtime material controls; exclude guides, cameras, and lights. The game supplies cockpit pitch and lighting.

## Structure and references

- `MechRewired.Core` contains game-data readers, simulation, and mission logic without a Godot dependency.
- `MechRewired` is the Godot application for rendering, input, audio, and platform integration.
- `MechRewired.Tests` contains NUnit tests for the independent core.
- `DTC.Core` is a submodule with shared logging, filesystem, and general utilities.

The [visual target](VISUAL_TARGET.md) documents the art direction, [roadmap](ROADMAP.md) records planned work, and [third-party licenses](THIRD_PARTY_LICENSES.md) records required attributions.
