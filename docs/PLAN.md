# Handle With Care — Game Design & Technical Plan

## 1. One-sentence pitch

**Pack bizarre deliveries into a cardboard box so they survive a journey you get to watch afterward.**

You are the newest packer at the **Mossbury Parcel Post**, the only courier in town that will ship
*anything*: sleeping armadillos, love-struck magnets, tipsy potions and one very sneezy dragon. You
arrange items, padding, dividers and straps inside a box, seal it with a satisfying *SCREEEECH* of
tape, then watch the parcel ride a van, a sorting depot, a careless courier and worse. At the other
end, the customer opens it. Did the vase survive? Did the dragon sneeze?

## 2. Design pillars

1. **One mechanic that feels great immediately.** Drag an item into the box, it drops in with a
   soft cardboard *thup*. Stuff padding in with a paint-drag. Seal. Watch. Everything is tactile.
2. **Controlled, legible physics.** The journey is a deterministic 2D simulation. The same packing
   always produces the same journey. Every failure is shown, named and timestamped
   ("Vase: jolt 11 > limit 8 at 0:06, HARD BRAKE"), and the next packing screen shows the trails
   and impacts of the last run so the fix is obvious.
3. **Funny, shareable failures.** Failures are comedy, not punishment: a dragon flambés the
   birthday cake, magnets slam together through a teacup, an armadillo wakes up and unrolls.
   The camera knows the future (the sim is precomputed), so it slows down and zooms in on the
   moment of disaster. Customer reviews close every delivery.
4. **Premium 2.5D presentation.** A simple 2D sim drives chunky, warm, Blender-modelled 3D toys in
   a cutaway box. Kraft cardboard, cream paper, red postal stamps, soft light.
5. **One more run.** Every delivery has three stars (Delivered / Under budget / Handled with care).
   Repacking is instant and keeps your last layout. Stars unlock new tape designs.

## 3. Core loop

```
Order card ─► PACK ─► SEAL ─► JOURNEY (watch) ─► UNBOXING REVEAL ─► Review & stars
   ▲                                                                      │
   └──────────── Repack (layout kept, last-run trails shown) ◄────────────┘
                                   or Next delivery / Replay with director cam
```

1. **Order.** A shipping-label card: recipient, items, route legs (icons: Van, Depot, Doorstep,
   Ship, Plane, Catapult), material budget and par. One line of flavour from the customer.
2. **Pack** (untimed, the puzzle). A front-on view of the open box. The item tray holds the items
   that *must* be delivered; the materials shelf holds padding, dividers, shelves and straps with
   limited counts. Items snap to the box grid and must rest on something.
3. **Seal.** Flaps fold, tape gun screeches across, label slaps on. About 2.5 seconds, skippable.
4. **Journey** (about 15 to 30 seconds). The box rides each leg of the route in a 3D diorama. The
   front of the box is cut away so you can watch the contents slide, bounce, roll, topple, sneeze.
   Director camera: establishing shots, follow cam, slow motion before each incident.
5. **Reveal.** The parcel lands on the customer's table. Tape is sliced, flaps burst open, and
   each item rises out with a rubber stamp: PERFECT / CHIPPED / SHATTERED / SPILLED / WIDE AWAKE /
   MELTED / POPPED / SCORCHED / SQUISHED.
6. **Results.** Customer review (stars + funny quote keyed to what happened), the three goal
   stars, buttons for **Repack**, **Replay** (scrub timeline, incident markers, speed, camera) and
   **Next**.

## 4. Mechanics in detail

### 4.1 The box and the grid

- The interior is a grid of `W × H` cells (3×2 in the first delivery, 8×5 at the end). One cell is
  0.25 m in the world.
- Items are rectangles of whole cells (1×1, 1×2, 2×1, 2×2). Most can be rotated 90° (R / right
  click / wheel). Creatures and the dragon can be flipped to face left or right.
- **Support rule:** everything must rest on the floor, a shelf, or another piece. When you hover,
  the ghost slides down to the first supported spot under the cursor (you can still tuck pieces
  under overhangs). Balloons are the exception: they rest against the ceiling or the underside of
  something.
- Picking up a piece that supports others makes those settle down (animated).

### 4.2 Materials (the toolbox)

| Material | Size | Cost | Behaviour |
| --- | --- | --- | --- |
| Crumpled paper | 1×1 | 1 | Light filler. Softens hits a little (hardness 0.6). **Flammable.** |
| Bubble wrap | 1×1 | 2 | Soft (hardness 0.3). **Pops** under a big jolt or a sharp neighbour (then hardness 0.7). Melts in fire. |
| Foam block | 1×1 | 3 | Softest (hardness 0.18), grippy, **fireproof**. |
| Divider | full-height wall on a grid line | 2 | Static cardboard wall. Blocks movement and heat. |
| Shelf | horizontal board on a grid line, spans the compartment | 2 | Static. Holds weight off whatever is below. |
| Strap | attached to an item | 3 | Holds the item fixed to the box. **Snaps** if the load exceeds its strength. |

Padding is placed with a paint-drag (hold and sweep across cells). Counts are limited per delivery.
The *cost* of what you used is compared with the delivery's **par** for the second star.

### 4.3 Items and their quirks

Every item has a **jolt limit** (the biggest sudden change in velocity it can take). Jolt is
measured from collision impulses and scaled by the hardness of what it hit, so hitting foam hurts
far less than hitting a wall. Quirks are shown on the item card with an icon and one short line.

| Item | Size | Quirk | Teaches |
| --- | --- | --- | --- |
| Teacup | 1×1 | Fragile (limit 9). | Padding basics |
| Book stack | 2×1 | Heavy, grippy, sturdy. Must still arrive. | Heavy things slide too; use as structure |
| Teddy bear | 1×1 | Soft (acts as padding), sturdy. | Items can protect items |
| Vase | 1×2 | Fragile (8). **Tall: topples** if shoved with nothing beside its shoulder. Toppling onto something hard breaks it. | Side support |
| Bowling ball | 1×1 | Very heavy, rolls freely. Hits like a truck. | Dividers, compartments |
| Snoozles the armadillo | 1×1 | **Asleep, rolls over** every few seconds. Wakes up on a hard jolt (limit 7) and scrambles about. Must arrive asleep. | A cozy nook |
| Magnet (pair) | 1×1 each | **Attract** each other (and metal) within 4.5 cells. Slam together. | Straps, distance |
| Potion | 1×2 | **Must stay upright.** Topples like the vase; toppling = spilled. | Shoulder support, shelves |
| Birthday cake | 2×1 | **Squishable:** nothing heavier than padding may rest on it. | Shelves |
| Balloon | 1×1 | **Floats** up. Pops on sharp things, fire, or a big squeeze. | Upside-down thinking |
| Cactus | 1×1 | **Sharp:** pops balloons and bubble wrap it touches, wakes creatures. Pot is fragile-ish. | Material choice |
| Clank the wind-up robot | 1×1 | **Walks** forward, turns around when blocked, pushes things. Metal (magnets pull it). | Containing movers |
| Ice swan | 1×2 | **Melts** if it spends 2 s within 1 cell of something hot (dividers block heat). Fragile (9). | Separation |
| Lava lamp | 1×2 | **Hot.** Tall, upright, fragile glass. | Heat auras |
| Bouncy ball | 1×1 | **Super bouncy**, keeps its energy. | Fill the space |
| Snow globe | 1×1 | Very fragile (6). | Gentle compartments |
| Frog | 1×1 | **Hops** every few seconds. Sturdy itself, but stomps whatever it lands on. | Ceilings (shelves) |
| Ember the tiny dragon | 2×1 | **Sneezes fire** when jolted (limit 6), facing left or right, 3 cells long: burns paper, melts bubble wrap and ice, pops balloons, toasts cake. Recoils. Hot. | Facing, fireproofing |
| Dragon egg | 1×1 | Very fragile (6), **must be kept warm**: within 1 cell of a heat source for 60% of the trip. | Finale combo |

### 4.4 The journey (route legs and events)

The route is a list of **legs**, and each leg is a list of **events**. Each event moves the box
through the world (position + tilt) with an exact kinematic curve. The simulation feels what the
box feels: gravity rotated into the box frame minus the box's acceleration. Contents float in a
free-fall, slam into the floor on landing, and slide forward when the van brakes. What you see the
box do is precisely what the contents experience.

| Leg | Set piece | Events |
| --- | --- | --- |
| Van | Delivery van (cutaway) on a country road with parallax scenery | depart, cruise, bump, pothole, speed bump, cobblestones, hill, hard brake, stop-and-go |
| Depot | Sorting depot with conveyors, a robot arm and a chute | conveyor rumble, belt drop, robot-arm grab (box tipped onto its side and back), chute slide, bin landing |
| Doorstep | Street, stairs and porch with the courier | carried up stairs, porch toss (arc + spin), dropped on the mat |
| Ship | Ferry deck in a swell | slow rocking ±18°, wave slam |
| Plane | Cargo hold | turbulence, air pocket (0.8 s of zero-g, then slam) |
| Catapult | Mossbury Express Catapult to the wizard's tower | launch (huge acceleration), zero-g flight with spin, haystack landing |

### 4.5 Simulation (deterministic, simplified 2D)

- Custom engine in pure C# (`HWC.Sim`, no UnityEngine dependency). Fixed step 1/240 s,
  axis-aligned rectangles that never rotate inside the sim. Rolling, wobble and topple rotations
  are presentation on top of exact sim positions.
- Sequential-impulse solver with warm starting, Coulomb friction, restitution, split position
  correction. Body order is fixed, so there is no randomness, no Unity physics and no frame-rate
  dependence. Quirk randomness comes from a seeded integer RNG.
- Frame effects: each tick, free bodies receive `R⁻¹·g·dt − R⁻¹·Δv_box` (gravity rotated into the
  box frame, minus the box's velocity change). Rotation-induced Coriolis and centrifugal terms are
  ignored on purpose (legibility).
- **Jolt:** for each contact, `Δv = normal impulse / mass × hardness(contact)`, summed over a
  3-tick window (12.5 ms). The peak jolt of each item is recorded and compared with its limit.
- **Load (squish):** normal impulses from bodies above (relative to current gravity) / dt / g give
  a "mass on top" value. Over the limit for 0.25 s squishes.
- **Topple:** tall upright items tip when the sideways pseudo-acceleration ratio exceeds w/h or
  they take a sideways knock, *unless* something is beside their shoulder on that side. The item
  is replaced by its lying-down rectangle in the nearest free spot. Presentation animates the
  rotation.
- **Straps** make a body fixed to the box. Every tick the strap's load (field forces + pseudo
  gravity × mass + contact impulses/dt) is compared with its strength; over it, it snaps.
- The whole journey is simulated at once when you seal (tens of milliseconds). It produces a
  **Recording**: 60 Hz frames (position, state flags per body) and an **incident list** (time,
  body, kind, magnitude, cause, the event that was running). The journey, replays, the director
  camera, results, customer reviews and last-run trails all read the Recording.

### 4.6 Goals and scoring

- **★ Delivered:** every item meets its requirement (intact, upright, asleep, not melted, not
  popped, warm enough…).
- **★ Under budget:** material cost ≤ par.
- **★ Handled with care:** every item's peak jolt stayed under 50% of its limit.
- Failing still shows the reveal (the comedy is the reward) and offers Repack with trails.
- A delivery unlocks the next one when it is delivered. Chapters open when the previous chapter is
  complete. Total stars unlock tape designs: Kraft (start), Red Stripe (10★), Polka (20★),
  "FRAGILE" print (30★), Dragon scale (45★), Gold (60★).

## 5. Content: twenty handcrafted deliveries

Four chapters (shifts) of five. Each chapter adds a route leg and new quirks, and ends with a
combination puzzle. Box sizes grow from 3×2 to 8×5.

### Chapter 1 — First Shift (route: Van)

| # | Delivery | Items | New idea | Purpose |
| --- | --- | --- | --- | --- |
| 1 | **A Cup for Edna** | Teacup | Place, paper, seal, watch | Guided tutorial. An unpadded cup slides into the wall at the brake and shatters; surrounded by paper it survives. |
| 2 | **Bookends** | Teacup, 2 book stacks | Heavy pieces slide; build structure | Books make walls; fill the rest. Shows that mass matters. |
| 3 | **The Tall Vase** | Vase, Teddy | Bubble wrap, toppling | Shoulder support. Teddy is a soft item that pads. |
| 4 | **Strike!** | Bowling ball, 2 teacups | Dividers | Stop-and-go traffic. Wall the ball off. |
| 5 | **Snoozles** | Armadillo, Vase | Foam, creatures | Cozy nook: a sleeping roller on cobblestones. Chapter test. |

### Chapter 2 — The Sorting Depot (route: Van + Depot)

| # | Delivery | Items | New idea | Purpose |
| --- | --- | --- | --- | --- |
| 6 | **Opposites Attract** | 2 magnets, teacup | Straps, magnets | Keep the magnets apart or strap one. The cup must not be between them. |
| 7 | **Potion Commotion** | Potion, 2 book stacks, teacup | Upright items | The belt drop and chute tilt push sideways. |
| 8 | **Happy Birthday, Timmy** | Cake, balloon, book stack | Shelves, floaters | Nothing on the cake; the balloon goes on top. |
| 9 | **A Prickly Situation** | Cactus, balloon, teacup | Sharp items | The robot arm tips the box on its side. Keep balloon and bubble wrap away from spikes. |
| 10 | **Magnetic Personality** | 2 magnets, armadillo, vase | Combination | Chapter test. |

### Chapter 3 — Last Mile (route: Depot + Doorstep, sometimes Van)

| # | Delivery | Items | New idea | Purpose |
| --- | --- | --- | --- | --- |
| 11 | **Clockwork Clank** | Robot, 2 teacups, magnet | Walkers | Contain a pusher; the magnet drags it. |
| 12 | **Swan Song** | Ice swan, lava lamp, teddy | Heat | Separate with distance or a divider; both are tall. |
| 13 | **Boing** | Bouncy ball, snow globe, vase | Bouncers | Stairs = many bumps. Fill the space. |
| 14 | **Hoppy Delivery** | Frog, cake, 2 teacups | Hoppers | A shelf as a ceiling over the frog. |
| 15 | **Ember** | Dragon, balloon, book stack | Fire | Face the dragon at something fireproof; foam not paper. Chapter test. |

### Chapter 4 — Express Service (routes: Ship, Plane, Catapult)

| # | Delivery | Items | New idea | Purpose |
| --- | --- | --- | --- | --- |
| 16 | **High Seas** | Potion, armadillo, bowling ball | Rocking ship | Long sideways tilts both ways. |
| 17 | **Air Pocket** | Balloon, 2 magnets, snow globe | Zero-g | In free fall, unsupported things float into each other. |
| 18 | **The Vase and the Dragon** | Vase, dragon, cactus, teacup | Trailer moment | A perfect vase survives; the dragon sneezes anyway. |
| 19 | **Museum Piece** | Vase, snow globe, ice swan, lava lamp, teacup | Everything fragile | Big box, tight budget. |
| 20 | **The Dragon Egg** | Dragon egg, Ember, 2 magnets, balloon | Keep warm + everything | Catapult express. The egg hatches in the reveal. |

Every delivery ships with a designer **reference solution** in data. Automated validation proves
each one is solvable (see §11).

## 6. Difficulty curve

- Deliveries 1–3: one idea each, generous materials, short gentle routes, par = reference cost + 2.
- 4–10: two interacting ideas; materials only slightly above what is needed; par = reference cost.
- 11–15: new movers (walker, hopper, sneezer) that create forces from inside the box.
- 16–20: harsh routes (rotation, zero-g, catapult) and 4–5 items, so the order of packing
  matters. 3-star solutions demand cleverness (an item used as padding, a strap instead of foam).
- Validation checks that a naive packing (items only, no materials) *fails* each level from 2 on,
  so no puzzle is trivial, and that each reference solution passes with margin.

## 7. Narrative beats

Told through sticky notes from **Mabel** (the head packer, dry and kind), order cards and customer
reviews. No cutscenes, no walls of text.

- **Shift 1:** "Welcome to Mossbury Parcel Post. We ship anything. Carefully." Edna's teacup.
  Mabel: "Paper is cheap. Teacups aren't." Ends with Snoozles: "Don't wake the armadillo."
- **Shift 2:** The depot is mechanised. "The robot arm has no feelings. Pack accordingly."
  Professor Quill's magnets, Pip the wizard's potions, Timmy's birthday.
- **Shift 3:** The courier, Dash, is fast and careless. "Dash says the stairs are 'basically a
  ramp'." Ember the tiny dragon arrives as a stray who sneezes at everything; Mabel adopts it.
- **Shift 4:** Express service by sea, air and catapult to the wizard's tower. Ember rides along
  in the trailer-moment delivery. The finale: the Royal Hatchery's dragon egg must arrive warm and
  whole. Ember is the heater. The egg hatches in the reveal; the hatchling sneezes. Credits roll
  over a wall of customer reviews.

## 8. Art direction

- **Look:** warm, tactile toy diorama. Chunky bevelled shapes, slightly exaggerated proportions,
  soft rounded silhouettes; nothing razor sharp except the cactus.
- **Palette:** kraft cardboard `#C8955A`/`#A8743F`, cream paper `#F3E9D2`, postal red `#D9483B`,
  teal `#2F8F8B`, ink navy `#1F2A44`, sticky-note yellow `#F6D55C`, plus saturated toy colours
  for items. Exteriors: soft sky blue, sage greens, peach dusk.
- **Lighting:** URP Lit. Warm key directional light with soft shadows, cool ambient, a rim light
  for silhouettes. SSAO for contact grounding. Bloom on fire and glows. Neutral tonemapping with a
  warm grade and a gentle vignette. Depth of field in the reveal.
- **Box:** cutaway (front panel removed) with corrugated cut edges, procedural kraft texture with
  fibres, printed "MOSSBURY PARCEL POST" stamp and arrows, tape strips, flaps that animate.
- **Camera:** packing uses a near-orthographic telephoto front view (FOV 22) for readability.
  Journey uses a 3/4 follow camera (FOV 35) plus a director with wide, medium and close shots.
- **UI:** shipping-label and packing-slip styling: cream paper panels, red rubber-stamp buttons,
  yellow sticky notes, Fira Sans Compressed Heavy headers and Fira Sans body (SIL OFL, bundled).
  Icons drawn procedurally; item thumbnails rendered from the Blender models.

## 9. Audio direction

All audio is synthesized with Python/numpy (Blender's bundled Python) into WAV files.

- **Music:** cozy jazzy lo-fi for packing (electric piano chords with voice leading, plucked
  upright bass, brushed drums, vinyl hiss); a bouncy "caper" cue for journeys (marimba, pizzicato
  bass, snappy drums); a title theme; short stingers for success, failure and three stars.
- **SFX:** cardboard thumps (sized), item place, paper crinkle, bubble-wrap squeak and pop, foam
  squish, divider slide, strap ratchet and snap, tape gun screech and snip, flap fold, label slap,
  stamp, glass shatter, ceramic crack, balloon pop, magnet clank, snores, armadillo yelp, dragon
  "ah… ah… CHOO" with fire whoosh, fire crackle, frog ribbit, robot whirr, ice crack, boing,
  van engine and road rumble, conveyor hum, chute slide, footsteps, knife slice, reveal chime,
  star dings, UI click/hover, paper shuffle.
- **Mix:** music ducks under impacts and reveals, journey impacts scale with jolt, a low-pass
  muffle on the music while paused.

## 10. UI/UX and controls

| Input | Action |
| --- | --- |
| Left click / drag | Pick an item or material and place it; drag to paint padding |
| Right click / R / wheel | Rotate (or flip facing) the held piece |
| Click a placed piece | Pick it back up |
| Shift + click | Remove a placed piece back to the tray |
| Z / Y (Ctrl optional) | Undo / redo |
| Space / Enter | Seal & ship (when every item is in) |
| Esc | Drop held piece / pause menu |
| During journey | Space pause, ←/→ scrub (replay), 1–4 speed, C camera, Enter skip |

Screens: Title (box on a desk, logo stamped on), Level select (a corkboard of order cards by
shift, stars on each), Order briefing, Packing HUD (tray, materials, cost vs par, seal button,
item card on hover), Journey HUD (leg banner, timeline, incident popups), Reveal, Results, Replay,
Pause, Settings (master/music/SFX volume, fullscreen, screen shake, reduced motion, show grid),
Tape designs, Credits. Transitions are a fast cardboard wipe.

**Onboarding without walls of text:** delivery 1 runs as a guided sequence of one-line sticky
notes pointing at the exact UI element (drag the teacup in → add paper → seal). Each new quirk or
material appears with a "NEW" note the first time. Item cards explain quirks with icon + one line.
Failures teach by showing.

## 11. Code architecture

```
Assets/Scripts/Sim/        pure C#, no UnityEngine (compiled by Unity AND by Tools/SimCheck)
  SimMath.cs               V2 struct, deterministic helpers, XorShift RNG
  Defs.cs                  ItemKind, MaterialKind, quirk parameters (the item catalog)
  Body.cs                  rigid AABB body state
  Physics.cs               broadphase, contacts, sequential impulses, position correction
  Quirks.cs                per-tick behaviours (roll, attract, walk, hop, sneeze, heat, float…)
  Route.cs                 legs, events, box kinematics (pose + velocity at time t)
  Packing.cs               grid model, placement validation, support/settle, cost
  Levels.cs                20 delivery definitions + reference solutions + text
  Simulator.cs             runs a packing through a route → Recording
  Recording.cs             frames, incidents, outcome, stars, review key
Assets/Scripts/Game/       Unity runtime
  Boot.cs                  RuntimeInitializeOnLoadMethod: builds the whole game from code
  GameFlow.cs              state machine Title → Select → Brief → Pack → Seal → Journey → Reveal → Results
  PackingController.cs     input, ghost, placement, painting, undo/redo, last-run trails
  JourneyPlayer.cs         plays a Recording onto views; director camera; replay controls
  Stages/*.cs              set pieces per leg (van, depot, doorstep, ship, plane, catapult)
  RevealController.cs      unboxing sequence
  SaveData.cs              JSON save in persistentDataPath
  AutoPilot.cs             -hwcAutopilot: plays every level with reference solutions, screenshots
Assets/Scripts/Visuals/    ModelLibrary, MaterialLibrary, BoxView, PieceView, Fx, CameraRig, Post
Assets/Scripts/UI/         UiKit (runtime uGUI builders), Theme, screens
Assets/Scripts/Audio/      AudioDirector (pooled SFX, music crossfade, ducking)
Assets/Editor/             BuildScript, ProjectSetup (URP/renderer/post), import postprocessors
Assets/Resources/          Models (FBX), Audio (WAV), Textures, Fonts, Materials
ArtSource/                 Blender generator scripts (+ saved .blend), texture & audio generators
Tools/SimCheck/            dotnet console app: validates levels, determinism, naive-fails, pars
Tools/*.sh                 unity launcher, build, play, autopilot, asset rebuild
```

**Validation (automated):**
- `Tools/SimCheck` (Unity's bundled .NET 8 SDK) compiles the exact `Sim/*.cs` the game uses and,
  for every delivery: the reference packing is valid (in bounds, no overlap, supported, within
  material counts); it is **Delivered**; its cost ≤ par; a 3★ reference reaches all three stars;
  the naive packing fails; two runs give byte-identical recordings (determinism hash).
- The same validator runs inside Unity in batch mode (`-executeMethod`), proving that the
  Mono build agrees with .NET.
- `-hwcAutopilot` in the player loads each delivery, applies the reference packing through the
  real UI code path, seals, plays the journey at high speed, and saves screenshots + PASS/FAIL.

## 12. Asset list (Blender, scripted with bpy)

Shared library `ArtSource/hwc_lib.py` (bevelled boxes, rounded cylinders, lathe profiles,
materials named `col_RRGGBB` / `glass_` / `metal_` / `glow_` swapped for URP materials on import).

- **Box:** body with cut-away front, inner corrugation edge, 4 animated flaps, tape strip, label.
  Generated per box size at runtime from a 9-slice-like set of parts (corners, edges) so every
  W×H works.
- **Items (19):** teacup + saucer, book stack, teddy, vase, bowling ball, armadillo (curled +
  awake pose), magnet, potion, cake, balloon, cactus in pot, wind-up robot, ice swan, lava lamp,
  bouncy ball, snow globe, frog, dragon (sleepy + sneeze pose), dragon egg (+ hatchling).
  Broken variants: shards set (generic ceramic/glass chunks), squished cake, popped balloon.
- **Materials:** crumpled paper ball, bubble-wrap cube (+ popped), foam block (egg-crate),
  divider sheet, shelf board, strap with buckle.
- **Set pieces:** delivery van (cutaway), road tiles, trees, bushes, houses, fences, hills,
  clouds; depot conveyor, robot arm, chute, bin; doorstep stairs, porch, door, mat, courier; ferry
  deck + waves; cargo-plane hold; catapult + haystack + wizard tower; packing table + lamp; the
  customer's table for reveals.

Every model is render-checked in Blender (Eevee contact sheets) before import.

## 13. Game feel and juice list

- Pick-up: piece lifts with a squash-and-stretch pop, casts a soft shadow, tilts with mouse velocity.
- Place: drops the last few centimetres with an ease-out bounce, dust puff, *thup* pitched by size.
- Paint padding: rapid crinkles, each piece pops in with a scale overshoot.
- Invalid spot: ghost turns red and shakes gently, soft "nope" tick.
- Tape: tape mesh extends with the gun, screech pitch-bends, end snip, label slap with a stamp ring.
- Journey: camera shake scaled by jolt, impact rings, cardboard dust, motion blur streaks on fast
  pieces, zzz bubbles over sleepers, magnet field lines, heat shimmer, sneeze fire particles,
  shatter shards, balloon confetti, slow motion with a zoom before incidents, and a leg banner
  ("LEG 2 · SORTING DEPOT").
- Reveal: flaps burst with light rays, items float up one by one, rubber stamps slam down with
  ink splats, stars fill with a ding ladder, confetti on three stars.
- UI: buttons press down like rubber stamps, sticky notes peel in, cards tilt on hover.

## 14. Milestones

| # | Milestone | Exit criteria |
| --- | --- | --- |
| M0 | Setup | URP project, git, .gitignore, build script, empty Linux build runs |
| M1 | Sim core | Pure C# sim, SimCheck harness, test scenarios behave as intended |
| M2 | Prototype | Packing + seal + journey + results with primitive shapes; screenshots; feels good |
| M3 | Art pipeline | Blender library, box, items and materials modelled, render-checked, imported |
| M4 | Content | 20 deliveries with references, all validated, quirks complete |
| M5 | Journey | 6 set pieces, director camera, replay, trails |
| M6 | UX | Title, select, brief, pause, settings, onboarding, reveal, results, save, tapes |
| M7 | Audio | All SFX and music synthesized, mixed, wired |
| M8 | Polish | Juice pass, post-processing, transitions, perf, bug sweep, autopilot clean |
| M9 | Ship | README, Linux build in `Builds/`, final verification |

## 15. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Physics feels floaty or random | Fixed step, tuned constants, grid-snapped placement, recorded jolts shown as numbers; prototype first |
| Float determinism differs between .NET and the Mono player | Same code compiled in both; validate in Unity batch mode too; reference solutions pass with margin; Mono scripting backend |
| Chaotic sensitivity makes puzzles feel unfair | Axis-aligned bodies (no rotation chaos), friction-heavy defaults, last-run trails |
| Twenty puzzles is a lot of tuning | Data-driven levels + SimCheck with references and naive-fail tests; tune numbers, not code |
| Art volume (19 items, 6 set pieces) | Shared bpy library, consistent stylized primitives, contact-sheet review |
| Editor GUI on Wayland / libxml2 | Local libxml2 shim (`Tools/.libs`), batch mode builds, `-force-wayland` player |
| Shared machine load | Batch mode, headless Blender with limited threads, close the Editor when idle |
| Music quality from pure synthesis | Simple, tasteful arrangements; reverb via convolution; mastering pass |

## 16. The 5-minute prototype test

A new player should, within five minutes:

1. Drop a teacup into a box (it *thups* in), stuff paper around it with a swipe, seal it to a
   satisfying tape screech, and watch it ride a bumpy van. It survives. The customer is delighted.
2. On delivery 2 or 3, fail once: the books slide and crush the cup, or the vase topples at the
   brake and shatters in slow motion. The reveal shows SHATTERED with a funny review.
3. Repack. The last run's trail shows the vase tipping right at 0:06. Add one bubble wrap beside
   its shoulder. Seal. It survives, and they get two stars, not three ("Handled with care" missed
   by a hair).

**What must make them ask to play again:** the gap between "it survived" and "three stars" is
visible and small; the next box has an armadillo in it; and the failure they just saw was funny
enough to want to see what the dragon does.
