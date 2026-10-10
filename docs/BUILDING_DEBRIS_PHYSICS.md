# Building explosion debris settling

## Quest report, October 10, 2026

The headset screenshot `uk.co.deanthecoder.mechrewired-20261010-171629.jpg`
shows several chemical-plant explosion chunks standing upright on narrow points.

`BattlefieldActor` previously moved the original CHUNKER meshes manually,
used a world bounding box for the ground bounce, then set pitch and roll to
zero at the first weak bounce (or after 12 seconds). This abruptly changed
orientation and height, and the original mesh axes did not necessarily describe
a stable resting face. These chunks were not Godot rigid bodies.

Explosion chunks now use convex collision hulls made from those same original
meshes. Their physical origins are centered on the mesh bounds; visual and
collision offsets match. The existing launch position, spin, low gravity and
5–10 chunks per destroyed actor remain. Hulls are shared between repeated
copies during each explosion. Bodies collide only with the existing decoded
terrain, can topple naturally, and sleep after settling. There is no timer or
script that resets their tilt or teleports them onto the ground. Distance-based
permanent cleanup remains.

Mech and aircraft wreckage already use rigid bodies and are unchanged. The
building's authored destroyed representation is also unchanged. No physics
engine switch is needed to correct the forced orientation reset.

A focused native regression check exercises an irregular, off-origin chunk
against flat terrain, including visual/collider alignment, settling and sleep.
Headset validation is still needed for the actual chemical-plant meshes and
for performance when destroying several buildings together.
