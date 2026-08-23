# Local game data

Place private MechWarrior 2 reference files here during development.

For the preferred DOS edition, the initial high-value file is:

```text
MW2.PRJ
```

Optional editions may be retained under `editions/` for private format comparisons, but they are not required by the first playable milestone.

Other installation files may be required as format support grows. Everything in this directory except this README is ignored by Git. Do not commit original game data, binaries, music, models or textures.

## Clan-selection screen

To display the original 31st Century Combat clan-selection screen, copy these original files from
a licensed installation to the ignored `DEMODATA/` directory:

```text
DEMODATA/AMWLOGO1.SMK
DEMODATA/FIRELOGO.MW2
DEMODATA/CLANSELECT_CENTER.png
```

`CLANSELECT_CENTER.png` is the centered original Mech/fire composition cropped from the supplied
screen reference. All of these files are ignored: MechRewired does not commit original game media
or reference artwork into the repository.
