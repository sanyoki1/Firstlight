using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // A store's settings (the Storage settings panel beside the store's info panel): which goods it takes, grouped by kind,
    // and per good keep at least / keep at most. The info panel itself has the priority, a one-line summary, and Copy /
    // Paste (Hud.Info.cs calls DrawStoreControls).
    public sealed partial class Hud
    {
        public bool StorageOpen;

        /// <summary>Opens or closes the Storage settings panel for the selected store (it takes the side panels' place).</summary>
        public void ToggleStorage()
        {
            StorageOpen = !StorageOpen;
            if (StorageOpen) GoalsOpen = JobsOpen = StocksOpen = LawsOpen = EventsOpen = false;
        }

        StoreSettings _storeClip;        // Copy / Paste
        string _storeClipFrom;

        public void CopyStore(Building b)
        {
            var st = S.GetStoreSettings(b);
            if (st == null) return;
            _storeClip = st; _storeClipFrom = b.Def.name;
        }

        public void PasteStore(Building b) { if (_storeClip != null) S.ApplyStoreSettings(b, _storeClip); }

        // ------------------------------------------------------------------ in the store's info panel

        static readonly string[] StorePriorityNames = { "Low", "Normal", "High" };

        /// <summary>The store's priority, what it takes in a line, and Storage settings / Copy / Paste.</summary>
        void DrawStoreControls(Building b)
        {
            if (!S.IsStore(b)) return;
            Gap(4);
            GUI.Label(new Rect(_lx, _ly + 4, 70, 22), "Priority", Small);
            for (int i = 0; i < 3; i++)
            {
                var pr = new Rect(_lx + 70 + i * 76, _ly, 72, 28);
                if (GUI.Button(pr, StorePriorityNames[i], (int)b.StorePriority == i ? ButtonOn : Button)) S.SetStorePriority(b, (Priority)i);
            }
            Tip(new Rect(_lx + 70, _ly, 226, 28), "New goods go to the highest-priority store that takes them, and haulers with nothing else to do move goods up from lower-priority stores.");
            _ly += 34;
            Para(StoreSummary(b));
            float w = (_lw - 8) / 3f;
            var sr = new Rect(_lx, _ly, w + 30, 28);
            if (GUI.Button(sr, "Storage settings", StorageOpen ? ButtonOn : Button)) ToggleStorage();
            Tip(sr, "Choose which goods this store takes and how much of each to keep here.");
            var cr = new Rect(_lx + w + 34, _ly, w - 15, 28);
            if (GUI.Button(cr, "Copy", Button)) CopyStore(b);
            Tip(cr, "Copy this store's settings (goods, priority, limits) to paste onto other stores.");
            var pa = new Rect(_lx + 2 * w + 23, _ly, w - 15, 28);
            bool can = _storeClip != null;
            if (GUI.Button(pa, "Paste", can ? Button : ButtonDim) && can) PasteStore(b);
            if (Over(pa)) Tip(pa, can ? $"Paste the settings copied from the {_storeClipFrom}." + (b.Def.accepts.Length > 0 ? $" A {b.Def.name} keeps to what it can hold." : "") : "Copy a store's settings first.");
            _ly += 34;
        }

        readonly StringBuilder _sb = new();

        /// <summary>"Takes everything" / "Takes 6 of 21 goods: ..." and the limits, in a sentence or two.</summary>
        string StoreSummary(Building b)
        {
            var (takes, holds) = S.StoreTakes(b);
            var sb = _sb; sb.Clear();
            if (takes == 0) sb.Append(Col("Takes nothing: goods here are moved out.", Warn));
            else if (takes == holds) sb.Append(b.Def.accepts.Length > 0 ? $"Takes all {string.Join(" and ", b.Def.accepts)}." : "Takes everything.");
            else
            {
                sb.Append($"Takes {takes} of {holds} goods");
                // Name the kinds it takes whole, or the goods themselves when there are few.
                if (takes <= 4)
                {
                    sb.Append(": ");
                    int n = 0;
                    for (int r = 0; r < S.ResCount; r++) if (S.Accepts(b, r)) { if (n++ > 0) sb.Append(", "); sb.Append(D.Resources[r].name.ToLower()); }
                }
                sb.Append('.');
            }
            AppendLimits(sb, b, true);
            AppendLimits(sb, b, false);
            return sb.ToString();
        }

        void AppendLimits(StringBuilder sb, Building b, bool min)
        {
            var a = min ? b.KeepMin : b.KeepMax;
            if (a == null) return;
            int n = 0;
            for (int r = 0; r < S.ResCount; r++)
            {
                if (a[r] <= 0 || min && !S.Accepts(b, r)) continue;
                sb.Append(n++ == 0 ? (min ? " Keeps at least " : " At most ") : ", ");
                if (n > 4) { sb.Append("..."); break; }
                sb.Append($"{a[r]:0} {D.Resources[r].name.ToLower()}");
            }
            if (n > 0) sb.Append('.');
        }

        // ------------------------------------------------------------------ the settings panel

        float _storageH = 400;
        Vector2 _storageScroll;
        readonly HashSet<string> _storageFolded = new();
        readonly List<string> _storeCats = new();
        readonly List<int> _catGoods = new();
        GUIStyle _numField, _check;

        // The limit being typed: which store, good and field, the text so far, and where the field is (panel coordinates).
        Building _editB; int _editRes = -1; bool _editMin; string _editText; Rect _editRect;
        bool _cancelEdit;
        /// <summary>A number is being typed: the keyboard belongs to the field (no hotkeys, no camera keys).</summary>
        bool Typing => _editB != null;

        void StorageStyles()
        {
            if (_numField != null) return;
            _numField = new GUIStyle(GUI.skin.textField) { fontSize = 14, alignment = TextAnchor.MiddleRight, padding = new RectOffset(4, 6, 2, 2) };
            var tex = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) tex.SetPixel(x, y, x == 1 && y == 1 ? new Color(0.07f, 0.075f, 0.07f, 1f) : new Color(0.42f, 0.39f, 0.32f, 1f));
            tex.Apply();
            _numField.border = new RectOffset(1, 1, 1, 1);
            _numField.normal.background = _numField.focused.background = _numField.hover.background = tex;
            _numField.normal.textColor = _numField.focused.textColor = _numField.hover.textColor = Text;
            _check = new GUIStyle(Button) { fontSize = 14, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 1) };
        }

        static string CatName(string c) => c switch
        {
            "food" => "Food", "material" => "Materials", "fuel" => "Fuel", "tools" => "Tools", "goods" => "Goods",
            _ => string.IsNullOrEmpty(c) ? "Other" : char.ToUpper(c[0]) + c.Substring(1),
        };

        static readonly string[] CatOrder = { "food", "material", "fuel", "tools", "goods" };

        /// <summary>A checkbox: 2 ticked, 1 some (a group with some goods ticked), 0 not. True when clicked.</summary>
        bool Check(Rect r, int state) => GUI.Button(r, state == 2 ? "✓" : state == 1 ? "·" : "", state == 2 ? ButtonOn : _check);

        /// <summary>
        /// First thing each OnGUI call (before any panel can use the event): Enter, Tab or a click anywhere but the field
        /// being typed in sets the number; Esc (seen by Keys) drops it; a closed panel lets go of the keyboard.
        /// </summary>
        void StorageInput()
        {
            if (_editB == null) return;
            var e = Event.current;
            if (_cancelEdit) { _cancelEdit = false; EndEdit(); return; }
            if (!StorageOpen || Selected != _editB) { CommitEdit(); return; }
            if (e.type == EventType.MouseDown && !_editRect.Contains(e.mousePosition)) CommitEdit();
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab)) { CommitEdit(); e.Use(); }
        }

        void DrawStorage()
        {
            var b = Selected;
            if (b == null || !S.IsStore(b) || b.IsSite) { CommitEdit(); StorageOpen = false; return; }
            StorageStyles();
            if (_editB != null && _editB != b) CommitEdit();

            const float W = 460;
            var r = BeginSide("Storage settings", W, _storageH, () => { CommitEdit(); StorageOpen = false; });
            Row(Col(b.Def.name, Dim), Small, 20);
            var (takes, holds) = S.StoreTakes(b);
            if (GUI.Button(new Rect(_lx, _ly, 90, 26), "All", Button)) S.SetStoreAllowAll(b, true);
            if (GUI.Button(new Rect(_lx + 96, _ly, 90, 26), "None", Button)) S.SetStoreAllowAll(b, false);
            GUI.Label(new Rect(_lx + 196, _ly, _lw - 196, 26), $"Takes {takes} of {holds}", SmallRight);
            _ly += 32;

            // Column heads.
            float xHere = _lw - 210, xMin = _lw - 140, xMax = _lw - 66;
            GUI.Label(new Rect(_lx, _ly, 160, 20), "Good", Small);
            GUI.Label(new Rect(_lx + xHere, _ly, 56, 20), "Here", SmallRight);
            var hm = new Rect(_lx + xMin, _ly, 66, 20); GUI.Label(hm, "At least", SmallRight);
            Tip(hm, "Keep at least this much here: new goods come here first and haulers bring it from other stores. Nobody takes it below this while another store has some (food and fuel too, until every other store is out).");
            var hx = new Rect(_lx + xMax, _ly, 66, 20); GUI.Label(hx, "At most", SmallRight);
            Tip(hx, "Keep at most this much here: no more comes in, and anything above it is moved to other stores.");
            _ly += 22;

            // The goods it can hold, grouped by kind (the usual kinds first, any others after).
            _storeCats.Clear();
            foreach (var c in CatOrder) _storeCats.Add(c);
            for (int k = 0; k < S.ResCount; k++) { var c = D.Resources[k].category ?? ""; if (!_storeCats.Contains(c)) _storeCats.Add(c); }
            float listTop = _ly;
            float maxList = math.max(160, VH - listTop - 70);
            var view = new Rect(_lx - 4, listTop, _lw + 8, 0);
            // Content height first (rows are fixed height), then draw inside a scroll view if it does not fit.
            float contentH = 0;
            foreach (var c in _storeCats)
            {
                int n = 0;
                for (int k = 0; k < S.ResCount; k++) if (D.Resources[k].category == c && S.CanHold(b, k)) n++;
                if (n == 0) continue;
                contentH += 28 + (_storageFolded.Contains(c) ? 0 : n * 26) + 4;
            }
            view.height = math.min(contentH, maxList);
            bool scroll = contentH > view.height + 0.5f;
            var inner = new Rect(0, 0, view.width - (scroll ? 16 : 0), contentH);
            Vector2 offset;
            if (scroll) { _storageScroll = GUI.BeginScrollView(view, _storageScroll, inner); offset = new Vector2(view.x, view.y) - _storageScroll; }
            else { GUI.BeginGroup(view); offset = new Vector2(view.x, view.y); _storageScroll = Vector2.zero; }
            float y = 0, x0 = 4, lw = inner.width - 8;
            foreach (var c in _storeCats)
            {
                _catGoods.Clear();
                for (int k = 0; k < S.ResCount; k++) if (D.Resources[k].category == c && S.CanHold(b, k)) _catGoods.Add(k);
                if (_catGoods.Count == 0) continue;
                int on = 0; float here = 0;
                foreach (int k in _catGoods) { if (S.Accepts(b, k)) on++; here += b.Store[k]; }
                bool folded = _storageFolded.Contains(c);
                if (GUI.Button(new Rect(x0, y + 2, 22, 22), folded ? "+" : "-", _check)) { if (folded) _storageFolded.Remove(c); else _storageFolded.Add(c); }
                if (Check(new Rect(x0 + 28, y + 2, 22, 22), on == _catGoods.Count ? 2 : on > 0 ? 1 : 0)) S.SetStoreAllowAll(b, on < _catGoods.Count, c);
                GUI.Label(new Rect(x0 + 58, y, 220, 26), $"<b>{CatName(c)}</b>  {Col($"{on} of {_catGoods.Count}", Dim)}", Label);
                if (here >= 0.5f) GUI.Label(new Rect(x0 + xHere, y, 56, 26), $"{here:0}", Right);
                y += 28;
                if (!folded)
                    foreach (int k in _catGoods)
                    {
                        var rd = D.Resources[k];
                        bool allowed = S.Accepts(b, k);
                        if (Check(new Rect(x0 + 28, y + 2, 22, 22), allowed ? 2 : 0)) S.SetStoreAllow(b, k, !allowed);
                        string name = rd.name;
                        if (!S.ResourceUnlocked(rd.id)) name = Col(name, Faint);   // not yet known (its research is not done)
                        else if (!allowed) name = Col(name, Dim);
                        GUI.Label(new Rect(x0 + 58, y, xHere - 64, 24), name, Label);
                        float amt = b.Store[k];
                        if (amt >= 0.5f) GUI.Label(new Rect(x0 + xHere, y, 56, 24), !allowed || S.KeepMaxOf(b, k) > 0 && amt > S.KeepMaxOf(b, k) + 0.5f ? Col($"{amt:0}", Warn) : $"{amt:0}", Right);
                        LimitField(new Rect(x0 + xMin + 6, y + 1, 60, 22), offset, b, k, true, allowed);
                        LimitField(new Rect(x0 + xMax + 6, y + 1, 60, 22), offset, b, k, false, true);
                        y += 26;
                    }
                y += 4;
            }
            if (scroll) GUI.EndScrollView(); else GUI.EndGroup();
            _ly = listTop + view.height + 6;
            Para(Col("Goods it no longer takes, or holds over its limit, are moved out by the haulers (bottom bar).", Dim));
            _storageH = _ly - r.y + 8;
        }

        /// <summary>A keep-at-least / keep-at-most field: shows the limit (blank = none); typing sets it on Enter, Tab or a
        /// click elsewhere; Esc drops what was typed.</summary>
        void LimitField(Rect r, Vector2 offset, Building b, int res, bool min, bool enabled)
        {
            float v = min ? (b.KeepMin != null ? b.KeepMin[res] : 0) : (b.KeepMax != null ? b.KeepMax[res] : 0);
            string name = (min ? "kmin" : "kmax") + res;
            bool editing = _editB == b && _editRes == res && _editMin == min;
            if (!enabled && !editing) { GUI.Label(r, v > 0 ? Col($"{v:0}", Faint) : "", SmallRight); return; }
            string shown = editing ? _editText : v > 0 ? v.ToString("0") : "";
            GUI.SetNextControlName(name);
            string t = GUI.TextField(r, shown, 7, _numField);
            if (GUI.GetNameOfFocusedControl() != name) return;
            if (!editing)
            {
                if (_editB != null) CommitEdit();
                _editB = b; _editRes = res; _editMin = min;
            }
            var sb = _sb; sb.Clear();
            foreach (char ch in t) if (ch >= '0' && ch <= '9') sb.Append(ch);
            _editText = sb.ToString();
            _editRect = new Rect(r.x + offset.x, r.y + offset.y, r.width, r.height);
        }

        /// <summary>Sets the number typed (blank = no limit) and lets go of the keyboard.</summary>
        void CommitEdit()
        {
            if (_editB != null && _editB.Alive)
            {
                float v = string.IsNullOrEmpty(_editText) ? 0 : float.TryParse(_editText, out float p) ? p : 0;
                if (_editMin) S.SetKeepMin(_editB, _editRes, v); else S.SetKeepMax(_editB, _editRes, v);
            }
            EndEdit();
        }

        void EndEdit()
        {
            _editB = null; _editRes = -1; _editText = null;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }

        // ------------------------------------------------------------------ the goods panel: where a good is kept

        /// <summary>"Kept in: Storage Pile 40, Granary 12" for a good (the stocks panel's tooltip), and whether no store takes it.</summary>
        string WhereKept(int r)
        {
            var sb = _sb; sb.Clear();
            int n = 0;
            foreach (var s in S.Buildings)
            {
                if (!s.Active || s.Store == null || s.Store[r] < 0.5f) continue;
                sb.Append(n++ == 0 ? "Kept in: " : ", ");
                sb.Append($"{s.Def.name} {s.Store[r]:0}");
                if (S.KeepMinOf(s, r) > 0) sb.Append($" (keeps {S.KeepMinOf(s, r):0})");
            }
            if (n == 0) sb.Append("None in store.");
            int takes = 0;
            foreach (var s in S.Buildings) if (s.Active && s.Store != null && S.Accepts(s, r)) takes++;
            sb.Append(takes == 0 ? "\nNo store takes it: it waits where it is made. Let it into a store (select the store, Storage settings)."
                                 : takes == 1 ? "\n1 store takes it." : $"\n{takes} stores take it.");
            return sb.ToString();
        }
    }
}
