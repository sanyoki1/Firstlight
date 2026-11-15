# Pass 2 plan (triage of the four audits)

Sources: `audit-performance.md` (P1-P10), `audit-code.md` (A1-A17), `audit-gameplay.md` (F1-F10),
`audit-ux.md` (U1-U30). Baseline before any change: `runs/pass2/baseline/summary.txt` (15 seeds x 30 years, all PASS,
Agricultural Age in years 3-6, Bronze in years 11-13, no errors).

Scoring: player impact (H/M/L: would a player of the slice notice in the first hours?) against effort (S/M/L) and risk
(to determinism, saves, or the balance the auto-test guards). Order within a batch is the order of work.

## What the audits say, in one paragraph
The Stone Age is the good part: tense, every building matters. The weak spots are (1) the first hour's guidance:
warnings that can't be acted on, a guide step that strands the player without stone, winter help that comes on the
first day of winter, no way to find out why someone died, nothing saying the next age is ready; (2) hitches: every
building or path placed freezes the game 30-127 ms, floods stutter once a second, a 3 ms water mesh rebuild every
frame, a HUD that allocates 31-42 KB a frame; (3) numbers that disagree between panels; (4) saves that would break
with the next build; (5) an Agricultural Age whose added systems (needs, laws, unrest, floods, disease, traders, tin)
make no measurable difference, and a Bronze Age that needs no bronze. (5) is mostly design, so most of it goes to
Nico; the tested data fixes are done now.

## Batches (do now)

| # | Batch | Items | Impact | Effort | Risk | Files (lane) |
|---|---|---|---|---|---|---|
| 1 | Save safety | A2 versioned + id-keyed saves (v1 still loads), A3 unknown ids dropped, A10 safe overwrite, P5 buffered save, A17 double counters, A13 `Lost[]` + conservation invariant in the fuzz test | H (every later build keeps saves; prerequisite for any new saved field) | M | save format: guarded by v1-compat check, save/load identity, fuzz | Save/*, Settlement.Save.cs, counters (main tree) |
| 2 | Hitches | P1 commit + prefetch together, batch path misses, prefetch after load, flood commits throttled; P2 water mesh only when the water stepped + wet-cell mask; P4 autosave off the season-start frame; P6 command line read once; P8 title menu save list cached; P9 Unity splash off; P7 deer threat test by herd | H (placing things and floods stop stuttering; laptops reach 60 fps) | M | routes must not change: auto-test reports byte-identical | GameController*, WorldView, Wildlife, FlowFields, Hud.Menus (lane 2) |
| 3 | Robustness | A1 error guard (pause, stop autosave, message), A9 bad data JSON = message / exit code instead of a hang, A16 Tab key, A4 one production estimate shared by sim and panel, A5 focus reset on rebuild, A12 4x path smoothing cap, A8 warnings | M-H (A16 is in the first minute; A4 is the kind of number Nico checks) | S each | none | GameController, GameData, Hud.cs keys, Hud.Info (lane 2, after 2) |
| 4 | First-hour UX | U1 guide stone trap, U2 no-tools chip, U3 storage chip, U4 "no elders" research box + ETA, U6 ready-to-advance chip/event/button, U7 + F8 winter hint in autumn with numbers and guide step 7 only from autumn, U5 (death hint names the death, deaths by cause in the people tooltip, Events panel), U5b hint auto-close, U9 chips name cause and remedy, U10-U13 numbers that disagree, U14 jobs panel reasons, U15 suggested site marked, U16, U17, U21 two-click demolish, U22 building hover tag, U23-U26, U28, U30 home + zoom keys, U20 contrast | H | S each, M in total | warning text changes the auto-test report text only; guide/hint state is saved (uses batch 1's versioning) | Hud.*, Settlement warnings (lane 2, after 3) |
| 5 | HUD cost | P3 lazy tooltips, styles created once, colour tags precomputed, arrays hoisted, totals once per call (this is QA's F27) | M (invisible here, a hitch source on slower PCs) | M | none (view only); before/after from `-perfprobe` | Hud.*, UiTheme (after 4, same files) |
| 6 | Balance (tested data fixes) | F1 `starveDamage` 0.12 -> 0.06 (famine: people leave before they die); F1 spring harvest-outlook warning; F3 `migrantMinHappiness` 0.5 -> 0.8 (needs gate growth); F6 Harsh winter -0.05 -> -0.02 and start goods 0.7 -> 0.85 if it tests well; F9 fishing 1.9 -> 2.8 and flax 120 -> 200 if they test well; A14 more names | M-H | S | balance: 15-seed gate before/after, years to Bronze and deaths logged | data JSON, Settlement.Agri warning (main tree, after 1) |
| 7 | Problems in the world | U8 badges over buildings with a problem (toggle), clickable chips (U9b), U18 seen hints re-readable in F1, U19 missing one-time hints | M-H | M | none | Hud.*, SettlementView (after 5, if time) |

Gate after every batch: compile check or player build; `tools/gate-autotest.sh` (15 seeds x 30 years, all PASS, no
errors, Bronze reached); `tools/fuzztest.sh`; save/load identity; perf numbers in `perf.md` for 2 and 5; years to
Bronze and deaths before/after for 6. A batch that fails is fixed or reverted.

Lanes: at most two subagents at once. Lane 1 works in the main folder; lane 2 in a git worktree
(`Downloads\ClaudeWorkspace\Firstlight-lane2`, branch `pass2-lane2`) with its own Unity Library and Build, so the two
never build over each other. Lane 2's commits are rebased onto `experiment/quarter-pass2` after review and the gate
is re-run on the combined tree.

## Needs Nico (not done; these change design decisions or add mechanics)
1. **The Bronze Age needs no bronze (F2).** On 15/15 seeds the first bronze is cast after the age-up; traders hand
   over the tin. Options: a "Dig a Tin Mine" milestone (tested: +1 year, needs an auto-player siting fix on seed
   2026), or a new "Cast N bronze" milestone type. Changes the age-up milestones in design.md.
2. **Needs that bite (F3, second half).** Full work speed at happiness 0.85 instead of 0.6, so each unmet need costs
   ~10 % of a household's work. (The migrant gate in batch 6 is the mild version.)
3. **Laws, unrest, floods, disease (F5, A11).** They ask nothing of the player today. Unrest grows with headcount
   alone (standing disputes past ~200 people, thefts past ~400, no counter). Proposals: unrest driven by the player's
   own choices, floods as a gamble instead of a +25 % harvest bonus, outbreaks dangerous to the very young and old.
4. **Research choices (F4).** One fixed order, all 27 topics known by year 12, ~900 knowledge a year wasted after.
   Options: dearer side topics so ~70 % fit before Bronze; a knowledge sink.
5. **After the Bronze Age (F7).** Optional goals per age (design.md already plans them), an end score, slow late
   pressure (woods that stop regrowing, deposits that run out).
6. **Use for surplus food (F1, third option).** Feasts, food as trade goods, or stored grain as a milestone.
7. **Pause while the research screen is open (U29)**, and the colour of falling-stock arrows.
8. **More starting stone (U1 alternative).** Not needed if the guide fix works; listed because it was offered.
9. **Hide Tent timing (F9).** Cheaper Hide Working so the tent comes before the Thatched Hut: changes the research
   tree's costs.

## Deferred (and why)
- A6 data-driven needs / traded goods / piles: tech debt that pays when Bronze content is added; no player effect now.
- A15 splitting the test and clip drivers out of GameController: no player effect; do it when a mode next breaks.
- P10 hourly sim pass and shift start at 1000-2000 people, Burst for the per-citizen step: later ages only
  (0.33-0.52 ms per step at 152 people today).
- Saving on a worker thread, a bucket-queue flow field: buffering (batch 1) and batching (batch 2) come first; revisit
  with their numbers.
- Minimap / overview (U30 M-L part), distinct workplace silhouettes (art; the hover tag covers telling them apart).
- F28-style full Harsh rebalance beyond the two tested numbers: needs a human playing it.
- QA's F30 (small tin deposit), F31 (quickload without asking), F32 (auto-player toast): unchanged, same reasons.
- Sound: still unheard by anyone.
