# Handle With Care — Improvement Plan

Written 2026-10-06 on the `improvements` branch, from v0.1.0 (commit 4915cce). This is a plan only;
nothing below has been implemented yet.

## Baseline (what was run, what it showed)

| Check | Result |
| --- | --- |
| `Tools/simcheck.sh check` | **ALL OK**: 20/20 references valid, delivered, under par, 3★, deterministic; the items-only packing fails on all 19 deliveries where that is checked |
| `Tools/unity.sh build-linux` (batch, fresh) | **Succeeded** in 73 s: 245 MB, 0 errors |
| `Tools/autopilot.sh` (built player, real packing code) | **20/20 PASS**, every hash matches the .NET validator, ~66 s |
| `Tools/tour.sh` (menus + delivery 1 with real mouse/keyboard events) | **PASS** (drag-painted paper, sealed with Space); no exceptions or errors in any player log |
| `Tools/shots.sh 20 ref` (Dragon Egg) | PASS, 13 screenshots reviewed (packing, catapult, doorstep, unboxing) |
| Tour at 1280×800 (16:10, MacBook / Steam Deck shape) | Layout holds: menus, packing HUD, results and pause all fit |
| `-hwcFps` probe | **Not meaningful**: the shared iGPU was at 100% busy from other sessions (11 fps everywhere). The probe also reports GPU time as 0.0 ms here, so it can't separate GPU from CPU cost. Main thread ~6.5 ms during a journey. |
| `simcheck solve` on 10 deliveries | The solver converges on the shipped references. Every par sits **2–3 above the cheapest known 3★ cost** (e.g. #8 costs 1 vs par 4, #9 costs 2 vs par 4) |
| GitHub release v0.1.0 | Linux zip only (181 MB). The blog lists Windows · macOS · Linux |

Other things found while reading the code and running the game:

- The Standalone bundle identifier is still the URP template's
  (`com.Unity-Technologies.com.unity.template.urp-blank`), and no application icon is set, so the
  game ships with the default Unity icon. On Linux nobody sees either; on macOS both show up
  (Dock, Finder, Gatekeeper prompt, save location).
- Deliveries unlock strictly in order (`IsUnlocked(n) = IsDelivered(n-1)`), and there are no hints.
  A player stuck on, say, #16 High Seas can't progress at all, even though every delivery has a
  reference solution in data.
- The results screen and the unboxing have no keyboard shortcuts. Repack, Replay, Next and the
  reveal's Skip are mouse-only, which slows the "one more try" loop and blocks keyboard-only play.
- After delivery 20 the game rolls credits, and there is nothing new to do except chase stars.
- `Application.targetFrameRate = 120` is hard-coded, and there is no VSync, frame-cap or resolution
  setting. `runInBackground` is on, so the game keeps running (and playing music) when unfocused.
- No gamepad or touch code anywhere (`Mouse.current` and `Keyboard.current` only). The uGUI
  buttons are built at runtime without explicit navigation.

## Ranked improvements

Impact is for a real player. Effort: S ≈ under half a day, M ≈ about a day, L ≈ several days. Risk is
the chance of breaking something that works now.

| # | Improvement | Impact | Effort | Risk | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | **macOS release build + release packaging** | High (reach) | S–M | Low–Med | The Mac module is installed. Universal (x86_64 + arm64) Mono build, real bundle ID, generated app icon, `Tools/package.sh` zips. It can't be run on a Mac here, so that stays unverified. |
| 2 | **Ask Mabel: hints from the reference packings** | High (stuck players can't progress at all today) | M | Low | Escalating hints after a failed trip: first which item is at risk and where it belongs, then its exact ghost, then the dividers and shelves. Uses `LevelDef.ReferencePacking()`, no new solving at runtime. |
| 3 | **Shift 5 "Overtime": five new deliveries proved by SimCheck** | High (depth, post-game) | M–L | Med | New combinations of existing quirks (fire vs ice, a hopper with a balloon, a walker with a cake, the egg kept warm by the lava lamp, bouncers with magnets). No new art. Unlocks after the finale. |
| 4 | **Faster retry loop + keyboard shortcuts after the trip** | Med–High (feel of "one more run") | S | Low | Enter/Space skips the unboxing; on Results, R repacks, Enter goes to Next, P replays; key hints on the buttons. Optional "quick unboxing" once a delivery has been opened before. |
| 5 | **Expert targets ("Mabel's best") + balance pass** | Med (replayability; addresses the README's "too cheap" note) | S–M | Low | Shows the best known cost from the solver as a fourth, optional stamp. The three stars and pars stay the same, so existing saves keep their meaning. Re-tunes A Prickly Situation so a lone divider isn't the whole answer, proved with the solver. |
| 6 | **Gamepad / Steam Deck support** (done in round 2) | Med–High for Deck owners, Low for desktop | L | Med–High | A cell cursor on the d-pad/stick for packing, face buttons for place/rotate/erase, and uGUI navigation on every runtime-built screen. It can be self-tested with virtual `Gamepad` devices in the autopilot. Too big for this round next to 1–5. |
| 7 | Display settings: VSync / frame cap, window size, pause on focus loss (done in round 2) | Med (laptops, battery, fans) | S | Low | Replaces the hard-coded 120 fps cap. |
| 8 | Dragon-egg finale framing (README known issue) | Low–Med (it's the finale) | S | Low | Pull the reveal camera back or raise its target for the hatch beat; verify with `shots.sh 20`. Can ride along with #4. |
| 9 | Windows build | High (reach) | S once the module exists | Low | **Blocked**: Windows Build Support isn't installed. A `BuildWindows` entry point can be added and left untested until it is. |
| 10 | Performance on a dedicated GPU + a real GPU-time probe | Med | M | Low | Can't be measured on this shared iGPU. Needs other hardware or an idle machine. |
| 11 | Audio listening pass | Med | M | Low | Needs human ears; measurement-only checks are already done. |
| 12 | Foliage realism | Low | L | Med | Requires a Blender rebuild (~40 min) for a modest gain on background scenery. |
| 13 | WebGL build | Low | L | High | **Not recommended.** The simulation runs on a worker thread (`Task.Run`), the baked maps are BC7, the URP setup uses SSAO, and the data is about 245 MB. All of those fit the web poorly. |
| 14 | Touch input | Low | L | Med | No mobile target is planned. |

## Round 1 scope

Five items, in implementation order. Every change keeps `simcheck check` and the autopilot green,
and the README is updated to match what actually shipped.

### A. Faster retry loop + keyboard shortcuts (#4, with #8 folded in)

- Enter, Space or Esc skips the unboxing straight to Results. On Results: `R` Repack, `Enter` Next,
  `P` Replay (Space stays reserved for the journey). Small key hints are shown on the buttons.
- Once a delivery has been unboxed before, later unboxings play faster (stamps arrive quickly). The
  dragon-egg finale always plays in full.
- The hatch beat in the dragon-egg reveal is fully in frame.

**Acceptance:** from sealing to the next packing screen without touching the mouse; nothing else
changes behaviour. **Verify:** extend the `-hwcMenus` tour with real key events (skip reveal, `R`
back to packing) and assert the phase changes in the log. Run `shots.sh 20` with an extra capture
at the hatch, and look at it.

### B. Ask Mabel hints (#2)

- After the first failed trip on a delivery, an **ASK MABEL** button appears on the packing HUD.
  Each press reveals one more step drawn from the reference packing: (1) a sticky note naming the
  item that failed and the idea it needs ("The vase needs a shoulder on the brake side"), (2) a
  ghost showing where that item goes, (3) the reference dividers and shelves, (4) everything.
- Hints used are recorded per delivery and shown in the delivery log as a small pencil mark. Stars
  are not taken away, since this is a cozy game.

**Acceptance:** on every delivery, following all hint ghosts reproduces a packing that validates and
earns 3★. **Verify:** an autopilot mode (`-hwcHints`) that plays all 20 deliveries purely by
placing the hinted ghosts through the real input path. It must be 20/20 PASS with hashes matching
SimCheck. Add a tour screenshot of each hint stage.

### C. Expert targets + balance pass (#5)

- Run `simcheck solve` at higher iterations for all deliveries and store each delivery's best known
  3★ cost as `Expert`. Show "Mabel's best: N" on the order card, and an EXPERT stamp on Results and
  in the log when the player matches or beats it. Pars and the three stars are unchanged.
- Re-tune A Prickly Situation (route, materials or layout) so a single divider no longer solves it,
  while it still teaches spikes vs balloon and bubble wrap.

**Acceptance:** SimCheck gains checks that each Expert cost is achieved by a stored 3★ packing and
that Expert ≤ Par; Prickly's one-divider packing fails in SimCheck. **Verify:** `simcheck check`
ALL OK; autopilot 20/20; the solver can't find a 3★ packing for Prickly that costs less than its
new Expert.

### D. Shift 5 "Overtime": five new deliveries (#3)

- Five deliveries that combine quirks the first 20 never put together, for example: Ember next to
  the ice swan (fire melts ice); the frog under a balloon; Clank the robot loose with a birthday
  cake; the dragon egg kept warm by the lava lamp instead of Ember; the bouncy ball and magnets on
  the ferry. Each comes with an order, Mabel's note and good/bad reviews, built only from existing
  models, routes and stages.
- Shift 5 unlocks after the finale. The delivery log fits five shift columns, the totals read
  /75 stars, the tape-unlock thresholds are re-checked, and credits still roll after #20.

**Acceptance:** every new delivery has a valid 3★ reference, its items-only packing fails, it is
deterministic, it has an Expert cost, and a random search of item-only packings delivers 0 times
(`simcheck explore`). **Verify:** `simcheck check` ALL OK at 25 deliveries; autopilot 25/25 PASS
with matching hashes; `shots.sh` contact sheets for each new delivery, reviewed by eye.

### E. macOS release build + packaging (#1, with #9 prepared)

- `BuildScript.BuildMac` (Universal, Mono) and `Tools/unity.sh build-mac` producing
  `Builds/Mac/HandleWithCare.app`. Set the bundle ID to `com.nearbycoder.handlewithcare` and the
  bundle version to 0.2.0, and add a generated app icon (script-rendered from the existing parcel
  art, committed like every other generated asset).
- A `BuildWindows` entry point is added too but can't be built here (see decisions below).
- `Tools/package.sh` produces `HandleWithCare-v0.2.0-linux-x86_64.zip` and `…-macos-universal.zip`
  (using `ditto`-compatible zip so the app bundle's permissions survive). The README gets per-OS run
  instructions, including the unsigned-app Gatekeeper step (right-click → Open, or
  `xattr -dr com.apple.quarantine`).

**Acceptance:** the Mac build succeeds with 0 errors; `file` reports a universal binary
(x86_64 + arm64); Info.plist has the right bundle ID, version and icon. **Verify:** the build log,
`file`/`plutil`-style inspection on Linux, and the icon in the Linux build. **Not verifiable here:**
actually launching on macOS. The README will say "built, not yet tested on a Mac" until someone does.

### Deferred to a later round

Gamepad / Steam Deck (#6) is the strongest next candidate, but at size L it doesn't fit alongside
A–E. Display settings (#7) are small and can be pulled in if time allows.

## Decisions for the owner

1. **Windows build:** install *Windows Build Support (Mono)* for 6000.6.2f1 in Unity Hub if a Windows
   zip is wanted. Until then the blog's "Windows" claim has no build behind it.
2. **macOS signing:** the Mac build will be unsigned and unnotarized (no Apple Developer ID here).
   Ship it like that with instructions, or wait for signing?
3. **Hints and stars:** the plan is that hints never cost stars (only a pencil mark in the log).
   Say so if hinted deliveries should be capped at 2★ instead.
4. **Version and release:** the plan bumps the version to 0.2.0 and prepares zips. Nothing is pushed
   or published without your go-ahead.

## Round 1 results (2026-10-06)

| Item | Commit | Verified by |
| --- | --- | --- |
| A. Faster retry loop | f72dc20 | `tour.sh` drives R / Space / Enter / P with real key events (10 PASS); repeat unboxing 4.5 s → 2.3 s; hatch framing before/after shots |
| B. Ask Mabel | 76bff86 | `simcheck check` (the full hint is 3★ on every delivery); `hintpilot.sh` clicks every stage and ships the ghosts: 25/25 three stars, hashes match |
| C. Mabel's best + Prickly | aceab88 | `simcheck check` (expert packings valid, 3★, ≤ par); Prickly: one divider no longer exists, 0/400 random item-only deliveries, bubble wrap by the cactus pops |
| D. Shift 5 Overtime | a4a639b | `simcheck check` ALL OK at 25; `explore` 0 deliveries for every new one; `autopilot.sh` 25/25; `shots.sh` contact sheet reviewed; finale routing checked |
| E. macOS + packaging | dd17703 | Mac build succeeded; universal Mach-O, Info.plist and icon inspected on Linux; packaged Linux zip passes 25/25. **Not run on a Mac.** |

Screenshots: `docs/media/improvements/`. Deferred: gamepad / Steam Deck (#6) and display settings (#7).
Blocked on the owner: Windows Build Support module, Mac signing and notarization, publishing a release.

## Round 2 scope

Started 2026-10-06 on `improvements-2`, from main after round 1 (8eea65e). Four items, in
implementation order. Every item keeps `simcheck check`, `autopilot.sh`, `hintpilot.sh` and `tour.sh`
green; screenshots go to `docs/media/improvements/round2/`.

Also from round 1: the self-test scripts wrote to /tmp (a shared RAM disk here), and one hint run
produced zero-byte screenshots under heavy load.

### R2-A. Self-tests write inside the repo, and screenshots are checked (S)

- `autopilot.sh`, `hintpilot.sh`, `tour.sh`, `shots.sh` default to `Logs/selftest/<name>/` (gitignored)
  instead of /tmp, and cap the player log size.
- `Shot()` waits until the PNG is on disk and non-empty, and logs a FAIL if it never appears.

**Acceptance:** a full run of all four scripts leaves nothing new in /tmp and no zero-byte PNGs.
**Verify:** run them all; `find /tmp -newer <stamp>` from this repo's tools is empty; `find Logs/selftest -size 0`.

### R2-B. Gamepad and Steam Deck support (L, ranked #6)

- A gamepad drives a virtual mouse: the stick moves a software cursor, and in the packing view the
  d-pad steps it cell by cell. A = click / hold to paint, B = right click (put back, cancel), so
  every mouse action (menus, shelf, box, toolbar, SEAL) works unchanged.
- Shortcuts: X rotate, Y Ask Mabel, LB/RB previous/next material, LT undo, RT redo, Start pause.
  Journey: A pause, B skip, d-pad left/right speed. Unboxing: A/B skip. Review: X repack, Y replay,
  A next (the cursor also starts on NEXT).
- The OS cursor hides while the pad is in use and comes back when the mouse moves.

**Acceptance:** with only a gamepad, a player can go from the title screen through delivery 1 (pick,
place, paint, seal, skip, next) and use rotate, dividers and undo on a later delivery, plus pause
and settings. **Verify:** a new `Tools/padpilot.sh` (`-hwcPad`) adds a virtual `Gamepad` device and
sends only gamepad events, asserting each step; screenshots at 1280×800 (Steam Deck). Not
verifiable here: a physical Steam Deck or controller.

### R2-C. Prompts follow the input device (S–M)

- Key hints on buttons and the delivery-1 tutorial notes switch between keyboard/mouse wording and
  gamepad buttons (A, X, Y...) depending on the last device used. The README controls table gains
  a gamepad column.

**Acceptance:** with a pad, the hints read pad buttons; with mouse and keyboard they're unchanged.
**Verify:** padpilot and tour screenshots side by side.

### R2-D. Display settings (S–M, ranked #7)

- Frame-rate cap (30 / 60 / 120 / unlimited) and VSync, replacing the hard-coded 120; window size
  presets (including 1280×800); pause and muffle the game when the window loses focus (on by default).
  Settings are saved.

**Acceptance:** each setting changes the game when clicked and survives a restart.
**Verify:** the menu tour clicks them with real mouse events and logs `targetFrameRate`,
`vSyncCount` and the window size; a `-hwcFps` run with the 30 cap stays at or under 30 fps. Focus loss is
tested by calling the focus handler (a headless run can't take focus away from its own window).

## Round 2 results (2026-10-06)

| Item | Commit | Verified by |
| --- | --- | --- |
| R2-A. Self-tests in the repo, screenshots checked | 3c9d295 | All five self-test scripts write to `Logs/selftest/`; a full run left nothing of ours in /tmp and no zero-byte PNGs |
| R2-B. Gamepad and Steam Deck | ac4dfcc | `padpilot.sh`: a virtual gamepad only, at 1280×800, 28/28 steps (title → delivery 2, dividers, undo/redo, turning Ember, Ask Mabel, pause, settings). Also fixed: right-clicking the cell you just painted now erases it |
| R2-C. Prompts follow the device | 286c147 | Keyboard (`tour.sh`) and pad (`padpilot.sh`) screenshots: toolbar 1–6 vs LB/RB, Z/Y vs LT/RT, SPACE vs VIEW, R/P/ENTER vs X/Y/A, and the tutorial wording |
| R2-D. Display settings | 4a424da | `tour.sh` clicks each control with real mouse events: VSync off, 30 fps limit measured at 30.0 fps, the window resized to the chosen 2560×1440, the save round-trips, and focus loss pauses packing |

After the last commit: `simcheck check` ALL OK (25), `autopilot.sh` 25/25, `hintpilot.sh` 26/26,
`tour.sh` 18 PASS, `padpilot.sh` 28/28. Screenshots: `docs/media/improvements/round2/`.

Not verified here: a physical controller or Steam Deck (only a virtual pad), and real alt-tab focus
loss (the handler is called directly). The local macOS build wasn't rebuilt this round.

Still for the owner: Windows Build Support, Mac signing and notarization, publishing a release,
and a license.
