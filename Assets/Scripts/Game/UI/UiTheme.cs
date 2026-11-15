using UnityEngine;

namespace Firstlight.Game.UI
{
    /// <summary>
    /// The HUD's look: one dark, neutral panel style with thin borders, muted text colours and a few accent
    /// colours for state (good / warning / bad / queued). Built from tiny point-filtered textures at start, so no
    /// default grey Unity boxes. Layout is in a 1080-pixel-high virtual screen, scaled to the real resolution.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Text = new(0.90f, 0.88f, 0.83f);
        public static readonly Color Dim = new(0.74f, 0.72f, 0.67f);
        // Faint is for decoration and disabled things only (ticks, "-", key letters on buttons), never for text the player
        // needs; 0.64 (was 0.55) so even that reads on the 0.11 panel.
        public static readonly Color Faint = new(0.64f, 0.63f, 0.59f);
        public static readonly Color Good = new(0.58f, 0.80f, 0.52f);
        public static readonly Color Warn = new(0.93f, 0.74f, 0.38f);
        public static readonly Color Bad = new(0.92f, 0.45f, 0.38f);
        public static readonly Color Accent = new(0.86f, 0.74f, 0.50f);
        public static readonly Color Info = new(0.62f, 0.75f, 0.86f);

        public static GUIStyle Panel, Header, Label, Small, Bold, Title, Big, Banner, Button, ButtonOn, ButtonDim, Tip, Chip, ChipGood, Center, Right, Wrap, SmallWrap;
        public static Texture2D White;
        static Texture2D _panel, _header, _btn, _btnHover, _btnActive, _btnOn, _btnOnHover, _tip, _chip, _chipGood;
        static bool _ready;

        /// <summary>Scale from virtual (1080 high) to real pixels.</summary>
        // The fit to the screen height is at least 0.85 (it was 0.75: at 1280x720 the 14 px small text came out at 10 px);
        // at 1280x720 the layout is 1506 x 847 virtual pixels, and the top bar drops goods from the right as designed.
        // The player's interface size multiplies that (a size below 100% can still go smaller, down to 0.6).
        // At most Screen.height / 800: the tallest panels (settings, help) are about 780 high and must fit, or a large
        // interface size pushed their Back buttons off the bottom of the screen (160% at 1080p or 720p).
        public static float Scale => Mathf.Clamp(Mathf.Min(Mathf.Max(Screen.height / 1080f, 0.85f) * UserScale, Screen.height / 800f), 0.6f, 3f);
        /// <summary>The player's interface size setting (1 = the 1080-high layout fitted to the screen height).</summary>
        public static float UserScale = 1f;
        public static float VW => Screen.width / Scale;
        public static float VH => Screen.height / Scale;

        static Texture2D Box(Color fill, Color border)
        {
            var t = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    t.SetPixel(x, y, x == 1 && y == 1 ? fill : border);
            t.Apply();
            return t;
        }

        static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle font = FontStyle.Normal, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, fontStyle = font, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
            s.normal.textColor = color; s.hover.textColor = color;
            s.padding = new RectOffset(0, 0, 0, 0); s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }

        static GUIStyle ButtonStyle(Texture2D n, Texture2D h, Texture2D a, Color text)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter, richText = true, clipping = TextClipping.Clip };
            s.normal.background = n; s.hover.background = h; s.active.background = a; s.focused.background = n;
            s.onNormal.background = n; s.onHover.background = h; s.onActive.background = a;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
            s.border = new RectOffset(1, 1, 1, 1);
            s.padding = new RectOffset(6, 6, 2, 2);
            s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }

        /// <summary>Builds the styles (call from OnGUI: GUI.skin only exists there).</summary>
        public static void Ensure()
        {
            if (_ready) { FontFloor(); return; }
            _ready = true;
            White = Box(Color.white, Color.white);
            _panel = Box(new Color(0.105f, 0.11f, 0.11f, 0.94f), new Color(0.30f, 0.31f, 0.29f, 1f));
            _header = Box(new Color(0.16f, 0.165f, 0.155f, 0.98f), new Color(0.30f, 0.31f, 0.29f, 1f));
            _btn = Box(new Color(0.19f, 0.195f, 0.185f, 1f), new Color(0.33f, 0.34f, 0.32f, 1f));
            _btnHover = Box(new Color(0.25f, 0.255f, 0.24f, 1f), new Color(0.48f, 0.48f, 0.44f, 1f));
            _btnActive = Box(new Color(0.30f, 0.30f, 0.28f, 1f), new Color(0.6f, 0.58f, 0.5f, 1f));
            _btnOn = Box(new Color(0.33f, 0.29f, 0.20f, 1f), new Color(0.70f, 0.60f, 0.38f, 1f));
            _btnOnHover = Box(new Color(0.38f, 0.33f, 0.23f, 1f), new Color(0.80f, 0.68f, 0.44f, 1f));
            _tip = Box(new Color(0.07f, 0.075f, 0.075f, 0.97f), new Color(0.55f, 0.52f, 0.44f, 1f));
            _chip = Box(new Color(0.16f, 0.13f, 0.09f, 0.94f), new Color(0.62f, 0.48f, 0.26f, 1f));

            Panel = new GUIStyle { border = new RectOffset(1, 1, 1, 1) }; Panel.normal.background = _panel;
            Header = new GUIStyle { border = new RectOffset(1, 1, 1, 1) }; Header.normal.background = _header;
            Tip = new GUIStyle(Style(15, Text, TextAnchor.UpperLeft, FontStyle.Normal, true)) { border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(10, 10, 8, 8) };
            Tip.normal.background = _tip;
            Chip = new GUIStyle(Style(15, Warn, TextAnchor.MiddleLeft)) { border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(10, 10, 0, 0) };
            Chip.normal.background = _chip;
            // A chip for good news (the next age is ready): the same shape, green.
            _chipGood = Box(new Color(0.09f, 0.14f, 0.09f, 0.94f), new Color(0.42f, 0.64f, 0.36f, 1f));
            ChipGood = new GUIStyle(Chip);
            ChipGood.normal.background = _chipGood; ChipGood.normal.textColor = ChipGood.hover.textColor = Good;
            Label = Style(16, Text);
            Small = Style(14, Dim);
            Bold = Style(16, Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            Title = Style(20, Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            Big = Style(24, Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Banner = Style(40, Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Center = Style(16, Text, TextAnchor.MiddleCenter);
            Right = Style(16, Text, TextAnchor.MiddleRight);
            Wrap = Style(16, Text, TextAnchor.UpperLeft, FontStyle.Normal, true);
            SmallWrap = Style(14, Dim, TextAnchor.UpperLeft, FontStyle.Normal, true);
            Button = ButtonStyle(_btn, _btnHover, _btnActive, Text);
            ButtonOn = ButtonStyle(_btnOn, _btnOnHover, _btnActive, new Color(1f, 0.93f, 0.78f));
            ButtonDim = ButtonStyle(_btn, _btn, _btn, Faint);
            ButtonDim.normal.background = ButtonDim.hover.background = ButtonDim.active.background = Box(new Color(0.15f, 0.155f, 0.15f, 1f), new Color(0.27f, 0.28f, 0.26f, 1f));
            // Variants the panels used to copy on every call (a GUIStyle is a native object with a finalizer): made once here.
            // Those copied from Small follow its size floor (FontFloor), as a copy made at the call did.
            SmallRight = new GUIStyle(Small) { alignment = TextAnchor.MiddleRight };
            SmallCenter = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter };
            SmallFoot = new GUIStyle(Small);
            SmallText = new GUIStyle(Small); SmallText.normal.textColor = SmallText.hover.textColor = Text;
            Label14 = new GUIStyle(Label) { fontSize = 14 };
            LabelCenter = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            FeedRow = new GUIStyle(Label) { fontSize = 14, wordWrap = true };
            PlaceTip = new GUIStyle(Wrap) { fontSize = 15 };
            Title24 = new GUIStyle(Title) { fontSize = 24 };
            Title28Center = new GUIStyle(Title) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            Title30Center = new GUIStyle(Title) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            Title34 = new GUIStyle(Title) { fontSize = 34 };
            Note15Center = new GUIStyle(SmallWrap) { alignment = TextAnchor.UpperCenter, fontSize = 15 };
            Button12 = new GUIStyle(Button) { fontSize = 12 };
            NodeSub = new GUIStyle(Small) { fontSize = 12 };
            NodeUnlocks = new GUIStyle(Small) { fontSize = 11 };
            _floorFor = -1; FontFloor();
        }

        /// <summary>Made once in <see cref="Ensure"/> (see there).</summary>
        public static GUIStyle SmallRight, SmallCenter, SmallFoot, SmallText, Label14, LabelCenter, FeedRow, PlaceTip, Title24, Title28Center, Title30Center, Title34,
            Note15Center, Button12, NodeSub, NodeUnlocks;

        /// <summary>Smallest text on screen, in real pixels: the small styles grow when the layout is scaled down.</summary>
        public const float MinTextPx = 13f;
        static float _floorFor = -1;

        /// <summary>
        /// Keeps the small text (14 px in the layout) at about 13 real pixels or more: when the scale is below 1 the small
        /// styles get a larger size (15 at 0.85, at most 16 when the player picks a small interface size). Only looked at again
        /// when the scale changes.
        /// </summary>
        static void FontFloor()
        {
            float s = Scale;
            if (s == _floorFor) return;
            _floorFor = s;
            int small = Mathf.Clamp(Mathf.RoundToInt(MinTextPx / s), 14, 16);   // at most the 16 of normal text, so rows still hold it
            Small.fontSize = SmallWrap.fontSize = small;
            SmallRight.fontSize = SmallCenter.fontSize = SmallText.fontSize = small;
            SmallFoot.fontSize = Px(12);
            Tip.fontSize = Chip.fontSize = ChipGood.fontSize = Mathf.Max(15, small);
        }

        /// <summary>A layout size of at least <see cref="MinTextPx"/> real pixels (for styles made with a smaller size).</summary>
        public static int Px(int size) => Mathf.Max(size, Mathf.Min(16, Mathf.RoundToInt(MinTextPx / Scale)));

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        public static string Col(string text, Color c) => string.Concat(Tag(c), text, "</color>");

        // "<color=#RRGGBB>" for each colour asked for, made once (Col used to format the hex on every call).
        static readonly Color[] _tagCols = new Color[64];
        static readonly string[] _tags = new string[64];
        static int _nTags;

        /// <summary>The opening colour tag for a colour (the same text as before: "&lt;color=#" + Hex + "&gt;").</summary>
        public static string Tag(Color c)
        {
            for (int i = 0; i < _nTags; i++) if (_tagCols[i].Equals(c)) return _tags[i];
            string t = "<color=#" + Hex(c) + ">";
            if (_nTags < _tags.Length) { _tagCols[_nTags] = c; _tags[_nTags++] = t; }
            return t;
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        /// <summary>A horizontal bar 0..1 with a dark track.</summary>
        public static void Bar(Rect r, float v, Color c)
        {
            Fill(r, new Color(0.05f, 0.05f, 0.05f, 0.9f));
            Fill(new Rect(r.x + 1, r.y + 1, Mathf.Max(0, (r.width - 2) * Mathf.Clamp01(v)), r.height - 2), c);
        }
    }
}
