using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Problem badges (UX audit U8a): a small tag over each building that has a problem the player can fix, drawn where the
    // building stands on the screen (like the hover tag), only while the camera is near enough to read them, at most
    // MaxBadges (nearest the middle of the screen first), never over a panel; clicking one selects the building. P or the
    // build bar's "Problems" button turns them off and on.
    //
    // What counts as a problem (short on purpose: a badge that is always there teaches the player to ignore badges):
    //   site:      no builders at all ("No builders"); a material it still needs with none in store ("No stone")
    //   workplace: flooded; paused; and, in a season it has work: its workers have no tools ("No tools") / an input with
    //              none in store ("No clay") / its output waiting for haulers ("Full") / nothing left in reach ("No trees
    //              left"), each once it has lasted two game hours (a shift change or a hauler on the way clears most of them
    //              by itself); no workers for a whole game day, unless its priority is Low or the season yields under half
    //              its usual (gatherers, hunters and fishers in winter).
    //   home:      flooded; lived in and unheated on a day that needed firewood ("Cold"); once the need applies, no clean
    //              water / no shaman in reach ("No water", "No faith"): both are about where the home stands, so the badge
    //              says where to build a well or a shaman's hut. Pots are not badged: they come from the potters, not from
    //              a place, and most homes lack them at once (the top bar's Pots figure says it).
    //   store:     97 % full or more ("Full"); the campfire also "No firewood" when its fire is out.
    // Not problems: fields and gatherers with nothing to do in the season, workers resting at night, a home that needs no
    // fire in summer, an input on its way from storage, an empty home.
    public sealed partial class Hud
    {
        public bool BadgesOn = true;
        public void ToggleBadges() => BadgesOn = !BadgesOn;

        const int MaxBadges = 30;
        /// <summary>Badges show for buildings up to this far from the camera (at the default zoom, about the middle of the
        /// screen and its near half; zoomed out to the whole valley they would be unreadable clutter).</summary>
        const float BadgeRange = 62f;

        sealed class BadgeSlot { public Building B; public string Text; public Color Col; public Rect R; public float Score; }
        readonly List<BadgeSlot> _badgePool = new();
        readonly List<BadgeSlot> _badges = new();
        readonly List<Rect> _badgeRects = new(), _prevPanels = new();
        readonly Dictionary<int, (string label, float since)> _problemSince = new();
        readonly Dictionary<string, string> _noLabel = new();
        readonly Dictionary<string, float> _badgeW = new();
        int _badgeFont = -1;
        static readonly System.Comparison<BadgeSlot> ByScore = (a, b) => a.Score.CompareTo(b.Score);

        /// <summary>"No clay" for a resource (made once per resource).</summary>
        string NoLabel(string resId)
        {
            if (_noLabel.TryGetValue(resId, out var s)) return s;
            s = "No " + ResName(resId).ToLower();
            _noLabel[resId] = s;
            return s;
        }

        /// <summary>The building's problem as a badge (null when it has none). Slow: whether it must last two game hours first.</summary>
        string Problem(Building b, out Color col, out bool slow) => Problem(b, out col, out slow, out _);

        string Problem(Building b, out Color col, out bool slow, out bool slowDay)
        {
            col = Warn; slow = false; slowDay = false;
            var d = b.Def;
            if (d.path || !b.Alive) return null;
            if (b.Dismantling) return "Dismantling";
            if (b.IsSite)
            {
                if (S.BuildersWanted == 0) return "No builders";
                for (int i = 0; i < d.cost.Length; i++)
                {
                    var c = d.cost[i];
                    if (b.Delivered[i] + b.InTransitOf(i) < c.amount - 0.01f && S.Stock(c.id) < 0.5f) { col = Bad; return NoLabel(c.id); }
                }
                return null;
            }
            if (!b.Active) return null;
            if (b.Flooded) { col = Bad; return "Flooded"; }
            if (d.warmthRadius > 0 && !b.Fueled) { col = Bad; return "No firewood"; }
            if (d.IsWorkplace)
            {
                if (b.Paused) { col = Dim; return "Paused"; }
                if (JobIdle(b) == null)
                {
                    slow = true;
                    // Nobody at a workplace the player has not put last (Low), in a season it yields at least half its
                    // usual (gatherers, hunters and fishers in winter are left empty on purpose), for a whole game day.
                    if (d.maxWorkers > 0 && b.Workers == 0)
                    {
                        if (b.Priority == Priority.Low || d.SeasonYield(S.Season) < 0.5f) return null;
                        slowDay = true;
                        return "No workers";
                    }
                    string st = b.Status;
                    if (st == "Needs tools") return "No tools";
                    if (st == "Waiting for goods to be carried off") return "Full";
                    if (st == "Waiting for inputs")
                    {
                        // The first input with none here and none in store (one on its way from storage is no problem).
                        for (int i = 0; i < d.inputs.Length; i++)
                        {
                            float have = 0, store = 0;
                            foreach (var id in d.inputs[i].ids) { int r = S.ResIndex(id); if (r >= 0) { have += b.In[r]; store += S.StockTotals[r]; } }
                            if (have <= 0.001f && store < 0.5f) { col = Bad; return NoLabel(d.inputs[i].ids[0]); }
                        }
                        return null;
                    }
                    if (st != null && st.StartsWith("No ") && st.EndsWith(" left nearby")) return NoLeft(st);
                    slow = false;
                }
            }
            if (d.housing > 0 && b.Residents > 0)
            {
                if (!b.Heated) { col = Bad; return "Cold"; }
                if (S.NeedActive("water") && !b.HasWater) return "No water";
                if (S.NeedActive("faith") && !b.HasFaith) return "No faith";
            }
            if (b.IsStorage && b.Store != null && !d.IsWorkplace && b.Stored >= S.Capacity(b) * 0.97f) return "Full";
            return null;
        }

        /// <summary>"No trees left nearby" (the sim's status) as "No trees left" (made once per status).</summary>
        string NoLeft(string status)
        {
            if (_noLabel.TryGetValue(status, out var s)) return s;
            s = status.Substring(0, status.Length - " nearby".Length);
            _noLabel[status] = s;
            return s;
        }

        /// <summary>A badge's text width (measured once per text and text size).</summary>
        float BadgeTextW(string text)
        {
            if (_badgeFont != Small.fontSize) { _badgeW.Clear(); _badgeFont = Small.fontSize; }
            if (_badgeW.TryGetValue(text, out float w)) return w;
            w = SmallText.CalcSize(Temp(text)).x;
            _badgeW[text] = w;
            return w;
        }

        /// <summary>Which buildings get a badge and where, worked out on the Layout pass (the other passes of the frame reuse it).</summary>
        void PlaceBadges()
        {
            _badges.Clear();
            var cam = G.Cam.Cam;
            if (cam == null) return;
            float hour = D.Balance.dayLengthSeconds / 24f;
            Vector3 eye = cam.transform.position;
            float cx = VW / 2, cy = VH / 2;
            int used = 0;
            foreach (var b in S.Buildings)
            {
                string text = Problem(b, out var col, out bool slow, out bool slowDay);
                if (text == null) { _problemSince.Remove(b.Id); continue; }
                if (!_problemSince.TryGetValue(b.Id, out var seen) || !ReferenceEquals(seen.label, text)) { seen = (text, S.Time); _problemSince[b.Id] = seen; }
                if (slow && S.Time - seen.since < (slowDay ? 24f : 2f) * hour) continue;
                // Over the roof of a home (huts stand about 2.5 m), lower over the low workplaces and stores.
                float top = b.IsSite ? 1.2f : b.Def.housing > 0 ? 2.3f : 1.5f;
                var world = new Vector3(b.Center.x, M.HeightAt(b.Center.x, b.Center.y) + top, b.Center.y);
                if ((world - eye).magnitude > BadgeRange) continue;
                var at = cam.WorldToScreenPoint(world);
                if (at.z <= 0) continue;
                float x = at.x / Scale, y = (Screen.height - at.y) / Scale;
                if (x < 0 || y < 0 || x > VW || y > VH) continue;
                if (used == _badgePool.Count) _badgePool.Add(new BadgeSlot());
                var s = _badgePool[used++];
                float w = BadgeTextW(text) + 26;
                s.B = b; s.Text = text; s.Col = col;
                s.R = new Rect(x - w / 2, y - 26, w, 20);
                s.Score = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                _badges.Add(s);
            }
            // Nearest the middle of the screen first; a badge over a panel, or over a badge already placed, is left out.
            _badges.Sort(ByScore);
            int kept = 0;
            for (int i = 0; i < _badges.Count && kept < MaxBadges; i++)
            {
                var r = _badges[i].R;
                bool clear = true;
                foreach (var p in _prevPanels) if (p.Overlaps(r)) { clear = false; break; }
                for (int k = 0; clear && k < kept; k++) if (_badges[k].R.Overlaps(r)) clear = false;
                if (clear) _badges[kept++] = _badges[i];
            }
            _badges.RemoveRange(kept, _badges.Count - kept);
        }

        void DrawBadges()
        {
            _badgeRects.Clear();
            if (!BadgesOn || ResearchOpen || EndOpen || GoneOpen || MenuOpen) { _badges.Clear(); return; }
            if (Event.current.type == EventType.Layout) PlaceBadges();
            foreach (var s in _badges)
            {
                if (!s.B.Alive) continue;
                var r = s.R;
                GUI.Box(r, GUIContent.none, UiTheme.Tip);
                Fill(new Rect(r.x + 7, r.y + 6, 8, 8), s.Col);
                GUI.Label(new Rect(r.x + 19, r.y, r.width - 21, r.height), s.Text, SmallText);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { Select(s.B); _badges.Clear(); return; }
                _badgeRects.Add(r);
            }
        }

        /// <summary>-badgeshots: the badges on screen now ("Woodcutter: No workers, ...").</summary>
        public string BadgeSummary() => $"{_badges.Count} badges: " + string.Join(", ", _badges.Select(s => $"{s.B.Def.name} {s.Text}"));
        public System.Collections.Generic.IEnumerable<Building> BadgeBuildings() => _badges.Select(s => s.B).ToList();

        static string _badgeBtnOn, _badgeBtnOff;

        /// <summary>The build bar's toggle (after the builders control).</summary>
        void BadgeButton(Rect r)
        {
            if (GUI.Button(r, BadgesOn ? _badgeBtnOn ??= $"Problems {Col("P", Faint)}" : _badgeBtnOff ??= $"Problems {Col("P", Faint)}", BadgesOn ? ButtonOn : Button)) ToggleBadges();
            if (Over(r)) Tip(r, (BadgesOn ? "Problem badges are on" : "Problem badges are off") + " (P): a tag over each building with a problem you can fix (no workers, no tools, a missing input, a cold home, a full store, a flood). Click a badge to select the building.");
        }
    }
}
