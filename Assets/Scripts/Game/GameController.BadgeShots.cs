using System.Collections;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -panelshots -badgeshots -seed N -outdir DIR (windowed, like -panelshots, whose scratch folder and no-autosave it shares):
    // screenshots of pass 2 batch 7, the problem badges over buildings and the tinted person rings. The auto-player plays
    // from the suggested campfire site; the run stops in the second summer (seed 7 starves then), the first winters (cold
    // homes when firewood runs short) and a later Agricultural Age town, each at the default zoom and close up, with a
    // badge-free shot (P) for comparison and a hungry, cold or sick person's ring. Each shot logs the badges it shows.
    public sealed partial class GameController
    {
        IEnumerator BadgeShots()
        {
            _shotDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "badgeshots"));
            Directory.CreateDirectory(_shotDir);
            foreach (var f in Directory.GetFiles(_shotDir, "*.png")) File.Delete(f);
            if (!Application.isBatchMode) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureFramerate = 10;
            Sim.SetSpeed(0);
            for (int i = 0; i < 10; i++) yield return null;
            Hud.PointAtSuggestedSite();
            var site = Hud.SuggestedSite ?? new float2(64, 64);
            LookAt(World(site), 40f, 52f, 30f);
            yield return Settle(4);
            Aim(site + new float2(0.01f, 0.01f));
            yield return null; Aim(site + new float2(0.01f, 0.01f));
            Hud.ClickWorld();
            Hud.PointerOverride = new Vector2(-100, -100);
            Auto = new AutoPlayer(Sim);

            // The second summer, midday (seed 7 runs short of food then).
            yield return Advance(() => Sim.Year >= 1 && Sim.Season == 1 && Sim.DayOfSeason >= 3 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.6f, 6000, 2);
            yield return Scene("summer_y2");
            yield return PersonRing("summer_y2_person_ring");

            // The first winter evenings with a cold home (up to the third winter).
            yield return Advance(() => Sim.Season == 3 && Sim.DayFraction > 0.4f && Sim.DayFraction < 0.6f && Sim.Buildings.Any(b => b.Active && b.Def.housing > 0 && b.Residents > 0 && !b.Heated)
                                      || Sim.Year >= 3 && Sim.Season == 3 && Sim.DayOfSeason >= 6, 12000, 2);
            Debug.Log($"[BadgeShots] winter: cold homes {Sim.Buildings.Count(b => b.Active && b.Def.housing > 0 && b.Residents > 0 && !b.Heated)}");
            yield return Scene("winter");
            yield return PersonRing("winter_person_ring");

            // A later town: the Agricultural Age with its needs, in summer.
            yield return Advance(() => Sim.Age.order >= 1 && Sim.Year >= 7 && Sim.Season == 1 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.6f, 20000, 3);
            yield return Scene("late");
            Hud.BadgesOn = false;
            yield return Shot("late_badges_off");
            Hud.BadgesOn = true;

            Debug.Log("[BadgeShots] done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>A person seen in the world (not indoors) selected, hungry or cold if anyone is, else sick, else anyone.</summary>
        IEnumerator PersonRing(string name)
        {
            Citizen who = null; float2 at = default; int rank = 9;
            foreach (var (c, p) in SView.Drawn)
            {
                int r = c.Hungry || c.Cold ? 0 : c.Sick ? 1 : 2;
                if (r < rank) { rank = r; who = c; at = p; }
            }
            if (who == null) yield break;
            Hud.SelectCitizen(who);
            LookAt(World(at), 9f, 45f, 25f);
            yield return Settle(2);
            foreach (var (dc, dp) in SView.Drawn) if (dc == who) { LookAt(World(dp), 9f, 45f, 25f); break; }
            yield return Shot(name);
            Debug.Log($"[BadgeShots] {name}: {who.Name} hungry {who.Hungry} cold {who.Cold} sick {who.Sick}");
            Hud.SelectCitizen(null);
        }

        /// <summary>The town at the default zoom, then close up on the building with a badge nearest the middle of the town.</summary>
        IEnumerator Scene(string name)
        {
            Hud.ClearBanner(); Hud.ShowHint(null);
            var mid = TownMiddle();
            LookAt(World(mid), 34f, 50f, 25f);
            yield return Settle(6);
            yield return Shot(name);
            Debug.Log($"[BadgeShots] {name}: {Hud.BadgeSummary()}");
            var b = Hud.BadgeBuildings().OrderBy(x => math.distance(x.Center, mid)).FirstOrDefault();
            if (b != null)
            {
                LookAt(World(b.Center), 16f, 45f, 25f);
                yield return Settle(4);
                yield return Shot(name + "_close");
                Debug.Log($"[BadgeShots] {name}_close on {b.Def.name}: {Hud.BadgeSummary()}");
            }
        }
    }
}
