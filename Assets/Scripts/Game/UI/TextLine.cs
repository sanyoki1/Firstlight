using System;
using UnityEngine;

namespace Firstlight.Game.UI
{
    /// <summary>
    /// A line of HUD text built in a reused buffer. <see cref="To"/> hands back the string kept from the last time when the
    /// text came out the same, so a label whose figures have not changed makes no garbage (the HUD used to build every label
    /// and tooltip as a new string on every OnGUI call, two or more a frame). Numbers go through TryFormat with the same
    /// format strings and culture as string interpolation (<c>$"{v:0.#}"</c>), so the text is exactly what it was.
    /// One builder per caller; a line must be finished (<see cref="To"/>) before the same builder starts another.
    /// </summary>
    public sealed class TextLine
    {
        char[] _buf = new char[256];
        int _n;

        public TextLine Begin() { _n = 0; return this; }

        void Need(int k)
        {
            if (_n + k > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _n + k));
        }

        /// <summary>Appends a string (null appends nothing, as in interpolation).</summary>
        public TextLine S(string s)
        {
            if (string.IsNullOrEmpty(s)) return this;
            Need(s.Length);
            s.CopyTo(0, _buf, _n, s.Length);
            _n += s.Length;
            return this;
        }

        /// <summary>Appends s from index start on.</summary>
        public TextLine S(string s, int start)
        {
            if (s == null || start >= s.Length) return this;
            Need(s.Length - start);
            s.CopyTo(start, _buf, _n, s.Length - start);
            _n += s.Length - start;
            return this;
        }

        public TextLine Ch(char c) { Need(1); _buf[_n++] = c; return this; }

        public TextLine F(float v, string format = null)
        {
            int w;
            while (!v.TryFormat(new Span<char>(_buf, _n, _buf.Length - _n), out w, format, null)) Need(_buf.Length);
            _n += w;
            return this;
        }

        public TextLine I(int v, string format = null)
        {
            int w;
            while (!v.TryFormat(new Span<char>(_buf, _n, _buf.Length - _n), out w, format, null)) Need(_buf.Length);
            _n += w;
            return this;
        }

        /// <summary>Opens a colour tag, as <see cref="UiTheme.Col"/> does; close it with <see cref="E"/>.</summary>
        public TextLine C(Color c) => S(UiTheme.Tag(c));
        public TextLine E() => S("</color>");

        /// <summary>The text: the cached string when it is the same as last time, else a new one (kept in the cache).</summary>
        public string To(ref string cache)
        {
            if (cache != null && cache.Length == _n)
            {
                int i = 0;
                while (i < _n && cache[i] == _buf[i]) i++;
                if (i == _n) return cache;
            }
            cache = new string(_buf, 0, _n);
            return cache;
        }
    }
}
