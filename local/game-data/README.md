# Local game data

Place private MechWarrior 2 reference files here during development.

This is a debug-build fallback. Normal users import their files through the first-run setup
screen; imported data takes precedence. See [setup instructions](../../docs/GAME_DATA.md).

For the preferred DOS edition, the initial high-value file is:

```text
MW2.PRJ
```

Optional editions may be retained under `editions/` for private format comparisons, but they are not required by the first playable milestone.

Other installation files may be required as format support grows. Everything in this directory except this README is ignored by Git. Do not commit original game data, binaries, music, models or textures.

## Clan-selection screen

To enable the original title plate and animation, optionally copy these files from
a licensed installation to the ignored `DEMODATA/` directory:

```text
DEMODATA/AMWLOGO1.SMK
DEMODATA/FIRELOGO.MW2
```

`DEMODATA/CLANSELECT_CENTER.png` is a separate optional developer reference: the centered
original Mech/fire composition cropped from the supplied
screen reference. It is not a file from the retail installation. Debug builds use this local
copy even when imported game data takes precedence. All of these files are ignored:
MechRewired does not commit original game media or reference artwork into the repository.
