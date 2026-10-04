# Handle With Care

**Pack bizarre deliveries so they survive a journey you get to watch afterward.**

You are the newest packer at the Mossbury Parcel Post, the only courier in town that will ship
*anything*: sleeping armadillos, magnets that must never meet, a potion that has to stay upright,
a birthday balloon next to a cactus, and Ember, a tiny dragon with a cold. Pack each order into a
cardboard box with paper, bubble wrap, foam, dividers, shelves and straps, seal it with a
satisfying screech of tape, and then watch the parcel ride a delivery truck, a sorting depot, a
careless courier, a ferry, a cargo plane and a catapult. At the other end the customer opens the
box on their kitchen table, and every item comes out with a rubber stamp: PERFECT, SHATTERED,
WIDE AWAKE, SPILLED, MELTED, POPPED, BOX ON FIRE...

Built in Unity 6 (6000.6.2f1, URP). Every 3D model is generated in Blender 4.5 by Python
scripts; every texture, sound effect and piece of music is synthesized from scratch.

## Controls

| Input | Action |
| --- | --- |
| Left click an item on the shelf | Pick it up |
| Left click in the box | Drop the held item (it slides down to the first spot it can rest on) |
| `1`-`6` or click the toolbar | Paper, bubble wrap, foam, divider, shelf, strap |
| Left click + drag (padding) | Paint padding into every cell you sweep over |
| Right drag (padding) | Erase padding |
| `R`, right click or mouse wheel | Rotate the held piece, or turn a creature/dragon to face the other way |
| Left click a placed piece | Pick it back up |
| Right click a placed piece | Return it to the shelf (items) or bin it (padding) |
| Click a divider/shelf (divider/shelf tool) | Remove it |
| `Z` / `Y` (or Shift+`Z`) | Undo / redo |
| `Space` / `Enter` | Seal & ship (when every item is packed) |
| `Esc` | Drop what you are holding, or pause |
| During the journey | `Space` pause, `Enter` skip, `1`-`4` speed |
| Replay | Click the timeline to jump, speed buttons, `C` or CAM button cycles director / close-up / wide |

## Rules

- Everything sits on a grid. Pieces must rest on something (balloons rest against the ceiling).
- The journey is a **deterministic 2D physics simulation**: the same packing always gives the
  same journey. What you see the box do (brake, pothole, belt drop, robot arm, chute, stairs,
  toss, waves, air pocket, catapult) is exactly what the contents feel.
- Every item has a **jolt limit**. Jolts are measured from impacts and softened by what the item
  hits: walls are hard, paper a little softer, bubble wrap much softer, foam softest.
- Item quirks (shown on the item card when you hover):
  - **Fragile**: breaks above its jolt limit. **Tall** pieces (vase, potion, lava lamp, ice swan)
    tip over unless something is beside their shoulders. The potion and the lava lamp must stay
    **upright**.
  - **Snoozles the armadillo** rolls over in his sleep and wakes up on a hard knock.
  - **Magnets** pull each other (and the wind-up robot) from 4 cells away. If they touch, they
    are stuck together for good.
  - **Cake**: nothing heavier than padding may rest on it. **Balloon** floats and pops on spikes,
    fire and big squeezes. **Cactus** pops balloons and bubble wrap and wakes sleepers.
  - **Clank the robot** marches until something stops him, then turns around.
  - **Ice swan** melts within 1 cell of something hot; a divider blocks the heat.
  - **Bouncy ball** keeps all its energy. **Frog** hops every few seconds.
  - **Ember the dragon** has a cold: he sneezes fire 3 cells ahead every few seconds and when
    jolted. Paper burns, bubble wrap melts, balloons pop, cardboard catches fire (the box is
    ruined). Foam and ceramic stop the flame.
  - **Dragon egg**: very fragile and must be kept within a cell of something warm for most of
    the trip.
- **Stars**: ★ Delivered (every item arrives OK), ★ Under budget (materials cost at or below
  par), ★ Handled with care (no item ever went above 65% of its limit, and nothing toppled).
  Stars add up across attempts and unlock tape designs.
- After a failed (or rattly) trip, the packing screen shows each item's path from the last run
  as a trail, marks where things went wrong, and lists what happened and where ("Vase shattered
  at the hard brake, jolt 11/8").

## Content

Twenty handcrafted deliveries over four shifts, each introducing a new idea:

| Shift | Deliveries |
| --- | --- |
| 1 · First Day (truck) | A Cup for Edna (tutorial), Bookends, The Tall Vase, Strike!, Snoozles |
| 2 · The Sorting Depot (truck + depot) | Opposites Attract, Potion Commotion, Happy Birthday Timmy, A Prickly Situation, Magnetic Personality |
| 3 · Last Mile (depot / truck + doorstep) | Clockwork Clank, Swan Song, Boing, Hoppy Delivery, Ember |
| 4 · Express Service (ferry, plane, catapult) | High Seas, Air Pocket, The Vase and the Dragon, Museum Piece, The Dragon Egg |

19 item types, 3 kinds of padding, dividers, shelves and straps; six journey environments (truck
on a country road, sorting depot with conveyors / robot arm / chute, doorstep with Dash the
courier, ferry deck, cargo-plane hold, catapult and haystack), an unboxing scene, title, delivery
log, settings, pause, replay with a director camera, tutorial, shift title cards and six tape
designs.

## Art

Everything is modelled in Blender by script and aims for a tactile, "real objects on a real
bench" look. Each prop is built from bevelled primitives and voxel-fused organic shapes, given
procedural physically based materials (glazed ceramic, plush, chrome, car paint, timber, foliage,
burlap, gingham...), then baked in Cycles to albedo, normal and mask maps with ambient occlusion,
which Unity's URP Lit shader reads directly. Big surfaces (road, grass, concrete, brick, shingles,
the sea) use tileable PBR texture sets instead. A realtime reflection probe re-captures each
scene, so glaze, glass and metal reflect the room they are in.

## Project layout

```
Assets/
  Scripts/Sim/        Deterministic simulation in pure C# (no UnityEngine): items, packing rules,
                      routes/kinematics, solver, quirks, recording, the 20 levels
  Scripts/Game/       Game flow, packing controller, journey player, reveal, save, autopilot
  Scripts/Visuals/    Box/piece views, journey stages, camera rig, post, particles, overlays
  Scripts/UI/         Runtime-built uGUI + TextMeshPro: HUD, menus, tutorial
  Scripts/Audio/      Pooled SFX, music crossfades and ducking
  Editor/             Project setup, import settings, build script
  Resources/          Models (FBX from Blender), Textures, Audio, Fonts, template materials
ArtSource/            Blender/numpy generators (+ .blend sources in ArtSource/blend)
  hwc_lib.py          Modeling helpers (Unity-space bmesh primitives, voxel-fused organic
                      shapes, FBX export, Cycles preview renders)
  pbr.py              Procedural physically based materials (ceramic, plush, glass, chrome,
                      car paint, foliage, timber...) and the Cycles bake that turns them into
                      albedo / normal / mask (metallic, occlusion, smoothness) textures
  items.py boxes.py station.py stages.py   Model builders
  build_assets.py     Builds, bakes and exports every model, optional contact sheets
  make_textures.py    Kraft, corrugation, tape, labels, stamps, plus tileable PBR surface sets
                      (asphalt, concrete, grass, cobbles, brick, shingles, tread plate, water,
                      hay) and the painted cloud layer
  make_audio.py       Every sound effect and music loop
Tools/
  SimCheck/           .NET console app compiling Assets/Scripts/Sim: validates every level,
                      explores and solves packings (used to set pars)
  simcheck.sh         Build + run SimCheck
  unity.sh            Editor launcher (libxml2 shim), batch setup/build, headless "serve"
  build.sh            Linux player build (resident editor or batch)
  play.sh             Run the built game
  autopilot.sh        Player self-test over all 20 deliveries (PASS/FAIL + determinism)
  tour.sh             Screenshot tour with real mouse/keyboard events
docs/                 Brief and design plan
```

## Building and running

```sh
Tools/build.sh                  # Builds/Linux/HandleWithCare.x86_64
Tools/play.sh                   # play (windowed 1920x1080; uses -force-wayland on Wayland)
```

Rebuild the generated assets (each is deterministic):

```sh
blender -b -P ArtSource/build_assets.py -- items boxes station stages   # models -> Assets/Resources/Models,
                                                                        # baked maps -> Textures/Baked (~40 min)
blender -b -P ArtSource/build_assets.py -- items --preview /tmp/prev     # + Cycles contact sheets for review
blender -b -P ArtSource/build_assets.py -- stages --no-bake --no-export --preview /tmp/prev   # fast look
blender -b -P ArtSource/make_textures.py                                # textures -> Assets/Resources/Textures
~/.local/opt/blender-4.5.9-linux-x64/4.5/python/bin/python3.11 ArtSource/make_audio.py   # audio (needs numpy)
```

## Verification

```sh
Tools/simcheck.sh check        # every reference packing valid, delivered, under par, 3-star,
                               # deterministic; item-only packings fail
Tools/simcheck.sh explore 7    # random item-only packings for a level (should never deliver)
Tools/simcheck.sh solve 7      # parallel local search for cheap / three-star packings
Tools/autopilot.sh             # the built player plays all 20 references and checks outcomes
Tools/tour.sh                  # screenshots of menus + a delivery played with real input events
```

The simulation runs at 240 Hz on axis-aligned bodies with sequential impulses, so a journey of
20-30 seconds simulates in a few tens of milliseconds when you seal the box. The journey,
replays, the director camera, the reveal, the review and the last-trip trails all play back the
same recording.

## Known limitations

- Trees and bushes are solid sculpted canopies with baked leaf detail, not individual leaves; up
  close they look more like well-made models than real foliage. The courier is a simple jointed
  figure animated procedurally.
- The synthesized audio has been checked only by measurement (levels, spectrum), not by ear.
- A few early deliveries are cheap to solve (for example A Prickly Situation can be done with one
  divider), so their under-budget star is easy.

## Credits

Everything in this project was made for it: code, Blender model scripts, procedural textures,
synthesized music and sound effects. Fonts: Fira Sans and Fira Sans Compressed (SIL Open Font
License, `Assets/Resources/Fonts/Fira-OFL.txt`). TextMesh Pro essentials and LiberationSans ship
with Unity.
