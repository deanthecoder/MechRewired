# Quest 3 export and setup

The Quest build is a seated-cockpit VR build. It starts the Wolf desert campaign
directly once compatible game data is present. If it is missing, VR shows a
readable setup message; the desktop drag-and-drop importer is not available in VR.
Use `-- --vr-preview` for a flat desktop preview, or
`--xr-mode on --rendering-method mobile -- --vr` for a PC OpenXR headset.

## Export prerequisites

Use the .NET edition of Godot 4.7.1 that matches the project, with the matching
Android export templates. Configure a JDK 17 or newer and Android SDK in
**Editor Settings > Export > Android**. Then open `MechRewired/project.godot`
and choose **Project > Install Android Build Template**. The generated
`MechRewired/android/` directory is local build scaffolding and is not tracked.

The checked-in **Quest 3 (setup required)** Android preset selects arm64, Gradle,
Godot's Mobile renderer, and OpenXR Android mode. It creates
`MechRewired/builds/MechRewired-Quest3.apk`. No original game files belong in an
APK, Git, or the generated Android template.

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
turns the torso, the right trigger repeats the selected weapon, and the left
trigger controls relative head aim. A selects the next weapon, B selects a
target, X inspects, Y recentres, and left Menu pauses. Jump jets are not mapped
for the initial Quest build.

## Install and game data

Enable Developer Mode on the Quest, connect it by USB, and accept the headset's
USB-debugging prompt. Export from Godot and install the resulting APK with the
Godot editor or `adb install -r MechRewired/builds/MechRewired-Quest3.apk`.

The APK intentionally contains no MechWarrior 2 files. For this prototype,
export a **debug** APK, launch it once from **Unknown Sources**, and sideload
your compatible DOS `MW2.PRJ` into `user://game-data`. The runtime prints the
resolved directory to the Godot log. With the default Android data location:

```powershell
adb push "C:\path\to\your\MW2.PRJ" /data/local/tmp/MechRewired-MW2.PRJ
adb shell run-as uk.co.deanthecoder.mechrewired mkdir -p files/game-data
adb shell run-as uk.co.deanthecoder.mechrewired cp /data/local/tmp/MechRewired-MW2.PRJ files/game-data/MW2.PRJ
adb shell rm /data/local/tmp/MechRewired-MW2.PRJ
```

Restart the app after copying. `run-as` requires a debuggable package; these
commands have not yet been verified against a headset build. For desktop debug
preview, place your data in `local/game-data/`. See [GAME_DATA.md](GAME_DATA.md)
for compatible editions and licensing notes. Original assets stay untracked.

Validate the APK on the headset after export: confirm OpenXR enters VR, the
seated cockpit is comfortable, the controls above reach the rig, and the Wolf
desert mission begins after copying the data. `Escape` opens the preview pause
menu and keeps mouse interaction available there. A desktop build or headless
Godot run does not prove Android/.NET export or headset behaviour.

## WIP scope and verification

Looking around does not aim the weapons. Holding the left trigger captures the
current head and torso angles; subsequent head rotation adjusts the torso until
release. The XR camera attaches to the seat above the synthetic camera-bob node.
Headset focus/tracking loss opens the pause menu; controller loss stops throttle.
Returning from the menu requires releasing the fire trigger before shooting.

The existing HUD, including the original chassis damage silhouette, is drawn
onto a transparent cockpit surface. Radar, weapons, status, navigation and
targeting can be toggled separately. Its finite-depth targeting presentation
still needs binocular alignment/readability testing on Quest.

Graphics controls currently cover shadows, glow, cockpit glass, terrain
parallax and battlefield smoke/dust. The cheap preset disables them initially.
Missile trails, fire, weapon flashes and other combat feedback remain; the
smoke/dust toggle is not a master switch for every particle in the game.
SSAO, screen-space reflections and the unverified stereo lens-flare compositor
are disabled. Mobile does not create the localized volumetric ground fog.
Options currently last for the mission session. The menu displays the most
recent running FPS/frame interval, **not GPU time or a paused benchmark**.

In a debug build with original data installed, run Godot with
`-- --vr-preview --vr-smoke` to exercise synthetic XR controller input, latched
throttle/stop, relative head aiming, menu fire suppression and mission-result
presentation. A graphics run also captures cockpit/menu previews into the
ignored `artifacts/` directory. This harness does not validate real tracking,
Android drivers, stereo comfort or headset performance.

On the implementation machine, the solution builds with zero warnings/errors,
and the existing suite reports 195 passed, 10 skipped, 0 failed. Missing-data
VR startup was exercised in Godot .NET. Full mission smoke and screenshots are
pending because no original `MW2.PRJ` is installed here. No APK has been exported:
matching .NET Android export templates and the Android SDK/JDK remain to be set up.
