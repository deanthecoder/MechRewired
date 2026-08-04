# MechRewired development roadmap

MechRewired will grow through thin vertical slices. File formats will be decoded when they unlock the next visible or playable result rather than as an exhaustive reverse-engineering exercise.

The three principal milestones are:

1. Display an original DOS BattleMech with faithful flat-shaded materials.
2. Walk and fight in a test battlefield.
3. Complete one original MechWarrior 2 mission.

## 1. Battlefield rendering

- Decode textured WTB material/decal selection and its indexed XEL resources for scenery and battlefield detail. — scenery materials remain

This phase is complete when a recognizable original battlefield can be explored with a debug camera.

## 2. Mech piloting

- Prevent the player mech walking through scenery and mission actors. — transformed vertical-wall triangle collision implemented; vertical clearance and sliding refinements remain
- Decode the original `MW2MECH.CPI`/`VWSP` cockpit and view definitions, then replace or validate the procedural cockpit frame against them. - Not a priority.
- Implement the remaining targeting and combat systems: heat, armor and location-based damage.

This phase is complete when the player can pilot one mech around the battlefield and destroy a stationary target.

## 3. Original mission gameplay

- Load mech definitions and weapon configurations from original data.
- Replace the current Pyre Light-specific resource constants with a scenario-driven mission definition that resolves the planet, battlefield, deployment, NAV sequence, enemy groups and music from the original data.
- Decode the remaining mission metadata (`MTBL`, `TSK` and `AFFL`) and compare Pyre Light with structurally different missions before settling the runtime model. — MTBL fixed records and Pyre Light trigger/goal flags decoded; comparison, TSK and AFFL remain
- Represent the remaining original objectives with reusable primitives: protect a target, eliminate all required enemies, and wait for a timer or prerequisite.
- Add enemy mission actors, health, destruction transitions, weapons and damage.
- Add basic friendly and hostile navigation, targeting and combat.
- Recreate Pyre Light's mission chain with enemy opposition: reach Nav Epsilon and engage the power plant, reach Nav Zeta and inspect the firebase, then reach Nav Eta for extraction. — enemy opposition remains
- Implement data-driven objective evaluation reusable across missions, including failure and debrief states. — failure and debrief remain

This phase is complete when one original mission can be played from deployment to a diagnostic debrief state.

## 4. Fidelity and remaster effects

- Add articulated legs and inverse kinematics.
- Improve cockpit shadow quality, stabilize nearby building shadows and tune shadow darkness for the dusk palette.
- Add emissive missile and weapon lighting.
- Tune particles for explosions, smoke, sparks, dust and damage feedback. — dust and damage scaling remain
- Complete fuller task animation for BWD-authored Wolf DropShip set-pieces.
- Tune exploding structure chunks. — visual tuning remains
- Add bloom, glow and cockpit lighting.
- Add restrained bump or normal mapping without losing the DOS art direction.
- Add remaining original voice, weapon, fire and mission sound resources, plus CD music where appropriate.
- Make major enhancements independently adjustable where useful.

## 5. Productization

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
