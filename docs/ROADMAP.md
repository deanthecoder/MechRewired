# MechRewired development roadmap

MechRewired will grow through thin vertical slices. File formats will be decoded when they unlock the next visible or playable result rather than as an exhaustive reverse-engineering exercise.

The three principal milestones are:

1. Display an original DOS BattleMech with faithful flat-shaded materials.
2. Walk and fight in a test battlefield.
3. Complete one original MechWarrior 2 mission.

## 1. Resource foundation — complete

- Locate the required DOS `MW2.PRJ` file.
- Check that it is non-empty and begins with the expected `PROJ` signature.
- Report a useful startup error when the data is missing or invalid.

Deeper archive validation is intentionally deferred until the project is otherwise complete. Individual formats will be decoded only when they unlock the next visual or playable milestone.

## 2. First original image

- Decode color palettes. — complete
- Decode enough `WTB` model geometry and material data to assemble one flat-shaded BattleMech. — complete
- Add a Godot inspection scene with an overview camera and simple lighting. — complete

This phase is complete when MechRewired displays a recognizable DOS BattleMech directly from the original data.

## 3. Battlefield rendering

- Decode terrain geometry and mission world references. — complete
- Establish coordinate, scale and material conventions between MW2 and Godot. — complete
- Recreate the DOS terrain shading, skies, palette and mission colors. — complete

This phase is complete when a recognizable original battlefield can be explored with a debug camera.

## 4. Mech piloting

- Implement throttle, reverse, leg steering and torso twist. — initial rigid-body movement complete
- Follow terrain and enforce slope limits. — initial implementation complete
- Add speed-driven cockpit gait and landing weight. — initial implementation complete
- Add cockpit and external diagnostic cameras. — initial rig complete
- Implement targeting, weapons, heat, armor and location-based damage.
- Add a minimal diagnostic HUD.

This phase is complete when the player can pilot one mech around the battlefield and destroy a stationary target.

## 5. Original mission gameplay

- Load mech definitions and weapon configurations from original data.
- Decode mission entities, teams, spawn points, waypoints and objectives.
- Add basic friendly and hostile navigation, targeting and combat.
- Implement mission success and failure.

This phase is complete when one original mission can be played from deployment to a diagnostic debrief state.

## 6. Fidelity and remaster effects

- Add articulated legs and inverse kinematics.
- Improve cockpit shadow quality, stabilize nearby building shadows and tune shadow darkness for the dusk palette.
- Add emissive missile and weapon lighting.
- Add particles for explosions, smoke, sparks, dust and damage feedback.
- Add depth fog, bloom, glow, dynamic shadows and cockpit lighting.
- Add restrained bump or normal mapping without losing the DOS art direction.
- Play original sound, voice and CD music resources, beginning with torso motors, footsteps and mission-start status announcements such as "Temperature nominal."
- Make major enhancements independently adjustable where useful.

## 7. Productization

- Discover and validate original game data automatically.
- Add friendly missing-data guidance, settings and input rebinding.
- Export supported macOS and Windows builds.
- Add joystick and HOTAS support.
- Add presentation features such as menus, the mech lab and campaign progression.
- Add OpenXR support after desktop cockpit gameplay is stable.

## Engineering approach

- Keep resource parsing and simulation in `MechRewired.Core`, independent of Godot.
- Consult DTC.Core before creating reusable infrastructure.
- Use external projects and format research as references while keeping MechRewired's implementation independent.
- Keep commercial game data out of source control and CI.
- Add small NUnit tests for meaningful behavior and failure isolation using synthetic binary fixtures.
- Log edition detection, resource counts, asset metadata, parser context and important gameplay transitions.
- Avoid routine per-frame logging outside an explicit diagnostic mode.

## Visual direction

The DOS release is the visual reference. MechRewired will retain its bold silhouettes, sparse surfaces, mission palettes and uncluttered atmosphere instead of adopting the 3dfx texture set. Modern effects should add physical presence and depth without turning the game into a differently styled remake.

The roadmap is intentionally adaptable. Discoveries in the original data may change the order of individual format readers without changing the playable milestones.
