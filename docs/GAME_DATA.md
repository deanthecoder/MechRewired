# Original game setup

MechRewired is an independent engine. It does not ship the original MechWarrior 2 data.
Use a copy you have the right to use.

## Quick start

1. Open MechRewired. If game data is missing or incompatible, the setup screen appears.
2. Use your existing **MechWarrior 2: 31st Century Combat DOS** installation, or open the
   [GamesNostalgia files page](https://gamesnostalgia.net/game/mechwarrior-2-31st-century-combat/files#ms-dos).
   Select **PC / English / MS-DOS v1.1**, then download `mechwarrior2_dos_win.7z`
   (listed as 578.79 MB, approximately 607 MB in decimal units).
   Select the PC package even on macOS or Linux: the files inside are DOS game data.
3. Drop the unopened `.7z` onto MechRewired, or choose it with **Choose archive or MW2.PRJ**.
   ZIP packages, extracted folders, and individual `MW2.PRJ` files also work.
4. Wait for validation and import, then choose Jade Falcon or Wolf.
   A large, solid-compressed 7z can take a few minutes to read.

You do not need to run the package's DOSBox, batch files, installers, or original executables.
For an already extracted package, you can select `MechWarrior2` or `MechWarrior2/disk/MECH2`.
If a package contains several `MW2.PRJ` files, select the specific installation folder or file.
Disc images, RARs, and installer executables are not imported directly; select an extracted
installation instead. A renamed disc image or archive is not a supported conversion.

The download page is a third-party listing. Its availability does not establish publisher
permission to redistribute the game. MechRewired links to the page and imports user-supplied
data; it does not mirror, bundle, or automatically download the commercial files.

## What is imported

- `MW2.PRJ` — required; the archive index and resources needed for campaign selection are checked.
- `DEMODATA/FIRELOGO.MW2` — optional original title plate.
- `DEMODATA/AMWLOGO1.SMK` — optional original title animation.

Only these files are copied. The title files must belong to the same installation as the
selected archive. File and folder names are matched without regard to case. Imports are
staged and checked before replacing an existing imported installation. Invalid or ambiguous
packages show an error in the setup screen and leave the existing installation intact.

The current compatibility gate checks the archive structure and the presence of the two
opening scenarios, player MEKs, clan insignias, palette, fire audio, and `MTAB/MECH.MTB`.
It is not exhaustive validation of every mission payload or support for every DOS release.
The known 19,960,257-byte archive in several older DOS downloads lacks `MECH.MTB` and is
rejected before clan selection. Expansions, demos, and Windows/Titanium editions are not
currently supported setup targets.

## Where files are saved

Imports use Godot's `user://game-data` directory, outside the application and repository:

| Platform | Default location |
| --- | --- |
| Windows | `%APPDATA%/Godot/app_userdata/MechRewired/game-data/` |
| macOS | `~/Library/Application Support/Godot/app_userdata/MechRewired/game-data/` |
| Linux | `~/.local/share/godot/app_userdata/MechRewired/game-data/` (or under `XDG_DATA_HOME`) |

To reopen setup, launch the exported application with `-- --setup`, or from the repository:

```shell
/Applications/Godot.app/Contents/MacOS/Godot --path MechRewired -- --setup
```

Debug builds also recognize `local/game-data/` when no imported directory exists. Release
builds use per-user storage. This fallback is for private developer data and is ignored by Git.

## Optional clan-selection presentation

The original title plate and animation are decoded directly in memory with the managed
StbImageSharp and SmackerSharp libraries. No external media tools or generated PNG files are
needed. Hovering over either original clan insignia shows its clan name; clicking the insignia
selects that campaign.

The former mandatory `DEMODATA/CLANSELECT_CENTER.png` is a locally cropped reference
screenshot, **not a retail game file**. It is optional; the importer preserves it when it is
present beside user-supplied game data, and debug builds also find it under `local/game-data`.
The verified retail download does not contain it.

## Download verification — September 6, 2026

The retained local installation was traced to a Chrome download named
`mechwarrior2_dos_win.7z` from GamesNostalgia. A fresh copy from the current files page has
this layout:

```text
MechWarrior2/
  cd/                       # CD image; not imported
  disk/MECH2/
    MW2.PRJ
    DEMODATA/FIRELOGO.MW2
    DEMODATA/AMWLOGO1.SMK
```

The package is 606,906,521 bytes. Its `MW2.PRJ` is 21,893,380 bytes, with SHA-256:

```text
e79f04412fb26cdad86403f19dd73605a8278fc3666608d782d9526bb2a2bef4
```

That archive matches the existing local reference byte-for-byte. It was imported directly
from the downloaded 7z through the setup UI and successfully started the Jade Falcon mission.
The fingerprint is a reproducibility record, not a claim about distribution rights or a
required checksum for every compatible edition.

Other downloads checked:

| Source | Result |
| --- | --- |
| [My Abandonware DOS packages](https://www.myabandonware.com/game/mechwarrior-2-31st-century-combat-34i#download) | The RIP Version, Complete Game with DOSBox, and Preinstalled ISO packages expose a 19,960,257-byte `MW2.PRJ` without `MECH.MTB`. They are not suitable for the current engine. |
| [DOS Games Archive demo](https://www.dosgamesarchive.com/file/mechwarrior-2-31st-century-combat/mech2dem/) | `mech2dem.zip` contains a DOS installer executable, not an importable campaign archive. Demo support has not been implemented. |

Download links and packages can change. Prefer the source page to a temporary CDN URL,
and keep compatibility checks in the importer rather than trusting an archive's filename.
