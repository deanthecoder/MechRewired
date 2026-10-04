# Quest startup crash — 4 October 2026

The uploaded package crashed before executing game code. A fresh launch reproduced
`.NET: Assemblies not found`, followed by a native SIGSEGV in `VkThread`.
The installed APK and the local pre-rebuild APK both measured 170,868,049 bytes
and contained no `assets/.godot/mono/publish/arm64/` managed payload. The installed
APK SHA-256 was `be5f64eb5b3c8b8b3a01bdba20849030a9ede8520e0ac1119612ff618f226035`.
The generating export command is unknown; do not attribute this to the HUD or
collision changes, since the application assembly was absent.

## Repair and device verification

Rebuilt from `c781cb4` using the Mono Godot 4.7.1 executable and
`QUEST_INCLUDE_TEST_DATA=1 scripts/quest.sh build`. The .NET publish and Android
export completed. The 213,078,993-byte Release package contains:

- `assets/.godot/mono/publish/arm64/MechRewired.dll`
- `assets/.godot/mono/publish/arm64/MechRewired.runtimeconfig.json`
- `assets/.godot/mono/publish/arm64/System.Private.CoreLib.dll`
- `assets/TestData/MW2.PRJ` (21,893,380 bytes)

The assembly hash matches the Android Release publish output; bundled game data
matches the private source. APK signature verification passed with the existing
release certificate. The manifest has no debuggable flag. Candidate SHA-256:
`a9e86708b9aee7ae54f0da098fdeec7744a3945be31440eb3c9098b0e38f467f`.

Installed over USB with `adb install --no-incremental -r`, preserving app data,
and launched with logging already active. At 19:26:59 BST it reached the first
frame (13,274 ms startup), and at 19:27:00 logged focused head tracking, centered
seat, valid aim, right controller tracked, left inactive, menu closed and
`paused=False`. The process remained alive after startup. This verifies the
packaging repair and mission launch, not live firing or headset readability.
The package includes the independently tracked right-hand weapon control fix and
compact Quest HUD from the previous session.

Filtered crash and repaired startup evidence is in
[quest-startup-2026-10-04.log](data/quest-startup-2026-10-04.log). Full captures and
both old APK snapshots remain under ignored `local/benchmarks/`; the committed
excerpt normalizes trailing whitespace and excludes unrelated Android app logs.

## Packaging safeguard

`scripts/quest.sh build` now runs `scripts/validate-quest-apk.py` before reporting
success or allowing its install flow to continue. It rejects absent/empty app and
core runtime assemblies or absent/empty/invalid runtime configuration. Private
test-data builds also require a nonempty bundled MW2.PRJ. This is a structural
check; signature verification and on-device startup are separate proof levels.

All six validator unit tests passed. The broken APK is rejected and the repaired
APK passes, including the private-data requirement. Shell syntax validation
passed. The user's pre-existing ADB reconnection changes in `scripts/quest.sh`
were preserved and are not included in the packaging-fix commit.
