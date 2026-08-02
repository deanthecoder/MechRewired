[![Twitter URL](https://img.shields.io/twitter/url/https/twitter.com/deanthecoder.svg?style=social&label=Follow%20%40deanthecoder)](https://twitter.com/deanthecoder) [![GitHub Repo stars](https://img.shields.io/github/stars/deanthecoder/MechRewired?style=social&label=Star)](https://github.com/deanthecoder/MechRewired/stargazers)

# MechRewired

**A modern, cross-platform reimplementation of the classic MechWarrior 2 combat experience.**

MechRewired is an independent engine written in C# with Godot. Its first target is the in-mission experience of the original DOS release of *MechWarrior 2: 31st Century Combat*.

The immediate goal is intentionally narrow: load original game data, enter a battlefield, pilot a BattleMech, target enemies, manage heat and weapons, and complete a mission. Intro videos, menus, the mech lab and campaign presentation come later.

## Project status

MechRewired has completed its initial resource-foundation stage. It detects and performs a lightweight check of the original DOS project archive, but does not yet render or play MechWarrior 2.

The first vertical slice will establish:

- Detection and validation of an original DOS installation.
- Readers for `MW2.PRJ` and the resource formats needed by the first mission.
- DOS-inspired terrain, skies and articulated BattleMechs.
- Throttle, steering, torso twist and cockpit movement.
- Targeting, radar, weapons, heat and location-based damage.
- One completable mission with basic friendly and hostile AI.

The remaster will preserve the DOS version's stark geometry, colors and atmosphere while adding modern lighting, particles, depth fog, bloom, shadows and material detail.

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

## VR

Godot supports OpenXR and Meta Quest devices. VR is not part of the first playable milestone, but the cockpit camera and input layers will be designed so a Quest mode can be added without changing the simulation. Desktop OpenXR streaming and a native Quest build are both potential targets.

## Relationship to MechWarrior 2

MechRewired is an unofficial, independently written compatibility engine. MechWarrior, BattleTech and related names and assets belong to their respective owners. No affiliation or endorsement is implied.

Community reverse-engineering projects and historical documentation may be consulted to understand file formats and observable behaviour, but MechRewired's implementation is independently written.

## License

MechRewired's original source code is licensed under the MIT License. This licence does not apply to any original game data supplied by users.
