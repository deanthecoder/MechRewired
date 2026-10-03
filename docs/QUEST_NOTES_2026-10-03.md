# Quest headset notes — 3 October 2026

The user ran the rendering and combat benchmarks again on the Quest. No new
Mac-side log was saved, and the headset's logcat ring was empty when checked.
The Release app's raw benchmark backups are in private `user://benchmarks/`
storage, which this ADB connection cannot read. The user reports that the
combat test finished, but **no numerical result or log-confirmed status from
these two new runs is available for analysis**. The
[1 October captured results](QUEST_TESTS_2026-10-01.md) remain the latest
committed measurement set; do not treat them as the 3 October rerun.

Headset feedback during this rerun:

- With **BAKED SKY + CABIN** enabled, the sun disc is in the right place but
  its halo is offset. The earlier comment about weak or missing sun bloom still
  applies; the halo mismatch should be diagnosed against the sun disc and
  captured sky coordinates before baking or moving any glow.
- The cockpit obscures the navigation/target panel at the lower left and the
  player-damage silhouette at the lower right. The desired Quest ordering is
  **damage, Heat, dH/dT, Jets, navigation/target** across the lower HUD.
- The combat benchmark finished but remained at its result menu instead of
  returning to normal play. Right-stick vertical input no longer pitched the
  torso. The head-following aiming reticle needs a little movement damping.

The associated code change places the two HUD displays around the gauges only
in VR, restores right-stick torso pitch, smooths the shared reticle/weapon aim
point slightly, and makes a completed combat suite resume a fresh mission with
its captured settings. These changes were checked in desktop VR preview and
then installed as a locally signed Release APK on the Quest, preserving the
existing app data. OpenXR/Mobile Vulkan startup, the private `MW2.PRJ` checksum,
7,735 indexed resources and a first mission frame were verified. The changes
still need a headset feel/occlusion check. The halo offset is **recorded, not
corrected**: its cause and a visually sound fix need a matched headset view.
