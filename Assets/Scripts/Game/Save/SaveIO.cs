using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Mathematics;

namespace Firstlight.Game
{
    /// <summary>Binary writer with the shapes the save uses (exact float bits, nullable arrays and strings).</summary>
    public sealed class SaveWriter
    {
        public readonly BinaryWriter W;
        public SaveWriter(Stream s) { W = new BinaryWriter(s, System.Text.Encoding.UTF8, true); }

        public void Int(int v) => W.Write(v);
        public void Long(long v) => W.Write(v);
        public void ULong(ulong v) => W.Write(v);
        public void Float(float v) => W.Write(v);
        public void Double(double v) => W.Write(v);
        public void Bool(bool v) => W.Write(v);
        public void Byte(byte v) => W.Write(v);
        public void Str(string v) { W.Write(v != null); if (v != null) W.Write(v); }
        public void F2(float2 v) { W.Write(v.x); W.Write(v.y); }
        public void I2(int2 v) { W.Write(v.x); W.Write(v.y); }
        public void Floats(float[] a) { W.Write(a == null ? -1 : a.Length); if (a != null) foreach (var v in a) W.Write(v); }
        public void Bytes(byte[] a) { W.Write(a == null ? -1 : a.Length); if (a != null) W.Write(a); }
        public void Bools(bool[] a) { W.Write(a == null ? -1 : a.Length); if (a != null) foreach (var v in a) W.Write(v); }
        public void Ints(int[] a) { W.Write(a == null ? -1 : a.Length); if (a != null) foreach (var v in a) W.Write(v); }
        public void Strs(IList<string> a) { W.Write(a == null ? -1 : a.Count); if (a != null) foreach (var v in a) Str(v); }

        /// <summary>A length-prefixed block, so a reader can skip blocks it does not know.</summary>
        public void Block(byte[] data) => Bytes(data ?? Array.Empty<byte>());

        /// <summary>Public fields of a plain object (ints, floats, bools, strings, string lists, lists of plain objects), in declaration order.</summary>
        public void Fields(object o)
        {
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.FieldType; var v = f.GetValue(o);
                if (t == typeof(int)) Int((int)v);
                else if (t == typeof(float)) Float((float)v);
                else if (t == typeof(bool)) Bool((bool)v);
                else if (t == typeof(string)) Str((string)v);
                else if (t == typeof(List<string>)) Strs((List<string>)v);
                else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var list = (System.Collections.IList)v;
                    Int(list?.Count ?? -1);
                    if (list != null) foreach (var item in list) Fields(item);
                }
                else throw new InvalidOperationException($"Save: unsupported field {f.Name} ({t})");
            }
        }
    }

    public sealed class SaveReader
    {
        public readonly BinaryReader R;
        public SaveReader(Stream s) { R = new BinaryReader(s, System.Text.Encoding.UTF8, true); }

        public int Int() => R.ReadInt32();
        public long Long() => R.ReadInt64();
        public ulong ULong() => R.ReadUInt64();
        public float Float() => R.ReadSingle();
        public double Double() => R.ReadDouble();
        public bool Bool() => R.ReadBoolean();
        public byte Byte() => R.ReadByte();
        public string Str() => R.ReadBoolean() ? R.ReadString() : null;
        public float2 F2() => new float2(R.ReadSingle(), R.ReadSingle());
        public int2 I2() => new int2(R.ReadInt32(), R.ReadInt32());

        int Len(int max = 1 << 26)
        {
            int n = R.ReadInt32();
            if (n < -1 || n > max) throw new InvalidDataException($"bad length {n}");
            return n;
        }

        public float[] Floats() { int n = Len(); if (n < 0) return null; var a = new float[n]; for (int i = 0; i < n; i++) a[i] = R.ReadSingle(); return a; }
        public byte[] Bytes() { int n = Len(); if (n < 0) return null; var a = R.ReadBytes(n); if (a.Length != n) throw new EndOfStreamException(); return a; }
        public bool[] Bools() { int n = Len(); if (n < 0) return null; var a = new bool[n]; for (int i = 0; i < n; i++) a[i] = R.ReadBoolean(); return a; }
        public int[] Ints() { int n = Len(); if (n < 0) return null; var a = new int[n]; for (int i = 0; i < n; i++) a[i] = R.ReadInt32(); return a; }
        public List<string> Strs() { int n = Len(); if (n < 0) return null; var a = new List<string>(n); for (int i = 0; i < n; i++) a.Add(Str()); return a; }
        public byte[] Block() => Bytes();

        /// <summary>Reads the public fields <see cref="SaveWriter.Fields"/> wrote into an existing object.</summary>
        public void Fields(object o)
        {
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.FieldType;
                if (t == typeof(int)) f.SetValue(o, Int());
                else if (t == typeof(float)) f.SetValue(o, Float());
                else if (t == typeof(bool)) f.SetValue(o, Bool());
                else if (t == typeof(string)) f.SetValue(o, Str());
                else if (t == typeof(List<string>)) f.SetValue(o, Strs() ?? new List<string>());
                else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                {
                    int n = Len();
                    var list = (System.Collections.IList)Activator.CreateInstance(t);
                    var et = t.GetGenericArguments()[0];
                    for (int i = 0; i < n; i++) { var item = Activator.CreateInstance(et); Fields(item); list.Add(item); }
                    f.SetValue(o, list);
                }
                else throw new InvalidOperationException($"Load: unsupported field {f.Name} ({t})");
            }
        }
    }
}
