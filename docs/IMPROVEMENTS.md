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

## Round 3 scope

Started 2026-10-06 on `improvements-3`, from main after round 2 (63cea27). The ranked list is mostly
done or blocked: #9 Windows needs the module, #10 performance needs a quiet machine or other hardware,
#11 audio needs ears, #12 foliage is a 40-minute rebuild for background scenery, #13 and #14 are not
planned. So this round goes after what a player can lose, and what the round-2 tests didn't really
cover.

Found while planning:

- **Every self-test writes to the real config folder.** `SaveData.Disabled` keeps `save.json` safe,
  but the Unity player writes a prefs file on every launch (screen size defaults, session counters).
  *Corrected while building:* the player writes it to the shared `~/.config/unity3d/unknown/unknown/prefs`,
  not to the game's own folder; the game folder's `prefs` is rewritten by the Unity editor during a
  batch build (with the same content).
- **A damaged save loses everything.** `save.json` is rewritten in place. If the game is killed or
  the machine loses power mid-write, the next launch can't parse it, quietly starts a new game, and
  the first write after that overwrites the old file.
- **An unsealed packing is thrown away.** The box is only saved when you seal it. Pause → Main Menu,
  picking another delivery from the log, or quitting the game loses the layout you were building.
- **The delivery log doesn't say which star is missing.** Cards fill stars left to right, so a
  two-star delivery doesn't show whether the budget or the care star is the one to chase.

Every item keeps `simcheck check`, `autopilot.sh`, `hintpilot.sh`, `padpilot.sh` and `tour.sh` green.
Screenshots go to `docs/media/improvements/round3/`.

### R3-A. Self-tests never touch the real config (S)

- Every self-test script runs the player with `XDG_CONFIG_HOME` inside its `Logs/selftest/<name>/`
  folder, so Unity's prefs and any save land there.
- The scripts record a checksum of the real config folder before the run and fail if it changed.

**Acceptance:** a full round of self-tests leaves `~/.config/unity3d/Mossbury Parcel Post/` byte for
byte unchanged. **Verify:** checksums before and after every script; the sandbox folders contain the
prefs the player wrote.

### R3-B. Crash-safe saves (S–M)

- Saves are written to a temporary file and swapped in with a rename, keeping the previous save as
  `save.json.bak`.
- If `save.json` can't be read, it is moved aside (`save.corrupt-<time>.json`, never deleted) and
  the backup is loaded. The title screen says so in one line, or says the progress couldn't be
  recovered when there is no usable backup.

**Acceptance:** a truncated `save.json` comes back from the backup with progress intact; garbage with
no backup starts a new game without an exception and keeps the damaged file. **Verify:** a new
multi-launch self-test, `Tools/savepilot.sh`, which runs the player with saving switched on (only
inside its sandbox; the test mode refuses any other location), damages the save between launches and
checks what the next launch loads.

### R3-C. The box you were packing is kept (S)

- The unsealed packing is saved when you leave the packing screen (main menu, delivery log, another
  delivery), when the game quits or loses focus, and a couple of seconds after the last change. Coming
  back to the delivery restores it. Sealing still saves as before.

**Acceptance:** place pieces, go to the main menu and continue: same layout; place more, quit, launch
again: same layout. **Verify:** `savepilot.sh` across real restarts. The same test checks that round
2's display settings (VSync, frame-rate limit) and the other settings survive a real restart, which
round 2 only checked in memory.

### R3-D. The delivery log shows which star is missing (S)

- The three stars on each card stand for Delivered, Under budget and Handled with care, in that order,
  so a missing one shows as a gap.
- Hovering a card (mouse or gamepad cursor) shows a detail line on the board: each goal with your
  best (cost against par, peak jolt against the 65% line), Mabel's best, attempts, and hints used.

**Acceptance:** a two-star delivery missing only the budget star shows ★ ☆ ★ and names the best cost
against par. **Verify:** tour screenshots of the log with a seeded save (in the sandbox), with the mouse
and the pad cursor on a card.

### R3-E. Refresh the local macOS build (S)

- Rebuild `Builds/Mac/HandleWithCare.app` from this branch, so it includes rounds 2 and 3.

**Acceptance:** the build succeeds with 0 errors and is still a universal binary with the right
bundle id and version. **Verify:** build log, `file` on the binary, Info.plist. **Not verifiable
here:** running it on a Mac.

### Not in this round

Real alt-tab focus loss: there is no Xvfb, xdotool or nested compositor on this machine, and
driving focus on the owner's KDE desktop would be intrusive, so the handler is still tested by a
direct call. Physical controller and Steam Deck testing need the hardware.

## Round 3 results (2026-10-06)

| Item | Commit | Verified by |
| --- | --- | --- |
| R3-A. Self-tests keep the config in their own folder | a4d1dc4 | Every script sets `XDG_CONFIG_HOME` to `Logs/selftest/<name>/config` and checksums `~/.config/unity3d/Mossbury Parcel Post/` before and after: unchanged on every run this round. The player's prefs now land in the sandbox (`config/unity3d/unknown/unknown/prefs`) |
| R3-B. Crash-safe saves | 1796b0c | `savepilot.sh`, 28/28 three runs in a row: a save cut in half loads the backup (progress, settings and the box from one write earlier), the damaged file is kept, the title note shows once; garbage with no backup starts fresh and saves again; no `.tmp` left behind |
| R3-C. The unsealed box is kept | 1796b0c | `savepilot.sh`: Main Menu → CONTINUE restores the box; it is on disk 2 s after the last change; a piece placed 0.3 s before quitting is there after a restart. The same run checks that VSync off, the 60 fps limit, screen shake and background pause survive a real restart and are applied at launch (round 2 only checked this in memory) |
| R3-D. The delivery log shows which star is missing | 6839d23 | `tour.sh`: a two-star card missing the budget star shows gold, gap, gold; pointing at it reads "best 6 / par 4", trips and "hinted"; card 3 shows "best 80% / 65%". `padpilot.sh`: from pause, the d-pad opens the log and walks to card 1, and the line follows |
| R3-E. Local macOS build refreshed | (build only) | Built with 0 errors; universal x86_64 + arm64 Mach-O; `com.nearbycoder.handlewithcare`, 0.2.0; the new code is in `Assembly-CSharp.dll`. **Not run on a Mac.** |

Found and fixed along the way (in the commits above):

- **The d-pad did nothing in Settings or the Delivery Log opened from the pause menu.** The pause
  menu stays paused underneath, and the pad looked for targets in the hidden pause panel. Round 2's
  padpilot only pressed A and B there. padpilot now moves through both.
- **The Last trip report showed an empty box instead of ✗** (visible in README screenshot 09), and
  "✓ MATCHED" had the same problem: Fira Sans has no check or cross marks. They are now a
  multiplication sign and a dot, which every UI font has (checked against each font's charset).
- **Self-test clicks were sometimes dropped.** If the test window lost focus before the test
  coroutine set the input background behaviour, the mouse was disabled and every later click was
  ignored. Test runs now ignore focus from the first frame.
- One padpilot run failed its paint sweep mid-way (load average was about 40); the next six runs
  passed. The gamepad-only test now ignores the real mouse and keyboard and logs it when they move,
  but none of those runs logged any, so the cause isn't confirmed.

After the last commit: `simcheck check` ALL OK (25), `autopilot.sh` 25/25, `hintpilot.sh` 26/26,
`padpilot.sh` 33/33, `tour.sh` 21 PASS, `savepilot.sh` 28/28 (three runs). Nothing new of ours in
/tmp, no zero-byte screenshots. Screenshots: `docs/media/improvements/round3/`.

Mistakes and limits:

- One debug launch during R3-B ran without the sandbox. The game's folder was untouched (same
  checksums as before the round), but that launch rewrote Unity's shared
  `~/.config/unity3d/unknown/unknown/prefs`, as every self-test before this round did.
- Batch builds aren't sandboxed: the Unity editor rewrites the game folder's `prefs` with the same
  content. Sandboxing the editor's config would risk its licence and preferences, so it's left alone.

Still not verified here: a physical controller or Steam Deck, real alt-tab focus loss, a power cut
mid-write, and the Mac build on a Mac. Still for the owner: Windows Build Support, Mac signing and
notarization, publishing a release, and a license.

## Round 4 scope

Started 2026-10-06 on `improvements-4`, from main after round 3 (8147912). The ranked list is done
or blocked (#9 needs the Windows module, #10 a quiet machine or other hardware, #11 human ears, #12 a
40-minute art rebuild for background scenery, #13 and #14 are not planned). This round looks again at
what a player runs into, from reading the code and the round 3 screenshots:

- **Your best packing gets overwritten.** The save keeps one packing per delivery: the last one
  shipped, and since round 3 also the unsealed box. A player who earns three stars and then tries for
  Mabel's best loses the three-star layout with the first experiment.
- **Undo and redo are swapped on German keyboards.** Shortcuts read physical key positions (US
  layout). On QWERTZ the key labelled Z sits where US has Y, so Z redoes and Y (and Ctrl+Z) undoes.
  On AZERTY the key labelled Z does nothing and the hint on the button still says Z.
- **Small text on handhelds.** The UI is laid out for 1920×1080 and scaled. At the Steam Deck's
  1280×800 the item cards, Mabel's notes, the order card and the log detail line (18–22 units)
  render at about 13–15 px. There is no way to make text bigger.
- **No way to start over.** Starting a fresh game means finding and deleting `save.json` by hand.

Every item keeps `simcheck check`, `autopilot.sh`, `hintpilot.sh`, `padpilot.sh`, `tour.sh` and
`savepilot.sh` green, and every self-test still leaves the real config folder unchanged. Screenshots
go to `docs/media/improvements/round4/`.

### R4-A. Your best packing is kept (S–M)

- Each delivery keeps the best packing you have shipped, apart from the box on the bench. Better
  means more stars, then lower cost, then a lower peak jolt.
- On the bench, a **MY BEST** button (with its stars) puts that packing in the box. It shows when a
  best exists and the box is different. Z undoes it, like any other change. The gamepad cursor
  reaches it.

**Acceptance:** after a three-star trip and then a failed one, MY BEST brings back the three-star
layout, and shipping it gets three stars again. Undo returns the failed layout. The best packing
survives a restart. **Verify:** `savepilot.sh`: after the restart, ship a failing packing, click
MY BEST with real mouse events, compare the box to the three-star one, undo and redo, ship it and
compare the outcome to the validator's. Plus a screenshot.

### R4-B. Shortcuts follow the keyboard's labels (S–M)

- Letter shortcuts (Z, Y, R, P, C) find the key by the label the keyboard layout reports, using the
  Input System's layout names, and fall back to the US position when the platform reports no names.
  The key hints on the buttons show the same labels.

**Acceptance:** with a German layout, the key labelled Z undoes and the key labelled Y redoes; with
French AZERTY, the key labelled Z undoes; with US, nothing changes. **Verify:** `tour.sh` sends real
key events for the physical keys while the layout names are swapped in through a test hook (the
platform can't be given another layout without changing the desktop's), checks undo and redo, and
logs what this Linux player reports for the real keyboard. **Not verifiable here:** a real non-US
layout on Linux, Windows or macOS.

### R4-C. Larger text (S–M)

- A **LARGER TEXT** setting. Body text (cards, notes, the order card, the log detail line, results
  labels, key hints) grows by up to about 30% wherever its box has room, and never shrinks below
  today's size. Saved like every other setting.

**Acceptance:** with it on, the small texts are larger and none of them overflows its box when it
didn't before. **Verify:** a self-test step that counts overflowing texts with the setting off and
on, and logs the sizes; `padpilot.sh` screenshots at 1280×800 of packing, an item card, the log,
settings and results, with the setting on, reviewed by eye.

### R4-D. Start over (S)

- **START OVER** in Settings, with a confirmation. It clears progress (stars, records, boxes, seen
  tips) and keeps the settings. The old save is kept as `save.erased-<time>.json`, never deleted.

**Acceptance:** cancelling changes nothing; confirming gives a fresh game with the same settings and
a copy of the old save on disk. **Verify:** `savepilot.sh` clicks both with real mouse events in its
sandbox and checks the files and the title menu.

### Not in this round

Controller rumble was considered, but Unity's Linux gamepad backend may not drive motors and nothing
here could feel it, so it would land untested. PlayStation button glyphs need a font or sprites for
the shapes. Physical controller and Steam Deck testing, real alt-tab focus loss and the Mac build on
a Mac still need hardware.

## Round 4 results (2026-10-06)

| Item | Commit | Verified by |
| --- | --- | --- |
| R4-A. Your best packing is kept | 612501b | `savepilot.sh` launches 4 and 5: a three-star trip becomes the best packing; after a restart, a failed trip doesn't replace it; MY BEST (with three gold stars) is clicked with real mouse events and puts the three-star layout back; Z and Y undo and redo it; shipping it gets three stars with the validator's hash. MY BEST is hidden while the box already holds the best |
| R4-B. Shortcuts follow the keyboard's labels | c9b214d | `tour.sh` presses physical keys while German, French and Russian labels are swapped in: QWERTZ Y undoes and Z redoes; AZERTY W undoes and Z does nothing; Russian (no Z label) keeps the US keys and the hints read Я / Н; US unchanged. The player log shows this Linux player reports the layout (`'us'`) and key names, so the lookup uses real platform data |
| R4-C. Larger text | 0fed5ca | `padpilot.sh` (pad only, 1280×800) reaches LARGER TEXT with the d-pad and checks four screens: settings, the bench (order card, hint note, item card), the delivery log and the results. 9–74 small texts per screen grow (about 1.2× on average, at most 1.3×), none shrinks, and none that fit before overflows. `savepilot.sh` checks it survives a restart |
| R4-D. Start over | c93f5d0 | `savepilot.sh` launch 6, real mouse clicks: KEEP MY PROGRESS changes nothing and writes no copy; START OVER leaves a fresh game ("START SHIFT"), keeps a setting changed just before (grid off) in memory and on disk, and keeps `save.erased-<time>.json` with the old progress and best packing |

After the last commit, on one build: `simcheck check` ALL OK (25), `autopilot.sh` 25/25, `hintpilot.sh`
26/26, `padpilot.sh` 38/38, `tour.sh` 25 PASS, `savepilot.sh` 49/49 over six launches. Every run
left `~/.config/unity3d/Mossbury Parcel Post/` unchanged. No zero-byte screenshots. Load average
during the input-driven runs: 21–28 (padpilot also passed once at 33). Screenshots:
`docs/media/improvements/round4/`.

Limits and notes:

- **Larger text** only grows text as far as its own box allows. Mabel's longer hint notes already
  fill the sticky note, so they stay the same size. Making the note itself bigger would mean
  re-laying out the bench's right column.
- **Keyboard layouts** are tested with swapped labels, not with a real German or French keyboard on
  Linux, Windows or macOS.
- **MY BEST** needs a delivered trip made with this version: saves from before have no best packing
  stored.
- Esc doesn't close menus (it never has, only the pad's B does), so the start-over dialog is
  cancelled with KEEP MY PROGRESS or B.
- Unity's batch builds leave their usual IPC sockets in /tmp (`Unity-Upm-*.sock`,
  `Unity-LicenseClient-*.sock`). They are the editor's, possibly shared with other sessions, so they
  were left alone. The local macOS build was not rebuilt this round.

Still not verified here: a physical controller or Steam Deck, real alt-tab focus loss, a power cut
mid-write, and the Mac build on a Mac. Still for the owner: Windows Build Support, Mac signing and
notarization, publishing a release, a license, and a font with real check and cross glyphs.

## Round 5 scope

Started 2026-10-06 on `improvements-5`, from main after round 4 (e586636). The ranked list is still
done or blocked (#9 Windows module, #10 a quiet machine or other hardware, #11 human ears, #12 a
40-minute art rebuild, #13 and #14 not planned). This round picks up what round 4 left open and what a
player still runs into:

- **Esc doesn't close menus.** Settings, the delivery log, the credits and the start-over question
  only close with a click or the pad's B. Esc is the first key most players try.
- **Mabel's hint notes stay small.** Hint notes are longer than her intro notes, so the sticky note
  shrinks them to fit (down to 15 units, about 11 px at 1280×800), and LARGER TEXT can't grow them.
- **PlayStation pads show Xbox letters.** A DualShock or DualSense player reads "A", "X", "LB" and
  "VIEW". Round 4 deferred this for want of sprites or a font. The UI is built in code, so the button
  shapes can be drawn in code too, with no font and no new licence.
- **The Last trip report uses stand-ins for ✓ and ✗** (a multiplication sign and a dot), because
  Fira Sans has no check or cross. Drawn marks fix that the same way.
- **Players coming from v0.1.0 get no MY BEST.** The only released save format keeps the last
  shipped packing per delivery. The simulation is deterministic, so that packing's result can be
  worked out again on the bench and kept as the best, with its real stars.

Every item keeps `simcheck check`, `autopilot.sh`, `hintpilot.sh`, `padpilot.sh`, `tour.sh` and
`savepilot.sh` green, and every self-test still leaves the real config folder unchanged. No new
delivery this round. Screenshots go to `docs/media/improvements/round5/`.

### R5-A. Esc backs out of menus (S)

- Esc does what the pad's B does on the screen on top: the start-over question keeps your progress,
  Settings is DONE (saved), the delivery log and credits go BACK (to the pause menu when opened from
  there). Pause and resume with Esc keep working as before.

**Acceptance:** with real key events, Esc closes each of those screens to the right place, and a
single press never also opens or closes the pause menu underneath. **Verify:** `tour.sh` steps
that press Esc on the start-over question, Settings (from the title and from pause), the log and
the credits, and check the screen and the pause state after each.

### R5-B. Mabel's notes grow to fit (S–M)

- The sticky note gets taller when its text would otherwise shrink below a readable size (19 units
  normally, the full 30% larger with LARGER TEXT), and ASK MABEL moves down with it. The note no
  longer catches clicks, so it never hides a shelf item from the mouse.

**Acceptance:** the longest hint note of every delivery is shown at 19 units or more (about 23 with
LARGER TEXT), the note stays clear of the seal button and the materials meter, and the hint button
is still reachable. Intro notes look as they do now. **Verify:** a padpilot step at 1280×800 that
goes through all four hint stages on a delivery and logs the note's font size, height and overlaps
with the setting off and on; `hintpilot.sh` still clicks every hint (25/25); screenshots by eye.

### R5-C. PlayStation button shapes, and real ✓ and ✗ (M)

- A small sprite sheet drawn in code at start-up (cross, circle, square, triangle, a check and a
  cross mark) is used by TextMeshPro inline. With a DualShock or DualSense, prompts read ✕ ○ □ △,
  L1/R1, L2/R2, OPTIONS and SHARE / CREATE, in the hints on buttons, the seal hint and Mabel's
  tutorial notes. A BUTTON ICONS setting (AUTO / XBOX / PLAYSTATION) covers pads that don't say
  what they are.
- The Last trip report and MATCHED use the drawn ✓ and ✗.

**Acceptance:** with a virtual DualShock 4 the prompts show the PlayStation shapes and names; with
the Xbox-style pad they're unchanged; the setting overrides AUTO and survives a restart; the report
shows drawn marks. **Verify:** `padpilot.sh` with a second virtual pad of DualShock 4 layout (the
Input System's own layout), checking the prompt texts and taking screenshots; `savepilot.sh` for the
setting. **Not verifiable here:** a physical PlayStation controller (on Linux, how it reports
itself depends on the driver; the setting is the fallback).

### R5-D. MY BEST for saves from v0.1.0 (S–M)

- When a delivery has been delivered but has no best packing stored (a save from v0.1.0, where the
  stored box is the last shipped one), the bench simulates that packing once. If it delivers, it
  becomes the best packing with the stars, cost and care the simulation gives. The save version
  goes to 2, so this happens only for older saves.

**Acceptance:** an old-format save with a stored three-star packing shows MY BEST with three stars,
and shipping it gives the same outcome as the simulation; a stored packing that would fail is not
kept; a version 2 save is left alone. **Verify:** `savepilot.sh` with a v0.1.0-style save written by
the test (fields exactly as in v0.1.0's `SaveData`), checked against the validator's outcome.

### Not in this round

Rumble (nothing here can feel it), a physical controller or Steam Deck, a real non-US keyboard, real
alt-tab focus loss, the Mac build on a Mac.

## Round 5 results (2026-10-07)

| Item | Commit | Verified by |
| --- | --- | --- |
| R5-A. Esc backs out of menus | 81d0778 | `tour.sh`, real key events: Esc on Settings and the log opened from pause goes back to the pause menu (still paused), Esc there resumes; from the title, Esc keeps progress on the start-over question, closes Settings, the log and the credits, and does nothing on the title itself (13 steps) |
| R5-B. Mabel's notes grow to fit | 1dc1946 | `padpilot.sh` at 1280×800: all 300 hint notes (25 deliveries × 4 stages × each item) are shown at 19.2 units or more with LARGER TEXT off (54 needed a taller note) and 24.7 or more with it on (175 did); no intro note grows with it off; the tallest note clears the meter and the seal button, ASK MABEL moved below it still works with A; the note fades to 30% under the stick cursor and comes back. `hintpilot.sh` 26/26 with the moved button |
| R5-C. PlayStation shapes, drawn ✓ and ✗ | 6ba3106 | `padpilot.sh` adds a virtual pad with the Input System's DualShock 4 layout: prompts read L2 / R2 / SHARE / L1 / R1 and the triangle, the tutorial note says "press" with the cross; back on the Xbox-style pad, the letters return; GAMEPAD BUTTON ICONS reached with the d-pad forces PLAYSTATION; the review draws square, triangle, cross (checked by which sprite each prompt really draws, which caught a TextMeshPro bug that drew every name as the first shape); the Last trip report draws the cross. `savepilot.sh`: the setting survives a restart. `tour.sh` screenshot of the log's ✓ / ✗ |
| R5-D. MY BEST for v0.1.0 saves | a5ab55a | `savepilot.sh` launch 7 loads a save with exactly v0.1.0's fields (a whole story playthrough, one delivery last shipped items-only, one never delivered): nothing is simulated at launch; each bench keeps its last box as MY BEST with the simulation's result (18 of 18 that deliver), the failing and undelivered ones get none; MY BEST after EMPTY BOX, shipped: three stars, hash matches; version 2 on disk |

After the last commit, on one build: `simcheck check` ALL OK (25), `autopilot.sh` 25/25, `hintpilot.sh`
26/26, `padpilot.sh` 55/55, `tour.sh` 38 PASS, `savepilot.sh` 61/61 over seven launches. No exceptions in
any player log, no zero-byte screenshots, and every self-test reported the real
`~/.config/unity3d/Mossbury Parcel Post/` unchanged. Load average during the final input-driven runs:
17–24 (the first R5-A tour passed at 49). Screenshots: `docs/media/improvements/round5/`.

Limits and notes:

- **First bench of an old delivery.** Checking a v0.1.0 box simulates one journey when its bench
  first opens: 88–761 ms per delivery (952 ms for the first, with JIT) at load 24–37 on this shared
  machine, against 2 ms for a bench without the check. It happens once per delivery. Doing it at
  launch instead took 4.7 s for a full playthrough, so it was moved. A worker thread could hide it
  completely if it matters on slow machines.
- **Which boxes count.** Version 1 saves from the unreleased rounds 3 and 4 may hold an unsealed box
  rather than the last one shipped; it is still only kept if the simulation says it delivers, with
  the stars it really earns.
- **PlayStation detection** uses the Input System's DualShock / DualSense layouts, then Sony's vendor
  id or name in the device description. Only a virtual DualShock 4 was tested; a real pad on Linux may
  report itself as a plain gamepad, and then AUTO shows Xbox letters until GAMEPAD BUTTON ICONS is set.
- **Tall notes at 16:9.** At 1920×1080 a grown note can overlap the top-right shelf cubby. Clicks go
  through it and it fades under the pointer, but it does cover the item until then.
- The check and cross marks are drawn by the game, so the owner's font decision from round 4 is no
  longer needed for them (a font with those glyphs would still work if preferred).
- The sprite sheet is upgraded by TextMeshPro at start-up, which logs one "Upgrading sprite asset"
  line. The TextMeshPro sprite shader is now in the build's always-included shaders.
- Old `/tmp/hwc-*` files from 2026-10-04 are still on the shared RAM disk; they predate this round and
  weren't touched.

Still not verified here: a physical controller (Xbox-style or PlayStation) or Steam Deck, a real
non-US keyboard, real alt-tab focus loss, a power cut mid-write, and the Mac build on a Mac (not rebuilt
this round). Still for the owner: Windows Build Support, Mac signing and notarization, publishing a
release, and a license.
