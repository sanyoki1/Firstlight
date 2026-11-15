# Batch 5 (the HUD's per-frame cost): before and after

Branch `pass2-lane2`. Before = e07363c (batch 4b plus one probe line: `-perfpanels` also times the Events panel). After =
9864389 (the six batch-5 commits). Same machine, same save (seed 7, day 452, 152 people), same command, two runs each:

`Build/Firstlight.exe -popupwindow -screen-width 1920 -screen-height 1080 -load runs/pass2/perf/save7/seed7-day452.flsave -perfprobe -perfspeeds 1 -perfsecs 40 -perfpanels`

(`runs/pass2/perf/hud.sh <label> <run>`; logs `runs/pass2/perf/hud-{before,after}/H{1,2}/player.log`, git-ignored; table from
`runs/pass2/perf/hudtable.py`.) The windowed player was **not throttled**: 162-165 fps in every phase of all four runs, so
the ms figures are real per-frame costs. OnGUI runs twice a frame (Layout and Repaint) in all of them. Phases: "town 1x" is
the default HUD (the first 40 s after the load, so the 7 feed rows are still showing); then 20 s each with Jobs, Goods,
Goals, Events, the research screen, the campfire selected, a citizen selected, and the lean-to being placed. KB per frame
is the Mono heap growth across the section (block-granular: only the sums over thousands of frames mean anything). GC/min
is gen-0 collections a minute over the whole game, the HUD being the main source in a windowed game.

"run 1 / run 2":

| Scenario | OnGUI ms/frame before | after | OnGUI KB/frame before | after | GC/min before | after |
|---|---|---|---|---|---|---|
| default HUD (town 1x) | 1.672 / 1.699 | **0.823 / 0.687** | 43.3 / 42.8 | **12.3 / 12.4** | 944 / 948 | **186 / 186** |
| Jobs panel | 2.546 / 2.585 | 1.448 / 1.301 | 55.3 / 56.6 | 27.9 / 26.5 | 1768 / 1769 | 408 / 411 |
| Goods panel | 1.899 / 1.949 | 1.045 / 1.026 | 41.1 / 42.3 | 16.5 / 16.0 | 1101 / 1101 | 264 / 258 |
| Goals panel | 1.497 / 1.504 | 0.637 / 0.723 | 28.7 / 28.5 | 8.0 / 7.8 | 819 / 816 | 141 / 141 |
| Events panel | 2.359 / 2.143 | 1.145 / 1.345 | 53.5 / 54.4 | 25.0 / 23.4 | 1654 / 1652 | 399 / 402 |
| research screen | 3.958 / 3.315 | **1.545 / 1.744** | 89.6 / 87.0 | **30.3 / 28.2** | 3526 / 3544 | **462 / 462** |
| campfire selected | 1.812 / 1.572 | 0.699 / 0.723 | 29.5 / 27.1 | 8.5 / 8.5 | 894 / 894 | 180 / 180 |
| citizen selected | 1.679 / 1.677 | 0.753 / 0.757 | 29.3 / 28.7 | 8.9 / 8.0 | 867 / 864 | 198 / 201 |
| placing a lean-to | 1.617 / 1.618 | 0.643 / 0.671 | 27.7 / 25.5 | 7.9 / 7.5 | 855 / 858 | 171 / 168 |

Per HUD part:

| Scenario: part | ms before | after | KB before | after |
|---|---|---|---|---|
| default: top bar | 1.205 / 1.216 | 0.367 / 0.308 | 31.7 / 31.4 | 6.9 / 7.0 |
| default: feed (chips + 7 rows) | 0.306 / 0.316 | 0.201 / 0.163 | 9.3 / 9.0 | 4.2 / 4.3 |
| default: build bar | 0.148 / 0.154 | 0.158 / 0.133 | 3.2 / 3.2 | 1.1 / 1.1 |
| Jobs panel | 0.983 / 0.991 | 0.793 / 0.705 | 23.8 / 25.5 | 18.2 / 17.8 |
| Goods panel | 0.494 / 0.510 | 0.450 / 0.440 | 9.5 / 10.6 | 8.5 / 8.7 |
| Goals panel | 0.027 / 0.029 | 0.036 / 0.042 | 0.4 / 0.5 | 0.5 / 0.4 |
| Events panel | 0.792 / 0.716 | 0.556 / 0.642 | 28.9 / 29.3 | 19.4 / 18.4 |
| research screen | 2.244 / 1.850 | 0.941 / 1.046 | 71.3 / 69.4 | 25.4 / 23.8 |
| campfire panel | 0.148 / 0.115 | 0.105 / 0.113 | 1.9 / 1.7 | 2.3 / 2.2 |
| citizen panel | 0.138 / 0.141 | 0.142 / 0.143 | 1.0 / 0.9 | 2.2 / 1.7 |

## Against the targets
- Default HUD: **0.69-0.82 ms** (target under 0.6) and **12 KB** a frame (target under 2 KB). Not reached.
- Research screen: **1.5-1.7 ms** (target under 1.5) and **28-30 KB** (target under 10 KB). Not reached.
- Gen-0 collections while nothing changes: down 5x (944 -> 186 a minute with the default HUD, 3530 -> 462 with the research
  screen), not to zero.

Why not: what is left is mostly Unity's own IMGUI text drawing, not the HUD's code. A throwaway experiment build (not
committed) drew 2000 labels with one constant string in each OnGUI call and read the Mono heap around them: in the Repaint
call about **190 bytes per rich-text label and 60-100 bytes per plain label**, on screen or off it, with the same string
every time (2000 x `float.TryFormat` and `int.TryFormat` into a buffer: 0 bytes, so TextLine itself makes none). Unity 6
IMGUI lays text out through TextCore on every Repaint, which allocates. The default HUD draws about 50 labels (top bar about
30, chips, 7 feed rows, build bar), Jobs about 125, the research screen about 180: 50 x 130-190 B is 7-9 KB, which matches
what is left. The remaining garbage per panel scales with the labels on screen, not with the strings the HUD builds.
Going further means fewer GUI.Label calls (merging labels into one string per row) or moving the HUD off IMGUI (UI
Toolkit), which is a layout change, not this batch. The remaining time is the same: the top bar's ~30 labels and buttons
cost about 0.3 ms in IMGUI's own drawing.

## What changed (one commit per kind)
1. `06b43f1` Lazy tooltips: `if (Over(r)) Tip(r, ...)` for every built tooltip (top bar figures, speed and panel buttons,
   stock and need figures, research nodes, build rows, chips, feed rows, crop and focus buttons, the demolish button).
2. `aebefda` Styles once: 15 cached variants in `UiTheme.Ensure` (`SmallRight`, `SmallCenter`, `SmallFoot`, `Label14`,
   `LabelCenter`, `FeedRow`, `PlaceTip`, `Title24/28Center/30Center/34`, `Note15Center`, `Button12`, `NodeSub`,
   `NodeUnlocks`; those copied from Small follow its size floor as the per-call copies did); the disabled menu button made
   once; no `new GUIStyle` left in a per-frame path (the remaining ones are `??=` fields or the once-only menu styles).
   Colour tags made once per colour (`UiTheme.Tag`; `Col` is one concat, same text).
3. `81ac89e` + `9864389` Settlement totals in a HUD-side snapshot counted once per OnGUI call (`CountTotals`: population,
   adults, elders, homeless, housing, happiness, food stock and need, "all research known" without LINQ, and how many
   people each need reaches). The same loops and float sums in the same order as Settlement's properties, so the figures
   are identical; nothing in the sim reads them. The second commit fixed the top bar's row two, which measured the needs
   strip once per stock shown (0.66 ms of the 0.94 ms left after the first four commits).
4. `7a70ad4` Cached strings: `UI/TextLine.cs` builds a label in a reused char buffer with `TryFormat` and the same format
   strings, and hands back last frame's string when the text is the same. Used for the top bar, needs strip, build bar,
   Jobs and Goods rows, the hover tag, the research header. Chip and feed-row texts and sizes made once per warning/event
   (and again when the text size changes); feed rows gathered again only when an event comes in or the chips change; chip
   click targets found without delegates (a kind per wording); Jobs rows pooled per building kind and sorted in place
   (same order as the OrderBy/ThenBy it replaces); build list rows once per category and age, cost lines when what is in
   store changes; Events rows' texts made with the rows; the placement tip once a frame; one shared GUIContent for
   measuring.
5. `23c754b` Research screen: node titles, badges, state lines and unlock lists made once per research change (a topic
   learned, the queue or the age changing), node rectangles once per screen size.

Not changed: the building and citizen panels' rows still format their lines on every call (2 KB a frame, the panels change
with every sim step); the guide card's progress line (the guide is a first-hour card); the end and menu screens' texts.

## Screenshots: does the HUD look the same?
`-guideshots`, `-panelshots` and `-uishots` (all `-seed 7`, windowed 1920x1080, fixed game clocks) before (twice: the
noise baseline) and after (`runs/pass2/b57/shots-{before,before2,after}`, compared pixel by pixel with
`runs/pass2/b57/cmpshots.py`, a pixel differing if any channel differs by more than 8):
- before vs before2 (same build): 66 of 69 identical; guide 06 and 10, panel 02, 23 and 24 differ (camera moves, Follow,
  a scrolling list and the menu's backdrop, run to run).
- before vs after: **63 of 69 identical**, including every top bar, Jobs, Goods, Goals, Events, research tree with its
  forced tooltip, build lists, F1, all four layout sizes and the needs labels. The five shots that differ between two
  runs of the same build differ again (looked at guide 06 side by side: the HUD is the same, the camera is elsewhere).
  The sixth, ui 20 (0.03 %, a 64 x 24 corner), is the hover tag of a building under the off-screen test pointer, cut off
  at the window's left edge: "3/3" before, "0/3" after. That tag is rebuilt every 0.25 s of real time (unchanged code,
  `Hud.cs` `_tagAt`), so the moment it shows depends on frame speed; the town in the shot is identical.
- Tooltips still appear: the forced research-tree tooltip (ui 08) and chip tooltip (guide 06) are identical; a real mouse
  hover was not tried (nobody at the PC): the hover test is `Rect.Contains(Event.current.mousePosition)`, the same test
  `Tip` already made.
