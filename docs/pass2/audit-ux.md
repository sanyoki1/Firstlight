# Pass 2: design and UX audit (first-time player view)

Branch `experiment/quarter-pass2`. Find-and-report only: no source or data file was changed.

**Method.** Read the HUD (`Assets/Scripts/Game/UI/*.cs`), camera (`Core/OrbitCamera.cs`), guide and hints
(`Hud.Guide.cs`), every warning and event string in `Game/Sim/Settlement*.cs`, and the names and descriptions in
`buildings.json`, `research.json`, `laws.json`. Looked at every existing still in `docs/art/quarter/`. Took two fresh
windowed runs with the stable copy `runs/pass2/build-ux/Firstlight.exe` at 1920x1080 (both completed first time):
`-uishots -seed 7` (30 shots, campfire to Bronze, `runs/pass2/ux/uishots/`) and `-p4shots -load
runs/pass2/perf/save7/seed7-day452.flsave` (21 shots: menus, guide steps, hints, trader, path drag, flood, 1280x720,
`runs/pass2/ux/p4/`). The 14 shots used as evidence are in `docs/pass2/ux-shots/` (scaled to 1280x720 JPG to keep the
repo small; the full-size PNGs are in `runs/pass2/ux/`).

**Limits.** The screenshot modes drive the game through the auto-player and the HUD's action methods, so every
interaction finding (clicks, hover, keys, drags) is **from code**, not seen. The auto-player runs during both modes
(its toast is in every in-game shot), so the research queue, builder count and build order in the shots are its
choices, not a new player's; where that matters it is said. The feel of camera motion, real mouse
behaviour at panel edges, readability in motion and the sound cannot be judged without a human. Findings already in
`audit-code.md` (A4, A12, A14, A16), `audit-performance.md` and `audit-gameplay.md` are referenced, not repeated.

---

## Summary: the ten things a first-time player will notice most

1. **The Elders' Fire the guide asks for cannot be finished** with the starting stone (10 stone; woodcutter 4 +
   Elders' Fire 8), the guide never mentions a Stone Gatherer, and step 6 ticks off as soon as the *site* is placed, so
   the guide moves on while research sits at "+0/day" (U1).
2. **Warnings in the first minute that the player can't act on or doesn't understand**: "2 workers have no tools"
   from day 4 (no way to make tools for several days), "Storage is nearly full" from day 1 (F29), "Researching Basic
   Tools +0/day" with nothing producing knowledge (U2, U3, U4).
3. **When someone dies, the reason disappears within a minute.** The feed holds 7 lines and fades them after ~56 real
   seconds; there is no event log. The death hint says "The event list says why" but names no one (U5).
4. **Nothing tells the player the next age is ready**: the Advance button lives only inside the Goals panel (U6).
5. **The winter hint comes on the first day of winter**, when nothing can be gathered any more; the guide's winter step
   can tick in spring or summer against small targets and is then gone (U7).
6. **No in-world sign of trouble**: idle, tool-less, input-starved, unstaffed or cold buildings and hungry, cold or
   sick people look exactly like healthy ones; every problem is a text chip with no place to click (U8).
7. **Chips name a symptom, rarely the cause or the remedy** ("Storage is nearly full.", "N people are sick.",
   "N people are unhappy and may leave.", "Waiting for inputs"), and none of them is clickable (U9, failure table).
8. **Numbers that disagree** (the owner reads numbers): top bar "Tools 12" vs goods panel "Stone tools 13"; end screen
   "105 adults" vs top bar "86 adults · 19 elders"; a homeless person reads "Fed, warm and well" at 54 % happiness;
   "Pottery 18" (stock) and "Pottery 100 %" (need) side by side in the same row (U10-U13).
9. **Jobs panel shows 0/8 woodcutters with no reason** and an empty red bar that draws nothing; there is no "what is
   holding the town back" view (U14).
10. **Small, low-contrast text at 1280x720** (layout scaled to 0.75: 13-14 px styles become ~10 px; the Faint grey is
    used for labels the player needs) (U20).

Quick wins (effort S) are marked **[S]** and collected in "Quick wins" below; most of the top ten have an S fix.

---

## 1. First 10 minutes: walkthrough

Times are at 1x: a game day is 40 s, a season 8 days (5.3 min), a year 21 min (`balance.json`).

| # | Step | What the player sees | Problem |
|---|---|---|---|
| 0 | Title -> New game | Title over a turning landscape; New settlement: seed box + Random, Gentle / Standard / Harsh with one line each and "Starting goods x1, yields x1, firewood use x1, winter +0 warmth" (`13-new-game.jpg`) | "winter +0 warmth" / "+0.08" is internal jargon (U27). No hint that Gentle is meant for a first game except its own line ("For learning the game"); fine. |
| 1 | Campfire prompt | Card at top: "Choose a site for the campfire ... open ground with woods, loose rock, water and game within reach", "Show a good site (Tab)", camera keys; guide card 1/9 bottom left; ghost tip "Within reach: trees 51, rock 22, water 18, game 11" (`01-campfire-prompt.jpg`). Time is stopped. | Tab does nothing (A16: `Hud.Keys` returns before the Tab line while `NeedsCampfire`). The button only moves the camera (`PointAtSuggestedSite` = `JumpTo`): the good site is not marked, the ghost stays under the mouse (U15). The four counts have no scale: is "game 11" good? (U15). |
| 2 | Campfire placed | Camera swoops to it (distance 26). Immediately: amber "12 people have no home." chip, "Storage is nearly full." in the feed (170 goods in 180), "Food 6.5 days" amber, and (for a human) "Nothing being researched" amber; with a topic queued, "Researching Basic Tools +0/day" (`14-build-menu.jpg`, auto-player queue). | Three or four amber items in the first second, one of them (storage) not a real problem yet (F29, U3); once queued, research looks started though nothing makes knowledge (U4). |
| 3 | Guide 2: lean-to | "Open Housing (B) and place it near the fire." with a "Place a Lean-to" button (`docs/art/quarter/p4-guide-2-leanto.png`). Ghost tip: "Within the campfire's warmth" in green. | Good. 12 homeless need 3 lean-tos (4 beds each); the step ticks after one. The chip "12 -> 8 people have no home" keeps the pressure visible, fine. |
| 4 | Guide 3-5: gatherers, woodcutter, storage | Build menu rows: name, cost, "3 workers · wild food"; tooltip with yields; ghost tip "Trees in reach: 26 -> yield 100 %". | Good, the yield-in-reach line is the best piece of placement feedback in the game. Cost shown amber when short, but nothing says that placing anyway is allowed and the site will wait (U16). |
| 5 | ~day 4: first tool warning | "2 workers have no tools." amber chip, standing (`02-site-night-no-tools-research0.jpg`). | Not fixable for days (Basic Tools, then a toolmaker, then its tools). The chip reads as an error (U2). |
| 6 | Guide 6: Elders' Fire + queue Basic Tools | "Place a Elders' Fire" button; progress "Elders' Fire not placed · Basic Tools queued" (in the shot the auto-player had queued it; a human must open R) (`03-guide-elders-fire-stone.jpg`, Stone 6). | Grammar ("a Elders'"). Stone: 10 at start, woodcutter took 4, the fire needs 8: the site stalls at 6/8 stone; the guide ticks on placement and moves on (U1). |
| 7 | Guide 7: prepare for winter | "Firewood 14 / 20  Food 64 / 112" (`04-guide-winter-step.jpg`). | Targets are computed from the homes that exist now (20 = the campfire's minimum with no homes). The step can tick in spring/summer and then vanishes; nothing tracks the runway to spring after that (U7). |
| 8 | Site waits at night | Site panel "Waiting for builders" at 19:40 (`02-...jpg`). | Builders are off work (workday ends ~19:10); the panel doesn't say so (U17). |
| 9 | Guide 8: toolmaker | "Waiting for Basic Tools" until it is known. | Fine once U1 is fixed; with U1 the wait is open-ended and unexplained. |
| 10 | Guide 9: goals (G) | Goals panel: key research (Farming, Tribes "not queued" amber) + "Queue the key research"; milestones with counts; Advance button greyed with "Still needed: ..." (`docs/art/quarter/` and `runs/pass2/ux/uishots/09_goals.png`). | Clear. After this the guide is gone: no more structured help, which is fine, but nothing replaces it for the age-up moment (U6). |
| 11 | First autumn | Chips "Not enough firewood for winter (x of about y)" from autumn, "Food stores won't last until summer (x of about y)" from mid-autumn. | These are the right warnings at the right time but say no remedy (U9). |
| 12 | First winter | Hint "Winter has come ... Keep the woodcutters on firewood and the food stores up." on winter day 1 (`docs/art/quarter/p4-...winter_hint`, `04-guide-winter-step.jpg`). | Too late to act on: gathering is x0.12 in winter (U7). |

**Does the guide stall?** Not literally (every step can tick), but step 6 ticks on a site that cannot finish (U1), and
step 7 can tick on a target that only covers the homes built so far (U7). Step 9 needs the player to open a panel,
which is the right kind of step. **Ignoring the guide**: the chips still lead (homeless, food, firewood in autumn),
the build menu is self-explanatory, and the research box says "Nothing being researched" in amber until something is
queued, so a player who ignores the guide is mostly fine except for the Elders' Fire stone trap and the missing
"knowledge comes from an Elders' Fire" link (once a topic is queued the box reads as if research runs: U4).
(Note: the shots were taken with the auto-player on, which queued Basic Tools on day 1; a human starts with an empty
queue, `Settlement.cs:284` is the only place topics are added.)
**Time to first meaningful choice**: immediate (campfire site), good.

**How fast should time run?** Nothing says. 1x is 21 min a year, the Bronze end is ~4 h at 1x (median year 11-12);
the speed buttons have tooltips ("Game speed 2x (key 2)") but no suggestion. Not a problem by itself; see U26 (no
paused indicator).

---

## 2. Failure states: what happens -> what the game shows

"Chip" = standing warning top left (`Settlement.RefreshWarnings`, `Settlement.cs:446-490`); "feed" = the 7-line event
list that fades (`Hud.Panels.cs:181-231`); "panel" = only visible after selecting the building/person. No failure has
an in-world marker except flooding (buildings tinted grey-blue, `SettlementView.cs:221`).

| What happens | What the game shows | Names cause? | Names remedy? | Gap |
|---|---|---|---|---|
| Food running low | Chip "Food will run out in N days." (red at 1-2), top bar food days amber/red | Partly (implicit: not enough food) | No | Which food is falling (wild food fades with cut woods; see gameplay N2) is invisible; suggest the producers |
| No food | Chip "There is no food left." (red); people "hungry" in citizen panel | No | No | No in-world sign; death follows within days |
| Heading into winter short of food | Chip "Food stores won't last until summer (X of about Y)." | Yes | No | Good timing (mid-autumn); add "gatherers make almost nothing in winter: build more food now" |
| Firewood short for winter | Chip "Not enough firewood for winter (X of about Y)." from autumn | Yes | No | Add "set a woodcutter to Firewood" (the remedy exists in its panel) |
| A home without firewood | Home panel "No firewood today: cold inside" (red) | Yes | No | Panel only; no world sign |
| Homeless | Chip "N people have no home."; top bar "N homeless" amber | Yes | Implied | Fine |
| Freezing / cold death | Feed "Ana (34) died of cold." (red, fades in ~56 s) | Partly (cold, not why: no home? no firewood? no clothing?) | No | Lost after a minute; no log (U5) |
| Starvation death | Feed "X (n) died of starvation." | Partly | No | Same |
| Sick (individuals) | Chip "N people are sick." only when >= max(3, pop/6); citizen panel "sick for N more days" | No | No | Add "no healer in reach" / "herbs: 0" where true |
| Outbreak | Feed "Sickness is spreading among the homes around the Thatched Hut: N people fell ill." (clickable) + hint once; risk chip "Sickness could spread: crowded homes, no healer near." | Risk chip yes; event no | Risk chip implied; event no | Good risk chip (F22/F24 fix) |
| Unhappy, leaving | Chip "N people are unhappy and may leave." (only if >= pop/4); feed "X left the settlement, unhappy." | No | No | The person's panel shows what is missing; the chip should name the most common missing need |
| Left in famine | Feed "X left the settlement to find food elsewhere." | Yes | No | Fine |
| Unrest | Chip "Unrest is rising (22%): disputes and theft are likely." | No | No | At 20-25 % neither happens yet (disputes 25 %, theft 30 %, `balance.json`): the wording overstates; name the drivers (the tooltip has them) |
| Dispute / theft | Feed "A dispute broke out around X's household: N people lost two days of work." / "Theft: N wood went missing from the Storage Pile." | No (unrest) | No | Add "(unrest N %)" |
| Building not built: no material | Site panel "No stone in store" (red) | Yes | No | Panel only; chips say nothing; the guide may already have moved on (U1) |
| Building not built: no builders | Site panel "Waiting for builders" / "No builders assigned (bottom bar)" | Partly | Yes for 0 builders | Doesn't say "night" or "N sites ahead of it" (U17) |
| Workplace: no workers | Panel status "No workers" (amber) | No | No | Why: no free adults? higher priorities took them? (U14) |
| Workplace: no tools | Panel status "Needs tools" (fields, mines); chip "N workers have no tools." | Yes | No | Tools in stock but handout lag (gameplay N8) makes it a nag (U2) |
| Workplace: waiting for inputs | Panel status "Waiting for inputs" | No (which input?) | No | Name it: "Waiting for clay (none in store)" (U9) |
| Workplace: output not collected | Panel "Waiting for goods to be carried off" + "Waiting to be carried to storage: N" | Yes | No | When Jobs says "0 free (they haul goods)", nobody is hauling: say so (U14) |
| Workplace: resource exhausted | Panel "No trees left nearby" | Yes | Implied | Fine (no chip, so a dead gatherers' camp can go unnoticed for a year: gameplay N2) |
| Storage full | Chip "Storage is nearly full." | No (which store, which goods) | No | QA top-10 #5, F29 (U3) |
| Field not sown | Field panel "Nothing was sown this year" / "Needs tools" | Yes | No | Panel only |
| Flood | Feed (amber) "The river is rising: it will flood tomorrow. 13 buildings on the low ground by the river will be under water." + hint with "Show me"; then "The river has flooded: ... Work stops there until the water goes down." Buildings tinted; panel "Under flood water" | Yes | Partly | Best-handled failure in the game |
| Nobody researching | Top bar research box "Researching Basic Tools" + "+0/day" (no colour); research screen "from 0 thinkers" | No | No | U4 |
| Research queue empty | Top bar "Nothing being researched" (amber) | Yes | Implied | Fine |
| Need unmet (water/pottery/faith) | Top bar "Water 93 %" etc. coloured; chip for water only ("N people have no clean water nearby.") | Yes | Tooltip | Pottery/faith have no chip; their hints can be held back (U5b) |
| Everyone gone | "The settlement is empty" screen: what happened, Load a save / Main menu / Look around | Yes | Yes | Good |

**Is there a summary of "what is limiting my town right now"?** No. The chips are the closest thing; the Jobs panel
has the raw data (U14).

### Warning and event strings, rated

Standing warnings (chips): `Settlement.cs:451-480`.

| String | Says why | Says what to do | Note |
|---|---|---|---|
| "Food will run out in N days." | ~ | no | |
| "There is no food left." | no | no | |
| "Not enough firewood for winter (X of about Y)." | yes | no | add "Woodcutter: Make Firewood" |
| "N people have no home." | yes | ~ | fine |
| "Storage is nearly full." | no | no | F29; see U3 |
| "N workers have no tools." | ~ | no | U2 |
| "N people are sick." | no | no | |
| "N people are unhappy and may leave." | no | no | name the top missing need |
| "Food stores won't last until summer (X of about Y)." | yes | no | |
| "Unrest is rising/high (P): disputes and theft are likely." | no | no | overstated below 25 % |
| "Sickness could spread: crowded homes, no healer near." | yes | ~ | good |
| "N people have no clean water nearby." | yes | ~ | "build a well" would fit in the chip |

Events (feed): births, migrants, "X finished.", "X rebuilt as a Y.", research finished with unlocks, milestones,
season changes, traders, laws: all clear and neutral. Deaths "X (n) died of cold." name the harm but not its source.
Disputes/theft don't mention unrest. Flood lines are good. Tone everywhere is neutral and plain (no jokes, no cozy
wording); see section 8.

---

## 3. Ranked findings

Ordered by how likely a first-time player hits them in the first hour and how much they hurt. Effort: S (an hour or
two, one function), M (a day), L (more). "From code" = not seen in a screenshot.

### U1. The guide's Elders' Fire step traps the player on stone [S] (high)
- **Evidence**: `balance.json` startResources stone 10; `buildings.json` woodcutter cost 4 stone (guide step 4),
  elders_fire 8 stone (step 6). After step 4 the top bar shows "Stone 6" (`03-guide-elders-fire-stone.jpg`,
  `02-site-...jpg`). Step 6's `Done` uses `Has(h, "elders_fire")` = `CountBuildings(id, includeSites: true)`
  (`Hud.Guide.cs:25,35`), so placing the site ticks it. No step asks for a Stone Gatherer. In the `-uishots` run the
  research bar sat at "+0/day" through day 6.
- **Impact**: the first research (the game's central mechanic) waits on a material the player was never told to make;
  the guide has already moved to "Prepare for winter", the research box still says "Researching Basic Tools".
- **Fix (pick one)**: (a) in the step-6 text add: "It needs 8 stone: a Stone Gatherer by the rocks brings more." and
  give the step a second button "Place a Stone Gatherer" while `S.Stock("stone") < 8` and no stone gatherer exists;
  (b) make step 6 tick only when the Elders' Fire is *finished* (`CountBuildings("elders_fire")` without sites) so the
  card stays and its progress line reads "Elders' Fire: waiting for stone (6 / 8)". Doing both is best. A data-only
  alternative (start with 14 stone) changes balance: needs owner decision.

### U2. "N workers have no tools." from the first days, when nothing can be done about it [S] (high)
- **Evidence**: chip from day 4 of year 1 (`02-site-...jpg`, `05-citizen-homeless-well.jpg`); `Settlement.cs:459-461`
  counts every worker in a `usesTools` job. Tools need Basic Tools, a toolmaker and its first output. Gameplay audit
  N8: it recurs 2-10 times a year on every seed even with 30-80 tools in store (handout lag).
- **Impact**: the player's first amber warning is one they cannot act on, which teaches them to ignore chips.
- **Fix**: in `RefreshWarnings`, while `ToolStock == 0 && !IsResearched("basic_tools")`, say instead (once, as an
  info event, not a chip): "Workers without tools work at 60 % speed. Research Basic Tools, then build a Toolmaker."
  While tools are in stock (`ToolStock > 0`), suppress the chip (the gap is handout lag, not a shortage). Keep the
  chip only for "no tools in store": "N workers have no tools and none are in store: build or staff a Toolmaker."

### U3. "Storage is nearly full." in the first minute and later without saying where [S] (high; F29, QA top-10 #5)
- **Evidence**: every early shot (`14-build-menu.jpg`, 02, 05); `Settlement.cs:458` (any 90 % of total capacity).
- **Fix**: before any Storage Pile exists, word it "The campfire's store is nearly full: build a Storage Pile
  (Storage)." and log it as Info, not Warning. Later: "Storage is nearly full (N of M): goods are waiting at
  workplaces. Build another Storage Pile." When granaries exist and food is the bulk, say "food" vs "goods". Effort S.

### U4. Research looks like it is running when nothing makes knowledge [S] (high)
- **Evidence**: top bar "Researching Basic Tools" with an empty bar and "+0/day" in the same dim grey as a working
  "+9.7/day" (`Hud.TopBar.cs:133-135`; shots 02-09). The research screen says "Knowledge +0 a day from 0 thinkers".
- **Fix**: in `DrawTopBar`, when `_knowPerDay < 0.05` and no Elders' Fire is staffed, draw the second line in Warn:
  "No thinkers: build an Elders' Fire" (or "Elders' Fire unstaffed"). Also show an ETA when running:
  "+9.7/day · ~6 days". Use one word for the workers: "thinkers" (research screen) vs "Elder" (worker title) vs
  "elders" (guide): use "elders" everywhere, since the building's worker title is "Elder".

### U5. Deaths (and every other event) vanish within a minute; there is no event log [M] (high)
- **Evidence**: the feed keeps 7 rows, newest at the bottom, fading by real time: `a = 1.4 - age/40` (`Hud.Panels.cs:
  212-214`), i.e. gone after ~56 s (1.4 game days at 1x, 0.35 at 4x). The "Someone has died" hint says "The event list
  says why" (`Hud.Guide.cs:108`) but names nobody; in the `-uishots` run the death line was already gone when the hint
  showed (`08-death-hint-stuck.jpg`). The people tooltip only has totals ("died 15"); `S.Deaths` (cause counts) is
  shown only on the end screen.
- **Impact**: the question the owner and every player will ask after a winter ("who died, of what?") has no answer
  in the HUD.
- **Fix**: (S part) put the actual line in the hint: replace the first sentence by the most recent death event text,
  e.g. "Mara (31) died of cold. Cold means ...". (M part) an "Events" panel (key E, button beside Jobs): the last 200
  `S.Events`, newest first, day + coloured severity, filter "Problems only", rows clickable when `HasPlace` (same code
  as the feed). Add "Deaths: cold 3, starvation 1, old age 2" to the people tooltip from `S.Deaths`.

### U5b. An undismissed hint holds back every later hint [S] (medium)
- **Evidence**: the `-uishots` run never clicked "Got it": "Someone has died" stood from year 6 spring to year 12 summer
  (shots 10, 12, 17, 18, 21, 23, 26), so the pottery, faith, laws and ore hints never appeared while "Pottery 0 %" and
  "Faith 0 %" stood red (`08-death-hint-stuck.jpg`). F25 only closes hints whose `When` turns false; death, needs, laws
  and ore stay true forever (`Hud.Guide.cs:108-114,144`).
- **Impact**: a player who ignores the card (it never blocks input) loses all later hints. Less likely than the
  screenshot suggests (most players click), but cheap to fix.
- **Fix**: close a hint after a fixed time on screen (e.g. 2 game days) if not dismissed, and keep it re-readable (see
  U18).

### U6. Nothing says "you can advance now" [S] (high)
- **Evidence**: the only Advance control is a button inside Goals (`Hud.Panels.cs:55-57`); the sim logs "Milestone
  reached: X." but nothing when `CanAdvanceAge()` becomes true. The age-up shots were taken after the auto-player
  pressed it.
- **Impact**: a player can sit at "all goals met" for a long time without knowing the age-up is a manual step; the
  Bronze end can be missed the same way.
- **Fix**: when `CanAdvanceAge()` turns true, log an Info event "Everything is ready for the Agricultural Age: open
  Goals (G) to advance." and add a standing green chip "Ready to advance to the Agricultural Age (G)" (chips are
  amber-only today: give `DrawFeed` a Good colour case), and draw the Goals button with `ButtonOn` while ready.

### U7. Winter help arrives too late, and the guide's winter step can tick early and vanish [S/M] (high)
- **Evidence**: hint `When = Season == 3` (first day of winter, `Hud.Guide.cs:107`); gathering is x0.12 in winter
  (`buildings.json`). The guide's step 7 (`WinterReady`, `Hud.Guide.cs:52-58`) targets "firewood until spring for the
  homes lived in *now*" (floored at 20) and "10 days of food"; in `04-guide-winter-step.jpg` (spring day 4, no homes
  yet) it reads "Firewood 14 / 20". Once both are met at any moment the step is done for good.
- **Impact**: QA top-10 #2 (first winter harsh, partly explained). The player is told about winter when it is too late,
  and the only runway tracker leaves the screen early.
- **Fix**: (S) move the hint to autumn day 1 with the numbers: "Winter comes in 8 days. Nothing grows then and little
  is gathered. You need about Y firewood (you have X) and about F food (you have G). Set a woodcutter to Firewood;
  keep gatherers and hunters busy now." Keep a short "Winter has come" line as an event. (S) make step 7 tick only in
  autumn or later (`S.Season >= 2 && WinterReady`), or when a winter is survived. (M) after the guide, show a "to
  spring" runway in the food tooltip and as a third line in winter: "Food to spring: 210 of ~260".

### U8. No in-world indicator for any problem [M] (high)
- **Evidence**: `SettlementView` draws no status marks; only flooded buildings are tinted (`SettlementView.cs:221`).
  A woodcutter with "No workers", a potter "Waiting for inputs", a cold home, a hungry or sick person all look normal.
  Hover tags exist only for people (`Hud.DrawHoverTag`).
- **Fix** (existing tools only): (a) a small IMGUI badge above each building with a problem status, drawn like the
  person name tag (`WorldToScreenPoint` of the building top): a coloured dot plus 1-3 words ("No workers", "No
  tools", "No clay", "Cold", "Full"), only within the camera's near range and capped at ~30; toggle with a key (e.g.
  "Problems: V"). (b) a hover tag for buildings: "<b>Woodcutter</b>  No workers" (same code as people). (c) for
  people, tint the existing hover ring and selection ring by state (red hungry/cold, amber sick). (a) is M; (b) is S.

### U9. Chips and statuses name the symptom, not the cause or the fix; chips are not clickable [S each] (high)
- **Evidence**: section 2 tables. Chips are drawn with `Box` + `Label`, no button (`Hud.Panels.cs:185-193`); events
  with a place are clickable, but nothing shows that except a tooltip.
- **Fix**: (a) Reword (exact strings): "Not enough firewood for winter (X of about Y): set a woodcutter to Firewood.";
  "Food stores won't last until summer (X of about Y): more gatherers, hunters or fishers now."; "N people are sick:
  a herbalist or healer with herbs treats them."; "N people are unhappy and may leave: most lack <need>." (count the
  commonest unmet item); "Unrest is N %: disputes start at 25 %, theft at 30 %. A staffed Tribal Council halves it.";
  status "Waiting for inputs" -> "Waiting for <input> (none in store)" from `d.inputs`. Chips hold ~60 letters
  (F24): use two-line chips (the feed already wraps) or put the remedy in the chip's tooltip. (b) Make chips
  clickable: cycle the camera through the buildings/people concerned (no-tools workers' workplaces, cold homes,
  homeless people, full stores). (c) Give clickable feed lines a small "›" at the right end.

### U10. Tools count differs between the top bar and the goods panel [S] (medium; owner-visible)
- **Evidence**: `07-goods-panel-tools.jpg`: top bar "Tools 12", goods panel "Stone tools 13". `ToolStock` casts the
  sum to `int` (truncates 12.9 -> 12, `Settlement.cs:116`); the panel prints `{amt:0}` (rounds).
- **Fix**: make `ToolStock` a float, or print both with the same rounding (`math.floor` everywhere or `:0`
  everywhere). Also add a "Tools (all kinds)" total row in the goods panel so the two can be compared.

### U11. End screen adults vs top bar adults [S] (medium)
- **Evidence**: `09-bronze-end.jpg`: "148 (105 adults, 43 children)" while the top bar reads "86 adults · 43
  children · 19 elders". End screen uses `S.Adults` (includes elders, `Hud.Agri.cs:256`); the top bar subtracts them
  (`Hud.TopBar.cs:100-102`). The empty-settlement screen has the same line.
- **Fix**: "148  (86 adults, 19 elders, 43 children)" on both screens.

### U12. A homeless person reads "Fed, warm and well" in green [S] (medium)
- **Evidence**: `05-citizen-homeless-well.jpg`: Home "none: sleeps by the fire" (amber), Happiness 54 % (amber),
  then "Fed, warm and well" (green). `Hud.Info.cs:272-281` lists hungry/cold/sick/needs but not "no home"; nothing
  says what holds happiness down.
- **Fix**: add `if (c.Home == null) needs.Add(Col("no home", Warn));` and, when happiness < 0.6, a line "Held down by:
  no home" using the same inputs as `AverageHappiness` (shelter, food, warmth, health, needs).

### U13. Two "Pottery" items in the same row [S] (medium)
- **Evidence**: `09-bronze-end.jpg` top bar: "Pottery 18" (stock, left) and "Pottery 100 %" (need, right); 26/28.
  Need labels use `n.name` (`Hud.Agri.cs:349`); only water is renamed.
- **Fix**: label needs by what they measure: "Water" -> "Water", "Pottery" -> "Pots", "Faith" -> "Faith", or prefix:
  "Homes with pots 100 %". Same idea for the top-bar tooltip ("Pottery: 100 % of people have it" -> "100 % of
  households have pots").

### U14. Jobs panel: unfilled jobs without a reason; empty red bars draw nothing [S] (medium-high)
- **Evidence**: `06-jobs-panel.jpg` (summer, year 6): Woodcutter x4 0/8, Gatherers' Camp x3 0/9, Stone Gatherer x2 0/4,
  Hide Worker 0/2 ... while Field x6 is 18/18 and "0 free (they haul goods)". The bar colour for 0 workers is Bad but a
  zero-width bar shows no colour (`Hud.Panels.cs:90`); "0/8" is in the same grey as "6/6".
- **Impact**: wood is "18 ↓" and the panel doesn't say the woodcutters are empty *because* fields at High priority
  took everyone; "0 free (they haul goods)" doesn't say "nobody is free to carry goods".
- **Fix**: colour the "w/max" text (red for 0, amber below max); add a header line "Places: 87, filled 25 · short of
  62 workers" and, when `free == 0`, "Nobody is free to haul: goods wait at workplaces" in Warn. In each row, when 0
  workers and others have High priority, a faint note "taken by High-priority jobs". This also covers part of QA top-10
  #4 (haul invisible).

### U15. Campfire site choice: "Show a good site" doesn't show it; counts without a scale [S] (medium)
- **Evidence**: `01-campfire-prompt.jpg`; `Hud.PointAtSuggestedSite` only calls `JumpTo` (`Hud.cs:285-291`), so the
  ghost stays under the mouse wherever it is; Tab is dead (A16). Tip "Within reach: trees 51, rock 22, water 18, game
  11" (`Hud.cs:549`) has no good/bad.
- **Fix**: after the jump, draw the existing radius studs (`v.Radii`) at the suggested site in the Accent colour until
  the campfire is placed, and set the tip line "Suggested site" there. Colour each count like the gatherers' yield
  line (green / amber / red against the thresholds the suggester uses), e.g. "trees 51 (plenty), rock 22, water 18,
  game 11 (few)". Fix A16 (move the Tab check above the `NeedsCampfire` return).

### U16. Placing a building you can't afford is allowed, but nothing says what happens [S] (medium)
- **Evidence**: build list shows cost in amber when short (`Hud.Cost`, `Hud.cs:595-605`); the placement tip shows
  "Cost: 12 wood" the same way. From code, placement succeeds and the site waits.
- **Fix**: when any cost item is short, add a tip line in Warn: "Not enough stone in store: builders will wait for
  it."; in the build list row show "short of stone" instead of only colouring.

### U17. Site panel: two progress bars and an unexplained "Waiting for builders" [S] (medium)
- **Evidence**: `02-site-...jpg` at 19:40: "Progress 51 %", "Wood 12/12", "Build work 30 %", "Waiting for builders"
  (`Hud.Info.cs:88-107`). Builders have gone home (workday 0.22-0.80 = 05:17-19:12, `balance.json`).
- **Fix**: outside work hours: "Builders are resting; work starts at 05:15". In work hours with builders busy
  elsewhere: "Waiting for builders: N sites are ahead of it (Build order: First to move it up)". Rename "Progress" to
  "Overall" or drop it (it is a blend of materials and work), keep "Materials" and "Build work".

### U18. Hints can't be re-read; the help screen is keys only [M] (medium)
- **Evidence**: hints are one-time cards (`Hud.Guide.cs:105-117`); `_hintsSeen` only grows. F1 Help lists 21 keys
  (`Hud.Panels.cs:128-166`) and no game concepts. how-to-play.md exists only outside the game.
- **Fix**: add a second column/tab to F1: "Topics", listing every hint already seen (title + text) plus short entries
  with no hint today (see U19), reusing the hint texts. A "Guide" button in F1 that turns the First steps card back on
  for this game (today only Settings can, and only for new games).

### U19. Important mechanics with no hint at all [S each] (medium)
From code, none of these has a hint, guide step or chip that explains it; each line below is a suggested one-time
hint text (neutral, <= 3 sentences), with its trigger:
- **Tools wear out** (first tool reaches 0 life): "Tools wear out with use. A toolmaker keeps them coming; workers pick
  up the best tools in store."
- **Builders count** (2+ sites waiting > 2 days with builders all busy): "Only N people build. Raise Builders (bottom
  bar) to finish sites faster; they come back to other work when nothing is left to build."
- **Priorities** (a workplace at 0 workers for a day while others are full): "Jobs fill by priority: High first, then
  builders, then Normal, then Low. Set it in the building's panel; Jobs (J) shows them all."
- **Make/focus** (first winter warning for firewood, if a woodcutter exists): "A woodcutter can make only firewood:
  open it and choose Make: Firewood."
- **Rebuild in place** (Farming known and a lean-to exists): "Lean-tos and hide tents can be rebuilt as thatched huts
  where they stand: select one and choose Rebuild."
- **Storage full** (U3 covers it).
- **Deposits / clay** (Pottery known): "Clay is dug at a Clay Pit on the river's clay beds; the placement shows them."
- **Tin far away** (Bronze Working known): "There is one small tin deposit, usually far out in the hills. A Tin Mine
  must stand on it; traders sometimes bring tin."
- **Age-up is manual** (U6).
- **What the Bronze end needs** (Agricultural Age reached): "The Bronze Age needs Writing and Bronze Working, 80
  people, a Mining Pit, a Smelter, a Granary and a Bronze Foundry. Goals (G) tracks them."
Effort per hint is S (one `HintDef` line each); check the queue issue (U5b) first.

### U20. Text size and contrast at 1280x720 [S] (medium)
- **Evidence**: `10-topbar-1280.jpg`: layout at scale 0.75 (`UiTheme.Scale` floor), so the 13-14 px Small style is
  ~10 px; "Builders / no sites", "FIRST STEPS 6 / 9" (12 px Faint), key hints on buttons and the locked rows of the
  build list are Faint grey (0.55) on 0.11 dark: hard to read even at 1080p for the 12 px ones.
- **Fix**: raise `Faint` to ~0.64 and never use it for text the player needs (builders' "no sites", the guide's step
  counter, "not queued"); set a 13 px minimum after scaling (e.g. `fontSize = max(fontSize, 13 / Scale)` in the
  styles), or let the floor of `Scale` be 0.8 and drop low-priority top-bar goods instead.

### U21. Demolish tool removes finished buildings with one click; panel asks twice [S] (medium)
- **Evidence**: `Hud.ClickWorld` demolish branch calls `S.Demolish` at once (`Hud.cs:125-129`); the panel's Demolish
  needs a second click (`Hud.cs:137-143`). Refund is 50 % (`demolishRefund`); occupied homes and staffed workplaces go
  too.
- **Fix**: in the tool, a finished building needs a second click on the same building within 2 s (reuse
  `_confirmDemolish`), with the tip "Click again to remove the Thatched Hut (8 live here; half the materials back)".
  Sites and paths stay one-click (cancelling a site is free: "Delivered materials go back to storage").

### U22. Workplace buildings are hard to tell apart; no building hover tag [S] (medium)
- **Evidence**: `12-town-wide.jpg`, `06-jobs-panel.jpg`: woodcutter, stone gatherer, toolmaker, hide worker, potter and
  storage piles are all brown platforms with small piles at the default zoom; huts, fields, wells, fires and the
  granary are distinct. Hover shows a tag only for people.
- **Fix**: hover tag for buildings: "<b>Woodcutter</b>  Working · 2/2" (same code path as `DrawHoverTag`, using
  `HoverBuilding`). No art change needed. Distinct silhouettes would be an art task (not proposed: art style is
  decided).

### U23. Guide "hide" turns the guide off for all future games; tooltip says "this game" [S] (low-medium)
- **Evidence**: `Hud.Guide.cs:86-87`: the button sets `GameSettings.Current.guideDone = true` and saves; tip "Hide the
  guide for this game (Settings can turn it back on)".
- **Fix**: either change the tip to "Hide the guide (Settings > First-game guide turns it back on)" or only set
  `GuideHidden` for this game. Also fix "Place a Elders' Fire": `$"Place {Article(build.name)} {build.name}"` or simply
  "Place: Elders' Fire".

### U24. End screen offers only "Keep playing" [S] (low-medium)
- **Evidence**: `09-bronze-end.jpg`, `Hud.Agri.cs:274`. The empty-settlement screen has Load / Main menu / Look around.
- **Fix**: add "Save" (opens the save screen) and "Main menu" (with the unsaved-play confirm already in `MenuLeave`)
  beside "Keep playing". Also add the cause-of-death breakdown on its own line (it is truncated into "Left / died").

### U25. "Follow" doesn't follow [S] (low)
- **Evidence**: `Hud.Info.cs:290`: `JumpTo(c.Pos)` once. The person walks off screen immediately at 2x-4x.
- **Fix**: rename to "Go to", or make it a toggle that keeps `G.Cam.Focus` on the person until the panel closes or the
  camera is moved by the player.

### U26. Pause has no on-screen indicator besides the "II" button [S] (low-medium)
- **Evidence**: grep: the only "Paused" label is in the game menu (`Hud.Menus.cs:414`). Speed is shown by which of
  four small buttons is highlighted.
- **Fix**: while `S.Speed == 0` and no menu is open, a small "Paused (Space)" tag under the top bar centre in Accent.
  Put the keys on the speed buttons' tooltips already there; fine.

### U27. Difficulty and settings wording [S] (low)
- **Evidence**: `13-new-game.jpg`: "Starting goods x1, yields x1, firewood use x1, winter +0 warmth"
  (`Hud.Menus.cs:229`). Load screen shows sanitized file names with stray underscores, "Agricultural Age_ year 7
  Spring.flsave", under every save (`11-load-screen.jpg`).
- **Fix**: "Winters: normal / milder / colder" instead of "+0.08 warmth"; keep the multipliers. Hide the file name
  line, or show it only when it differs from the display name, without the extension.

### U28. Clothing line always says "hide clothing" [S] (low)
- **Evidence**: `Hud.Info.cs:284` hard-codes "hide clothing"; weavers make cloth clothing too (`Weaving`).
- **Fix**: "Clothing  N cold days left" (the kind isn't tracked per person; don't name it).

### U29. Research screen keeps time running behind a full-screen panel [S] (low; needs owner decision)
- **Evidence**: `ResearchOpen` doesn't pause (`GameController.Flow.cs:42`: only the game menu pauses). At 4x a season
  passes in 80 s while the player reads the tree.
- **Fix (needs owner decision)**: auto-pause while the research screen is open and restore the speed on close; or
  show the date and speed in the research screen header.

### U30. No camera "home" key and no overview [S/M] (low-medium, from code)
- **Evidence**: `OrbitCamera.cs`: WASD/arrows/edge pan, Q/E turn, right-drag turn/tilt, wheel zoom, middle-drag pan;
  zoom 6-320 on a 128-cell map; no key to return to the campfire, no zoom keys (keyboard-only players can't zoom), no
  minimap. The camera does jump home on age-up.
- **Fix**: Home key (or H) = `JumpTo(S.TownCenter.Center, 34)`; `+`/`-` or PageUp/PageDown for zoom; add both to F1.
  A minimap is M-L (not proposed now). MaxDistance 320 is probably far more than useful on a 128 map; judge by hand.

### Smaller items (all S)
- **Unrest chip overstates** at 20-25 %: "disputes and theft are likely" (`Settlement.cs:474`) -> "Unrest is rising
  (22 %): disputes start at 25 %." (in U9).
- **Research ETA**: show "~N days" next to "+9.7/day" (in U4).
- **F1 help** lists no Tab (dead anyway, A16), doesn't say events are clickable or that every top-bar number has a
  tooltip; add one line: "Hover any number for what moves it; click an event to go there."
- **Trend arrows** are red for any fall (wood falls when building, which is fine): consider amber for falls and red
  only when the stock will run out within N days. Needs owner decision (colour language), low priority.
- **"Researching Writing" box** has no queue count; tooltip has it. Fine, but "3 queued" on the second line would help.
- **Food tooltip** says "Click for every good (T)" while the second line shows only food; fine.
- **Site build order** buttons read "Last / Normal / First" while the same underlying priority reads "Low / Normal /
  High" on workplaces and in Jobs. Acceptable (different meaning to the player); keep.
- **Settings in command-line runs** show the stored values (Fullscreen, 2560x1440) rather than the running window
  (popup 1920x1080) (`runs/pass2/ux/p4/03_p4_menu_settings.png`); only matters for testing.

---

## 4. Hints and guide: timing and coverage

| Hint | Trigger | Timing judgement |
|---|---|---|
| Winter has come | winter day 1 | Too late (U7): move to autumn day 1 with the numbers |
| Someone has died | first death | Right time, but names nobody, and the event it points to fades (U5); never auto-closes (U5b) |
| Clean water | water need active (Agricultural Age) | Fine |
| Pottery | Pottery known | Fine (held back by U5b in the run) |
| Faith | Shamanism known | Fine |
| Sickness is spreading | first outbreak | Says "Outbreaks start in crowded homes without clean water" (the old F22 wording; the chip was fixed, the hint wasn't). Use the same cause list as `OutbreakWarning()` |
| Laws | Tribal Law known | Fine |
| Ore deposits | Prospecting known | Fine; add "tin is far" (U19) |
| River rising | flood warned (a day ahead) | Good, has "Show me" |
| Traders | first offer | Good |

Re-reading: impossible (U18). Missing hints: U19. The guide: steps 1-5 good; step 6 trap (U1); step 7 early tick (U7);
step 8 fine; step 9 fine. "hide" wording (U23).

---

## 5. Camera and settlement readability (from the screenshots)

- **Start**: the campfire prompt opens at distance 45, pitch 50 on a "viewpoint" with woods, rock and river in frame
  (`01-campfire-prompt.jpg`): sensible. After placing, distance 26 (`OnCampfirePlaced`): close and readable.
- **Buildings**: huts (lean-to, hide tent, thatched, stone-footed), fields (sown/green/ripe/bare), wells, fires, the
  granary and the council read well at default and wide zoom (`12-town-wide.jpg`, `uishots/29-30`). Small workplaces
  blur together (U22). Sites read clearly (stakes, scaffold, materials) (`02-...jpg`).
- **Status in the world**: none except floods (U8). Upgrades in place: the rebuild shows scaffolding; fine.
- **People**: visible at default zoom, carry poses visible close up; at wide zoom they are specks (expected). Hover
  ring + name tag help.
- **Trees**: the canopy cut-away works near the focus (`p4/09` vs `10`); far trees still hide gatherers and
  woodcutters, which is acceptable (A12/QA note).
- **Night** (`uishots/17_night.png`, `p4/08`): readable; firelight pools mark the centre; buildings away from fires are
  dark but legible. **Winter** (`uishots/18_winter.png`): snow whitens paths and fields; huts and people stand out
  well; work buildings lose their outlines. Acceptable.
- **Selection ring and radius studs**: clearly visible in daylight and on snow (`06`, `25`, `26`).
- **Placement ghost**: green/red with a reason in red ("Can't build in water", `uishots/04_placement_invalid.png`);
  the yield-in-reach line and floodplain/warmth/irrigation lines are excellent. Rotation keys are named in the tip.
- **Map information while choosing a site**: trees and rock are visible; water obvious; floodplain only shows while
  placing (tip line), fertile/irrigable land only as a tip line, herds only as moving deer, deposits after Prospecting
  as stained ground (good). No overlay mode. Acceptable for this slice; an overlay key (floodplain / water reach) would
  be M.

## 6. Controls (from code)

- Keys are consistent and listed in F1; panel buttons show their key (R, G, J, F1, Demolish X); build categories and
  speed buttons don't (B opens the first category; 1/2/3 are in tooltips only). No rebinding.
- Esc order is sensible (menu screen > end/empty > research > help > tool > build list > side panels > selection >
  game menu, `Hud.Back`).
- Right-click: cancels/clears only if the pointer moved < 6 px (`Hud.cs:270`), else it is a camera turn: good.
- Shift-click keeps placing; `,`/`.` rotate (named in the placement tip, F1). Discoverable enough.
- Demolish: tool one-click (U21), panel two-click. Quickload F8 without confirm (F31, known).
- Clicking feed events with a place jumps there (tooltip only says so); chips aren't clickable (U9).
- Mouse alone: everything except Tab (dead) and hide-HUD; keyboard alone: no zoom (U30), no selection.
- Tooltips appear immediately on hover (no delay) and follow the mouse: fine for a numbers-heavy HUD.

## 7. Menus and end states

- Title, New game, Load, Save, Settings, game menu: clear, with confirms for overwrite, delete, load over unsaved play
  and leaving unsaved (`Hud.Menus.cs:75,255,300,306`). Good.
- Settings apply at once ("Changes are kept at once"); a bad resolution has no revert timer (known gap in the build
  log). Low risk on a PC with a desktop option.
- New game: seed explained; difficulty wording (U27).
- Bronze end screen: one button (U24), adults count (U11). Empty settlement screen: good.

## 8. Text and tone

- Tone is neutral throughout: descriptions are plain and grounded ("A daubed bin raised on posts, out of reach of damp
  and mice."), no jokes, no cozy diminutives. Nothing to change for tone.
- Typos/grammar: "Place a Elders' Fire" (U23). Sanitized save names with underscores (U27). Otherwise clean (the QA
  pass fixed the plurals).
- Inconsistent names: "thinkers" / "elders" / "Elder" (U4); "Pottery" stock vs need (U13); "hide clothing" (U28);
  "Happy" (top bar) vs "Happiness" (panels) - fine; "wild food" (good name) vs "roots, nuts and berries" (guide) - fine;
  outbreak hint vs chip causes (section 4).
- Long texts: the Laws panel paragraphs and the research tooltips are long but are reference text; the hint cards are
  2-3 sentences; fine.

---

## Quick wins (effort S), in suggested order

| # | Fix | Where |
|---|---|---|
| U1 | Elders' Fire step: mention stone, add "Place a Stone Gatherer", tick on finish | `Hud.Guide.cs` Steps[5] |
| U2 | No-tools chip: info line before tools exist, suppress while tools are in store | `Settlement.RefreshWarnings` |
| U3 | Storage chip: campfire wording before a pile exists; say goods wait at workplaces | `Settlement.RefreshWarnings` |
| U4 | Research box: "No thinkers: build an Elders' Fire" in amber; ETA; one word for elders | `Hud.TopBar.cs:129-137`, `Hud.Research.cs:102` |
| U5 (S part) | Death hint quotes the last death line; deaths by cause in the people tooltip | `Hud.Guide.cs:108`, `Hud.TopBar.cs:107` |
| U5b | Auto-close a hint after ~2 game days | `Hud.UpdateHints` |
| U6 | "Ready to advance" event + green chip + lit Goals button | `Hud.Panels.cs`, `Hud.TopBar.cs` |
| U7 | Winter hint at autumn day 1 with numbers; guide step 7 ticks only from autumn | `Hud.Guide.cs:38,107` |
| U9 | Reworded chips/statuses (exact strings above); clickable chips | `Settlement.cs:451-480`, `Settlement.Buildings.cs:589`, `Hud.DrawFeed` |
| U10 | Tools rounding | `Settlement.ToolStock` |
| U11 | Adults/elders on end and empty screens | `Hud.Agri.cs:256,313` |
| U12 | "no home" in the citizen's needs; "held down by" line | `Hud.Info.cs:272-281` |
| U13 | Rename need labels (Pots / Water / Faith) | `Hud.DrawNeedsStrip` |
| U14 | Jobs: coloured counts, "short of N workers", "nobody free to haul" | `Hud.DrawJobs` |
| U15 | Mark the suggested campfire site; judge the counts; fix Tab (A16) | `Hud.PointAtSuggestedSite`, `Hud.Keys` |
| U16 | "Builders will wait for it" when short | `Hud.DrawPlacementTip` |
| U17 | Site panel: night / sites ahead wording | `Hud.DrawSite` |
| U20 | Faint contrast, 13 px floor | `UiTheme` |
| U21 | Two-click demolish for finished buildings in the tool | `Hud.ClickWorld` |
| U22 | Building hover tag | `Hud.DrawHoverTag` |
| U23 | Guide "hide" wording; "a Elders'" | `Hud.Guide.cs:86-97` |
| U24 | End screen Save / Main menu | `Hud.DrawEndScreen` |
| U25 | "Follow" -> "Go to" (or real follow) | `Hud.Info.cs:290` |
| U26 | Paused tag | `Hud.OnGUI` |
| U27, U28 | Difficulty wording, save file line, clothing line | `Hud.Menus.cs`, `Hud.Info.cs:284` |
| U19 | New one-time hints (one line each) | `Hud.Guide.cs` Hints |
| U30 | Home key, zoom keys | `Hud.Keys`, `OrbitCamera` |

Medium (M): U5 event log panel, U8 problem badges over buildings, U18 Topics page in F1, U7 runway line in winter.
Needs owner decision: U29 (pause on research screen), arrow colour language, starting stone (alternative fix for U1).

---

## Looked at and fine

- Placement tip: yield-in-reach with a coloured %, warmth, floodplain, irrigation, deposit cells, water reach, cost:
  the best feedback in the game (`14-build-menu.jpg`).
- Invalid placement: red ghost and a clear reason ("Can't build in water", "Rock in the way", "Must stand more on the
  tin deposit (n of m cells)").
- Research tree: columns by age, age-locked column clearly labelled "(reach it to research these)", queue numbers on
  nodes, key-research marker, full tooltip with cost/needs/unlocks, footer with the mouse controls.
- Goals panel: key research with queue state and a "Queue the key research" button, milestones with counts, the
  Advance button with "Still needed: ..." text.
- Top bar tooltips: every figure has one saying what it is and what moves it.
- Flood flow: warning a day ahead with the number of buildings, hint with "Show me", tinted buildings, panel line.
- Trader offer card: what, for what, in-store amounts, how long it stands, Always accept.
- Laws panel: each law's benefit and cost in one paragraph, the cooldown, unrest shown.
- Field panel: stage, sown/grown/harvested bars, expected harvest, irrigation note, crop choice locked until next spring.
- Site panel materials per item and "No X in store" in red.
- Empty-settlement screen (F1 fix), save/load confirms, autosave rotation.
- Click-through protection and Esc order (read in code).
- Event feed collapsing repeats ("x3") and not repeating standing chips.
- Night, winter, selection ring, radius studs: legible in stills.
- Tone: neutral throughout.
- 1280x720 layout: nothing overlaps or is cut off (`10-topbar-1280.jpg`), only small (U20).

## Not judged (needs a human)

Camera feel (pan/turn speeds, edge-scroll sensitivity, zoom steps), drag thresholds with a real mouse, tooltips at panel
edges, whether players notice the guide card at the bottom left, readability in motion (hundreds of moving people),
sound, and how long a real first winter takes to understand.

---

## Notes log (raw, appended as found)

- Fresh `-uishots -seed 7` windowed run OK (30 shots, `runs/pass2/ux/uishots/`); `-p4shots -load save7` OK (21 shots, `runs/pass2/ux/p4/`).
- Top bar "Researching Basic Tools  +0/day" from day 1 to day 6+ with no Elders' Fire (02-09 shots): reads as progress.
- "2 workers have no tools." amber chip from day 4 of year 1 (05-09) although tools cannot exist until Basic Tools + a toolmaker: an unfixable warning in the first minutes. Warning code `Settlement.cs:459-461` counts any `usesTools` job.
- Guide/stone: start 10 stone; woodcutter 4 + Elders' Fire 8 = 12; guide never asks for a Stone Gatherer -> the Elders' Fire site waits for stone (step 6 ticks on placement, so the guide moves on while research is stuck at +0/day).
- "Someone has died" hint stays up from spring y6 to summer y12 in the run (10, 12, 18, 26), never names who/why, and the feed no longer shows the death line; it blocks every later hint (pottery 0 % red with no pottery hint, 12/18).
- Event feed: 7 rows, fades after ~56 s real time (`Hud.Panels.cs:213`), no history; the death hint says "The event list says why".
- No "ready to advance" signal: age-up is a manual button in Goals (`Hud.Panels.cs:57`); nothing else tells the player when all goals are met.
- Tools: top bar `ToolStock` truncates to int, goods panel rounds: "Tools 12" vs "Stone tools 13" (13/14 shots).
- Citizen panel: homeless person shows "Fed, warm and well" in green with happiness 54 % and no reason (07).
- Site panel: "Progress 51 %" and "Build work 30 %" two bars; "Waiting for builders" at 19:40 (night) without saying builders are asleep (05).
- "Follow" button jumps once (`Hud.Info.cs:290`), does not follow.
- Demolish tool (X, click) removes a finished building at once (no confirm), the panel button asks twice (`Hud.cs:125-128` vs `137-143`).
- "Hide" on the guide sets `guideDone` for all future games; tooltip says "for this game" (`Hud.Guide.cs:86-87`).
- Clothing line always says "hide clothing" though weavers make cloth (`Hud.Info.cs:284`).
- End screen: "105 adults, 43 children" while the top bar shows "86 adults · 43 children · 19 elders" (28).
