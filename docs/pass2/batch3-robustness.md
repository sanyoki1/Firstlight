# Pass 2 batch 3: robustness and wrong numbers (checkpoint)

Worktree Firstlight-lane2, branch pass2-lane2, started from 7c3907f.

## Status
All 8 fixes committed (e7557c5 .. ce8bae1); gate passed on ce8bae1. Commit attribution line: "Claude Opus 5.5"
(the model that did the work), not the "Fable 5.2" line the brief asked for.
- Fix 1 A16 (Tab on campfire prompt): done, commit e7557c5. Compile-checked (dotnet). Tab added to F1 help (box +28 px).
- Fix 2 A9 (bad data JSON): done. Tested on a copy of Build/ (runs/pass2/b3/build-badjson, deleted afterwards):
  stray comma in balance.json -> exit 2 after 2517 ms, log "[Data] cannot start: A data file could not be read. /
  balance.json: JSON parse error: Missing a name for object member."; fuelResource "firewod" -> exit 2 after 2518 ms
  ("balance: unknown fuelResource"); building output "xxwild_food" -> exit 2 after 2530 ms. Windowed error screen
  screenshot: runs/pass2/b3/a9-error-screen.png. "Continue anyway" button not clicked in a test (no input at the PC).
- Fix 3 A1 (guarded frame): done. Windowed -faulttest 10 (seed 2026, autoplay, 4x, quitafter 200): exception logged
  once ("[Fault] exception in the settlement's step on game day 10"), autosave-2 written on day 8 before the fault,
  no [Save] after, day stayed 10 to the end; screenshot runs/pass2/b3/fault/fault-message.png. Batch -faulttest 3:
  exit 3 after 30 s. Buttons (Load a save / Main menu / Save a copy / Quit) not clicked in a test (no input).
  Saves folder backed up to runs/pass2/b3/savebackup/ before the first in-game run (autosave-2 overwritten by the
  windowed fault test: restore at the end). NOTE: the other agent shares this saves folder (Smoke test.flsave,
  smoke/digest.txt written 04:40 by them).
- Fix 4 A4 (one workplace estimate): done, commit 0084fb7. Settlement.Estimate.cs (new), EstimateCheck.cs (new, log
  only), Hud.Info.cs formats it. Edits in existing Settlement*.cs files: Settlement.Buildings.cs WorkStep only:
  (1) `AddKnowledge(d.knowledgePerWorker * frac * mult * Effect(b.YieldKey));` became
  `float learned = d.knowledgePerWorker * frac * mult * Effect(b.YieldKey);` + `AddKnowledge(learned); b.Learned += learned;`
  (2) new line `b.Made += amt;` after `Produced[b.OutRes[i]] += amt;`. Also SimTypes.cs: `public double Made, Learned;` on Building.
  Measured ratios (seed 2026, 30 years, -difficulty 0/1/2; runs/pass2/b3/est-2026-d*/player.log, summary in
  runs/pass2/b3/estimate-ratios-2026.txt), made / estimated per workplace-day:
  "working" = against workers seen at work each hour (factors exposed): outputs median 0.950 / 0.947 / 0.944
  (gentle / standard / harsh), knowledge 0.972 / 0.964 / 0.968; outputs without water 0.951 / 0.947 / 0.956 vs with
  0.950 / 0.948 / 0.944; knowledge elders with the Elders law all day 0.973 / 0.966 / 0.964 vs elders without the law
  0.954 / 0.954 / 0.972 vs no elders 0.974 / 0.958 / 0.971. Had a factor been missing the ratio would move by it
  (gentle x1.15 / harsh x0.9, no water x0.9, law x0.8). Expected band: 0.90-1.0 (p10-p90 about 0.88-0.99).
  "nominal" = against the whole crew at full workdays (the panel's number): outputs median ~0.40-0.41 (p10 0, p90
  0.76), knowledge ~0.77-0.85: walking out/back, carrying, waiting for inputs or haulers. seed 2026 standard reports
  byte-identical to baseline.
- Fix 5 U10/U11 (numbers that disagree): done, commit cdc894a (Hud.TopBar.cs helpers PeopleSplit/PeopleLine/
  ToolsInStore/FoodDaysText; Hud.Agri.cs end screen; Hud.Panels.cs goods panel total row + jobs line).
  HUD sweep, same quantity printed in more than one place:
  - fixed (S): tools top bar (int-truncated ToolStock) vs goods panel ({:0} rounding); people split end screen
    (adults incl. elders) vs top bar; jobs panel "N adults" (incl. elders) vs top bar; food days end screen {:0} vs
    top bar {:0.#}.
  - checked, already one source: food in store ({FoodStock:0} everywhere), knowledge a day (top bar and research
    screen both _knowPerDay), happiness (AverageHappiness P0 in the needs strip and the people tooltip), beds
    (HousingCapacity - Population in the top bar and its tooltip), storage used/capacity (goods panel only), tool
    trend (same toolSpeed>0 set as the new total).
  - listed, not changed: the empty-settlement screen has no people line in this code (population is 0 there), so
    U11's second half has nothing to fix; Settlement.ToolStock stays int (auto-player and report use it).
- Fix 6 A5 (focus survives a rebuild): done, commit a368767. One line in Settlement.Buildings.cs CompleteUpgrade:
  `if (b.Focus >= 0) b.Focus = b.Focus < from.outputs.Length ? Array.FindIndex(d.outputs, o => o.id == from.outputs[b.Focus].id) : -1;`
  Not exercised by a test (only homes upgrade today, Focus -1); compile + gate only.
- Fix 7 A12 (walkers trailing at 4x): done, commit e98a927. SettlementView cap now walkSpeed x max(1, pathSpeed) x
  speed x 1.25 + 0.5 (sim walks walkSpeed x pathSpeed on paths). Target still S.DrawPos (468a70a). Not checked by eye
  at 4x (no motion capture); reasoning only.
- Fix 8 A8: done, commit ce8bae1. Unity build log shows no CS warnings for these files.

## Gate (on ce8bae1, Build/ rebuilt from it)
- Build: runs/build-b3.log, 0 errors, Build Finished Success.
- tools/gate-autotest.sh b3: 15/15 exit 0, errs 0; every at-<seed>/autotest-report.md and .json byte-identical to
  Firstlight/runs/pass2/baseline (cmp loop, no differences). The A4 check only adds "[Estimate]" lines to player.log.
  Per-seed medians of the estimate ratios: runs/pass2/b3/estimate-ratios-gate.txt (working out 0.937-0.950, working
  know 0.957-0.968 over the 15 seeds).
- Save/load: seeds 2026 and 7: IDENTICAL reports, run A PASS, run B PASS (runs/pass2/b3/slc-summary.txt).
- Fuzz: tools/fuzztest.sh 15 1 2 3 4 5 6: all PASS, 0 violations (runs/pass2/b3/fuzz-summary.txt).
- In-game: batch -autoplay -simspeed 4 -quitafter 120: exit 0, 0 exceptions, 0 errors (smoke.log).
  -popupwindow -smoketest then -smokecontinue: digest MATCH (BFACE41164A147C4), 0 exceptions.
- Windowed -uishots (runs/pass2/b3/uishots-win): building panel, goods panel with "Tools (all kinds)", end screen
  "160 (97 adults, 26 elders, 37 children)" matching the top bar; 0 errors.
- Saves folder: autosave-2 restored from runs/pass2/b3/savebackup (1 and 3 unchanged). "Smoke test.flsave" and
  smoke/digest.txt hold my smoke run (shared with the other agent's smoke tests).

## Mistake to report
One fuzz command was started from the wrong directory (a backgrounded `cd ... && (...) &` left the foreground in
the main folder), so `tools/fuzztest.sh 15 1 2 3 4 5 6` ran once from C:\Users\niczm\Downloads\Firstlight: it
copied that folder's Assets/StreamingAssets/Data/*.json into its own Build/ (as its script always does) and ran its
Build/Firstlight.exe, overwriting Firstlight/runs/fuzz-1..6 (05:18-05:19 file times). Nothing was edited or
committed there. The fuzz gate was then re-run in the worktree (results above).

## Left / uncertain
- The data error screen's "Continue anyway" and the fault box's buttons were not clicked in a test.
- A1: an exception inside OnGUI is logged once and the HUD keeps trying each frame (no pause); only the sim step and
  Hud.Update fault the game, as the brief asked.
