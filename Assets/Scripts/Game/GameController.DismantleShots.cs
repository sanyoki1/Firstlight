using System.Collections;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -load SAVE -panelshots -dismantleshots [-outdir DIR]: stills of dismantling with the HUD. A lived-in home is
    // selected (the Dismantle button), marked (the panel and the building's marked look), half taken apart, and down
    // (the goods on its plot, selected); then a store that holds goods, marked. Batch mode or windowed; writes nothing
    // to the real save folder and quits.
    public sealed partial class GameController
    {
        IEnumerator DismantleShots()
        {
            _shotDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "dismantleshots"));
            Directory.CreateDirectory(_shotDir);
            foreach (var f in Directory.GetFiles(_shotDir, "*.png")) File.Delete(f);
            if (!Application.isBatchMode) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureFramerate = 10;
            Sim.SetSpeed(0);
            for (int i = 0; i < 10; i++) yield return null;
            Hud.ClearBanner(); Hud.ShowHint(null);
            if (Sim.BuildersWanted < 4) Sim.SetBuilders(4);
            var tc = Sim.TownCenter.Center;
            yield return Advance(() => Sim.DayFraction > 0.3f && Sim.DayFraction < 0.4f, 4000, 0.25f);

            var home = Sim.Buildings.Where(b => b.Active && b.Def.housing > 0 && b.Residents > 0 && b.Def.buildWork > 0).OrderBy(b => math.distance(b.Center, tc)).FirstOrDefault();
            if (home != null)
            {
                LookAt(World(home.Center), 15f, 46f, 25f);
                Hud.Select(home);
                yield return Settle(4);
                yield return Shot("dismantle_1_before");
                Sim.Dismantle(home);
                yield return Settle(4);
                yield return Shot("dismantle_2_marked");
                yield return Advance(() => home.Work < 0.55f || !home.Active, 4000, 0.1f);
                yield return Settle(3);
                yield return Shot("dismantle_3_half");
                yield return Advance(() => !home.Active, 4000, 0.1f);
                Debug.Log($"[DismantleShots] {home.Def.name}: state {home.State}, pile {home.OutTotal:0.#}");
                if (home.IsPile)
                {
                    Hud.Select(home);
                    yield return Settle(2);
                    yield return Shot("dismantle_4_pile");
                }
            }
            var store = Sim.Buildings.Where(b => b.Active && b != Sim.TownCenter && b.Store != null && b.Stored > 5).OrderByDescending(b => b.Stored).FirstOrDefault();
            if (store != null)
            {
                LookAt(World(store.Center), 15f, 46f, 25f);
                Sim.Dismantle(store);
                Hud.Select(store);
                yield return Settle(6);
                yield return Shot("dismantle_5_store");
            }
            // The tool's tip over a marked building.
            var marked = Sim.Buildings.FirstOrDefault(b => b.Dismantling);
            if (marked != null)
            {
                Hud.Select(null);
                Hud.StartDemolish();
                Aim(marked.Center); yield return null; Aim(marked.Center);
                yield return Shot("dismantle_6_tool");
                Hud.CancelTool();
            }
            Time.captureFramerate = 0;
            Application.Quit();
        }
    }
}
