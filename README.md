[![Twitter URL](https://img.shields.io/twitter/url/https/twitter.com/deanthecoder.svg?style=social&label=Follow%20%40deanthecoder)](https://twitter.com/deanthecoder) [![GitHub Repo stars](https://img.shields.io/github/stars/deanthecoder/MechRewired?style=social&label=Star)](https://github.com/deanthecoder/MechRewired/stargazers)

# MechRewired

**A modern, cross-platform reimplementation of the classic MechWarrior 2 combat experience.**

MechRewired is an independent engine written in C# with Godot. Its first target is the in-mission experience of the original DOS release of *MechWarrior 2: 31st Century Combat*.

![Timber Wolf cockpit overlooking the battlefield](img/Cockpit.png)

![Chemical Plant objective at Nav Epsilon](img/ChemPlant.png)

The immediate goal is intentionally narrow: load original game data, enter a battlefield, pilot a BattleMech, target enemies, manage heat and weapons, and complete a mission. Intro videos, menus, the mech lab and campaign presentation come later.

## Project status

MechRewired reads the original DOS project archive, palettes, WTB model geometry, MEK movement data, BWD world placement data and MTBL mission objectives. It currently renders Pyre Light's terrain, palette-derived atmosphere, scenery and ground-settled wreckage; identifies targetable gameplay actors and alternate destroyed representations; and deploys a controllable PlayerMech at the original Dropsite with cockpit, external and inspector cameras. The initial gameplay slice includes original-style movement, objective targeting, a medium laser, building damage, low-gravity original-model explosion debris and the original mission reports.

The first vertical slice will establish:

- Detection and validation of an original DOS installation.
- Readers for `MW2.PRJ` and the resource formats needed by the first mission.
- DOS-inspired terrain, skies and articulated BattleMechs.
- Throttle, steering, torso twist and cockpit movement.
- Targeting, radar, weapons, heat and location-based damage.
- One completable mission with basic friendly and hostile AI.

The remaster will preserve the DOS version's stark geometry, colors and atmosphere while adding modern lighting, particles, depth fog, bloom, shadows and material detail.

Battlefield fire and smoke use an adapted GPU flipbook shader and smoke atlas from [GodotExplosionVFX](https://github.com/memo1918/GodotExplosionVFX); the required MIT attribution is in [THIRD_PARTY_LICENSES.md](docs/THIRD_PARTY_LICENSES.md).

See the [development roadmap](docs/ROADMAP.md) for the planned sequence of playable milestones.

## Architecture

The repository separates the original-game implementation from the host engine:

- `MechRewired.Core` contains data readers, simulation, mission logic and deterministic tests. It has no dependency on Godot.
- `MechRewired` is the Godot 4.7 .NET application responsible for rendering, input, audio and platform integration.
- `MechRewired.Tests` contains NUnit tests for the independent core.
- `DTC.Core` is included as a submodule for shared logging, filesystem and general-purpose infrastructure.

This separation keeps gameplay testable and leaves room for desktop, VR and tooling hosts to share the same simulation.

## Original game data

MechRewired does not include or distribute Activision's game data, executable code, music, models or textures. You will need a legitimate installation of a supported edition of MechWarrior 2.

For local development, place private reference files under:

```text
local/game-data/
```

That directory is ignored by Git apart from its README.

## Build

Requirements:

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (the projects target .NET 8 for Godot compatibility)
- [Godot 4.7.1 .NET](https://godotengine.org/download/archive/4.7.1-stable/)

Restore, build and test the managed projects:

```shell
git submodule update --init --recursive
dotnet restore MechRewired.sln
dotnet build MechRewired.sln --no-restore
dotnet test MechRewired.Tests/MechRewired.Tests.csproj --no-build
```

Open `MechRewired/project.godot` with the .NET edition of Godot to run the application.

The application starts in the 3D cockpit. The current piloting controls follow the original game's defaults:

- Press <kbd>1</kbd> to stop, <kbd>2</kbd>–<kbd>9</kbd> for 20–90% throttle, or <kbd>0</kbd> for full throttle. Press <kbd>-</kbd>/<kbd>=</kbd> to adjust the throttle in 10% steps.
- Press <kbd>Backspace</kbd> or <kbd>`</kbd> to toggle forward/reverse. Reverse is limited to half the forward speed.
- Use <kbd>Left</kbd>/<kbd>Right</kbd> to steer the legs, <kbd>Up</kbd>/<kbd>Down</kbd> to tilt the torso, and <kbd>,</kbd>/<kbd>.</kbd> to turn the torso.
- Click the viewport to capture the mouse, then move it to aim the torso. Left-click or <kbd>Space</kbd> fires the initial laser weapon. The faithful implemented target controls are <kbd>T</kbd>/<kbd>R</kbd> for next/previous live hostile, <kbd>Ctrl</kbd>+<kbd>T</kbd> to clear targeting, <kbd>E</kbd> for nearest live hostile, <kbd>Q</kbd> (or middle-click) for the actor under the reticle, and <kbd>I</kbd> to inspect the selected or nearby active inspection objective. Friendly targeting is not implemented yet. Right-click cycles weapons; <kbd>Tab</kbd> and Control-click are trackpad-friendly aliases.
- In debug builds, selecting maximum speed with <kbd>0</kbd> applies a 3× travel multiplier to shorten mission playtesting; release builds retain the original speed.
- Hold <kbd>Shift</kbd> and use the arrow keys for a quick, damped pilot head pivot. Releasing <kbd>Shift</kbd> or the arrows smoothly returns the pilot view to centre.
- Press <kbd>C</kbd> or keypad <kbd>5</kbd> to centre both the torso and pilot view. Keypad <kbd>5</kbd> matches the original key map; <kbd>C</kbd> is the laptop-friendly alias.
- Press <kbd>M</kbd> to turn the legs and chassis smoothly towards the torso's current bearing.
- Press <kbd>X</kbd> to reduce the radar range or <kbd>Shift</kbd>+<kbd>X</kbd> to increase it. Press <kbd>N</kbd>/<kbd>Shift</kbd>+<kbd>N</kbd> to cycle forwards/backwards through mission NAV points.
- Press <kbd>Escape</kbd> to release the mouse.

The camera and battlefield inspection controls are:

- Press <kbd>F4</kbd> to cycle through cockpit, external and free-flight inspector cameras.
- In inspector view, use <kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd> to fly and <kbd>Q</kbd>/<kbd>E</kbd> to descend/ascend.
- Hold <kbd>Shift</kbd> for an inspector-camera speed boost.
- Press <kbd>F1</kbd> to toggle wireframe rendering or <kbd>F2</kbd> to toggle unshaded rendering.
- Press <kbd>F3</kbd> to log the active camera's MW2-space transform, its nearest rendered-triangle ray hit, the current cockpit dimensions and the PlayerMech movement state.
- The on-screen **Debug** menu provides the same rendering diagnostics when function keys are unavailable.
- In debug builds, <kbd>F5</kbd> cycles the live fire/smoke VFX parameter, <kbd>F6</kbd>/<kbd>F7</kbd> decreases/increases it, <kbd>F8</kbd> restores the default preset and <kbd>F9</kbd> logs it. Hold <kbd>Shift</kbd> with <kbd>F6</kbd>/<kbd>F7</kbd> for 5× steps.

## VR

Godot supports OpenXR and Meta Quest devices. VR is not part of the first playable milestone, but the cockpit camera and input layers will be designed so a Quest mode can be added without changing the simulation. Desktop OpenXR streaming and a native Quest build are both potential targets.

## Relationship to MechWarrior 2

MechRewired is an unofficial, independently written compatibility engine. MechWarrior, BattleTech and related names and assets belong to their respective owners. No affiliation or endorsement is implied.

Community reverse-engineering projects and historical documentation may be consulted to understand file formats and observable behaviour, but MechRewired's implementation is independently written.

## License

MechRewired's original source code is licensed under the MIT License. This licence does not apply to any original game data supplied by users.
