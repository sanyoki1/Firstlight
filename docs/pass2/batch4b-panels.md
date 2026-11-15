# Pass 2 batch 4b: panels, numbers, controls, readability

Worktree `Downloads\ClaudeWorkspace\Firstlight-lane2`, branch `pass2-lane2`, from `97fba1f` (batch 4a). HUD, view and
camera only. The sim gets one new read-only partial file (`Settlement.Explain.cs`) and `SuggestCampfireSite` reads its
four reach/cap numbers from a table there (same numbers; reports byte-identical). No data, balance or save change.

## Fixes (one commit each, in order)
| # | Finding | Commit | Files | Shot (`docs/art/quarter/pass2/`) |
|---|---|---|---|---|
| 1 | U12 homeless "Fed, warm and well" | `e726909` | Sim/Settlement.Explain.cs (new, `ExplainHappiness`), Hud.Info.cs | p2-citizen-homeless |
| 2 | U13 two "Pottery" items | `30a8db9` | Hud.Agri.cs (`NeedLabel`, need tooltips), Hud.Info.cs, Hud.TopBar.cs | p2-needs-labels |
| 3 | U14 Jobs panel reasons | `420f2c8` (+ layout in `6397bc4`) | Hud.Panels.cs (`JobIdle`, `JobNote`) | p2-jobs-reasons, p2-layout-720p |
| 4 | U15 suggested site | `07ef53e` | Hud.cs (`SuggestedSite`, `Judged`, rings in `PushToView`), Settlement.Explain.cs (`CampfireReach`), Settlement.Buildings.cs (reads it) | p2-suggested-site |
| 5 | U21 two-click demolish tool | `c7aa239` | Hud.cs (`ToolConfirmPending`, `DemolishLoss`, tip), Hud.Info.cs (panel tooltip) | p2-demolish-confirm |
| 6 | U22 building hover tag | `14f0bf9` | Hud.cs (`BuildingTag`, cached 4x a second), View/SettlementView.cs (faint ring) | p2-hover-building |
| 7 | U26 + U30 paused tag, Home/H, zoom keys | `b9542e3` | Hud.cs (`DrawPausedTag`, keys), Core/OrbitCamera.cs (PgUp/PgDn, +/-), Hud.Panels.cs (F1) | p2-paused-tag, p2-help-1080p-160 |
| 8 | U24 U25 U27 U28 | `6b5efef` | Hud.Agri.cs (end/empty screens), Hud.cs + Core/OrbitCamera.cs (Follow), Hud.Info.cs, Hud.Menus.cs | p2-end-screen-buttons (load list: runs/pass2/b4b/panelshots3/24) |
| 9 | U20 contrast and size | `6397bc4`, `6253152` | UiTheme.cs (Faint 0.64, Scale floor, `FontFloor`, `Px`), Hud.BuildMenu/Guide/Events/Info/Agri/Menus/Research/Panels/TopBar.cs | p2-events-dates, p2-layout-720p, p2-layout-720p-160, p2-help-1080p-160 |
| - | screenshot driver | `751225a` | GameController.PanelShots.cs (new, `-panelshots`), GameController.cs, Flow.cs, DataPaths.cs, UiShots.cs (`_shotKeepPause`) | all |

Details:
1. "no home" is in the citizen's needs list; while happiness is under 60 % a line "Held down by: no home, no pots at home
   (heading for 51 %)" comes from `Settlement.ExplainHappiness(c, list)`: the same food / warmth (Warmth / comfort) /
   shelter (homelessShelter) / health (x0.6 when sick) / water / goods / faith inputs, the same avg-and-worst blend and the
   `happiness` effect as `DailyNeeds`, worst first, returning the target. With every need met: "Every need is met:
   happiness is rising (heading for N %)".
2. Top bar needs read "Water", "Pots", "Faith"; tooltips say what they count ("Pots at home: 7 % of people live in a
   household with pots in use ... (the Pottery figure on the left is the pots in store, not yet handed out)"). Home panel
   "pots in use (40 days left)" / "no pots"; citizen "no pots at home"; Happy and People tooltips say "pots at home". The
   sim's unhappy chip still says "pottery" (sim text, left: the report would change).
3. Counts coloured (red 0, amber short, green full); an empty bar draws a red track. Header "Places: 21, filled 11 · short
   of 10 workers" (places = workplaces that want workers now: not paused, flooded or out of season), "Nobody is free to
   haul: goods wait at workplaces" in amber when free <= 0. A short row gets a line under it: "Paused", "Flooded",
   "Nothing to do this season", "The harvest is in"; when nobody is free: "Short: High-priority jobs take everyone first"
   / "Short: taken first by High jobs and builders [and Normal jobs]" (who ranks before the row: High, builders, then
   Normal for Low rows) or "Nobody left to take it: more people, or raise its priority"; a full row whose status is a
   problem: "Its workers need tools", "Waiting for its inputs", "Full: waiting for haulers". No line while someone is free
   (the next shift change fills it). The list scrolls when taller than the screen (it ran off the bottom at 720p).
4. After "Show a good site" / Tab, studs in the accent colour at radius 1.2 and 2.4 round the site and at the campfire's
   warmth radius, until the campfire is placed; the prompt says "The gold ring marks it. Left-click to place the fire.";
   the tip says "The suggested site" within 1.5 cells of it. Counts judged against the suggester's caps (trees 45, rock 8,
   water 12, game 14): at the cap "(plenty)" green, a third or more "(some)" amber, less "(few)" red, 0 "none".
5. Tool: first click on a finished building arms a confirm for 2 s (real time); the tip reads "Click again to remove the
   Lean-to" / "4 live here; half the materials come back." (also "N work here", "N goods move to other stores if there is
   room"); before the click "Remove the Lean-to: click twice" with the same second line. Sites ("Remove the X site" /
   "Delivered materials go back to storage.") and path tiles stay one click. The panel's Demolish tooltip uses the same
   loss text (built only while hovered).
6. Tag for the building under the pointer when no person is: "<b>Toolmaker</b> · Working · 2/2", "Storage Pile · 212 /
   250", "Thatched Hut · 6 / 8 live here", "Site: Lean-to · 40 %", "Campfire · burning", flooded, being rebuilt. Same
   box and code path as the people tag; a faint ring under it in the view (not under the selected building).
7. "Paused (Space)" box under the top bar centre while speed is 0 and no menu, research, end or empty screen is open.
   Home or H: camera to the campfire (distance 34). PageUp / = / numpad + zoom in, PageDown / - / numpad - out, about 8
   wheel notches a second, held. No conflicts (Hud.Keys used none of them; text fields capture the keyboard). F1 lists
   "Wheel, PgUp / PgDn, + / -" and "Home or H"; the middle-drag moved onto the pan row.
8. End screen: Keep playing / Save (opens the save screen) / Main menu (through `MenuLeave`, which asks when play is
   unsaved), and a "Deaths by cause" row with every cause (the empty-settlement screen too). Follow: a toggle ("Following"
   lit); the camera eases onto where the person is drawn until the player pans (keys, edge, middle-drag:
   `OrbitCamera.PlayerPanned`), the panel closes or changes, or any jump (Workplace, Home, chips, events). Difficulty:
   "Starting goods x1, yields x1, firewood use x1. Winters: normal / milder / colder". Load list: the second line shows
   autosave / quicksave, the file name only for a save whose header can't be read. Clothing: "Clothing  N cold days left".
9. Faint 0.55 -> 0.64 and kept for decoration (ticks, "-", key letters, disabled category buttons). Dim instead for: the
   builders' "no sites", the guide's step counter (now 13 px), the Events dates, "(counted from autumn)", locked build
   rows (name and summary; "Needs X" in amber), "Empty" homes and stores, citizen tool "none" (amber) / clothing "none",
   "No water in reach to irrigate", the greyed rebuild button, the research tree's "Needs ..." and "(reach it ...)", the
   help's and game menu's footers, the save-folder line (now 12+ px). Scale: the fit to the screen height is at least 0.85
   (was 0.75), multiplied by the interface size, overall 0.6-3, at most height/800 as before. Small styles get
   round(13 / Scale) clamped to 14-16 (15 at 1280x720 100 %: about 12.75 real px; Label is 16 x 0.85 = 13.6). Help box
   820 wide (key column 290, text 480): nothing clips. Top bar stock names 68 wide (slot 120). Auto-player label above
   the build bar on narrow screens.

## "Smaller items" in the audit
- Unrest chip overstates: already done in 4a (U9 wording). Research ETA: done in 4a. F1 line "Hover any number ...":
  done in 4a; Tab listed already.
- Trend arrow colours: dropped (needs an owner decision, as the audit says).
- "3 queued" in the research box: not done (no room in the 228 px box at the new text size; the tooltip has the count).
- Food tooltip, build-order names: audit says fine/keep. Settings showing stored values in test runs: dropped (testing only).
- U23 leftovers: none (4a fixed the tooltip wording and "a Elders' Fire").
- U10 / U11 (tools rounding, adults on the end screen): already done before this batch.

## Seeing it
`-panelshots -seed 7` windowed 1920x1080, three runs (`runs/pass2/b4b/panelshots1..3`). Fixed from them: the driver (no
homeless person at year 2 in seed 7; now shot on day 3 when everyone sleeps by the fire), the F1 key names still clipping
at 270 (box widened), the Jobs list running off the bottom at 720p (scroll), the reason lines clipping ("...and N"), the
"10/10normal" crowding, "Firewood104" at 720p, the auto-player label over the builders control at 720p, and a too-large
floor at a 70 % interface size (capped at 16). Final run panelshots3 (24 shots, 0 exceptions); 12 copied as `p2-*.png`
(no 4a shot replaced). The end screen shot is the real one (seed 7 reached the Bronze Age in the run).

## Gate (final build `build-b4b`, commit `751225a`)
- Build: success, 0 `error CS` (`runs/build-b4b.log`). (Intermediate builds build-b4b-1 .. -10, one per fix.)
- `tools/gate-autotest.sh b4b`: 15/15 exit 0, errs 0; every seed's `autotest-report.md` and `.json` byte-identical (`cmp`)
  to `runs/pass2/b4a-final/at-<seed>/` (`runs/gate-b4b.out`). Also checked after fix 4 alone (the suggester table):
  seed 2026 identical (`runs/pass2/b4b/check4`).
- Save/load identity, seeds 2026 and 7: IDENTICAL reports, run A PASS, run B PASS (`runs/pass2/b4b/slc-*`).
- Fuzz `tools/fuzztest.sh 15 1 2 3 4 5 6` from the worktree: all PASS, 0 violations (`runs/pass2/b4b/fuzz.out`).
- In-game: `-batchmode -seed 2026 -autoplay -simspeed 4 -quitafter 120`: exit 0, 0 exceptions
  (`runs/pass2/b4b/smoke/player.log`); windowed `-smoketest` then `-smoketest -smokecontinue`: digest MATCH, 0 exceptions
  (`smoke1.log`, `smoke2.log`, in `%TEMP%\Firstlight-scratch`).
- Real saves folder: file list and times identical before and after all runs (`realfolder-before.txt`, `-after.txt`).

## Not done / uncertain
- Research-box queue count (above). Research tree node fonts (11-12 px) left as they are: the nodes are fixed-size cards.
- Follow eases the camera; with the first jump still running it waits for it (1.6 s). Not checked by hand with a mouse.
- The hover tag shows for any building under the pointer, including while the pointer is off-window in test drivers
  (a ring at the screen corner in a few shots); with a real mouse the pointer is always somewhere real.
- Feed chips can sit under a side panel at 720p 160 % (as before this batch).
