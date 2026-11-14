using System;
using Unity.Mathematics;

namespace Firstlight.Game
{
    public enum BuildState : byte { Site, Active, Removed }
    public enum Priority : byte { Low, Normal, High }
    public enum Severity : byte { Info, Warning, Bad }

    /// <summary>What a citizen is visibly doing (for the view and the HUD).</summary>
    public enum Activity : byte { Idle, Resting, Walking, Working, Building, Carrying, Fetching, Sick, Playing, Leaving, Arriving }

    /// <summary>Internal step of a citizen's current errand.</summary>
    enum Task : byte
    {
        None, GoHome, Rest, GoWork, Work, DeliverOutput, FetchInput, DeliverInput,
        FetchMaterial, DeliverMaterial, GoBuild, Build, HaulPickup, Idle, Leave, Arrive,
        GoField, FieldReturn,      // field workers: walk out to a tree / rock / herd, and carry the load back
    }

    /// <summary>An entry in the settlement's event log.</summary>
    public struct SimEvent
    {
        public int Day;
        public Severity Severity;
        public string Text;
        public bool HasPlace;            // where it happened (cell units), for the HUD to jump to
        public float2 Place;
        public override string ToString() => $"day {Day} [{Severity}] {Text}";
    }

    /// <summary>One age milestone and whether it has been reached (latched once done).</summary>
    public sealed class MilestoneState
    {
        public MilestoneDef Def;
        public bool Done;
        public int DoneDay = -1;
        public int Current;          // current value (population, winters, building count)
        public string Label => Def.label;
    }

    /// <summary>
    /// A placed building: a construction site until its materials are delivered and the build work is done,
    /// then active. Holds its own goods: storage (if it is a store), finished goods waiting to be carried
    /// off (Out) and inputs carried in (In), all indexed by resource index.
    /// </summary>
    public sealed class Building
    {
        public int Id;
        public BuildingDef Def;
        public int2 Origin;                  // lower-left footprint cell
        public int Door;                     // walkable cell in front, where people arrive
        public float2 Center, DoorPos;
        public BuildState State;
        public float Work;                   // build work done, 0..1
        public float[] Delivered;            // per cost entry
        internal float[] InTransit;          // per cost entry: on its way in a builder's hands
        public float[] Store;                // storage buildings only (per resource)
        public float Stored;                 // total in Store
        public float[] Out, In;              // per resource
        public float OutTotal;
        internal float HaulReserved;
        public Priority Priority = Priority.Normal;
        public bool Paused;
        public int Focus = -1;               // index into Def.outputs to make only that one (-1 = the normal mix)
        public int Workers, Residents, Builders;
        public bool Heated, Fueled;          // home has firewood today / campfire burns
        public float Fullness = 1f;          // nearby feature fullness 0..1 (1 if none needed)
        public float FeatureLeft;            // nearby feature count (for the HUD)
        internal float FeatureAcc;
        internal int[] CostRes, OutRes;
        internal int[][] InRes;
        internal string YieldKey;
        public string Status = "";           // short status line for the HUD ("No workers", "Needs tools", ...)

        public bool Alive => State != BuildState.Removed;
        public bool Active => State == BuildState.Active;
        public bool IsSite => State == BuildState.Site;
        public bool IsStorage => Def.storageCapacity > 0;

        /// <summary>Share of the cost materials delivered, 0..1 (1 when there is no cost).</summary>
        public float MaterialsFraction
        {
            get
            {
                float need = 0, have = 0;
                for (int i = 0; i < Def.cost.Length; i++) { need += Def.cost[i].amount; have += math.min(Delivered[i], Def.cost[i].amount); }
                return need <= 0 ? 1f : have / need;
            }
        }

        /// <summary>Overall construction progress 0..1 for drawing a site rising: materials count 30%, work 70%.</summary>
        public float Progress => State == BuildState.Site ? MaterialsFraction * 0.3f + Work * 0.7f : 1f;

        /// <summary>Cost entry i on its way to this site in builders' hands.</summary>
        public float InTransitOf(int i) => InTransit[i];

        public bool MaterialsComplete
        {
            get
            {
                for (int i = 0; i < Def.cost.Length; i++) if (Delivered[i] < Def.cost[i].amount - Settlement.MaterialTolerance) return false;
                return true;
            }
        }
    }

    /// <summary>
    /// One person. Light on purpose: a few hundred of these are updated every sim step.
    /// Pos is in cell units (x, map y); Facing is a yaw in radians (0 faces +y, like the Band).
    /// </summary>
    public sealed class Citizen
    {
        public int Id;
        public string Name;
        public bool Female;
        public float Age;                    // years
        public Building Home, Job;
        public bool IsBuilder;
        public float2 Pos;
        public float Facing;
        public Activity Activity;
        public float Health = 1f, Happiness = 0.7f, Warmth = 1f;
        public bool Hungry, Cold;
        public int SickDays;
        public bool Treated;                 // sick and in reach of a herbalist with herbs
        public int Tool = -1;                // resource index of the tool held (-1 = none)
        public float ToolLife;               // working days left on it
        public float ClothingLife;           // days of wear left on clothing (0 = none)
        public int CarryRes = -1;
        public float CarryAmt;
        /// <summary>Field workers (fieldWork jobs) gather into their own load out at the tree / rock / herd and carry it
        /// back to storage when it is full or the shift ends. LoadRes = the main good in it (for the view).</summary>
        public float LoadTotal;
        public int LoadRes = -1;
        internal float[] Load;
        /// <summary>Where a field worker is working: the tree or rock cell, or the herd index for hunters (-1 = none).</summary>
        public int FieldFeature = -1;
        public bool AtField;                 // working out at the feature (not at the building's door)
        public int UnhappyDays;
        public float BirthCooldown;
        public bool Gone;                    // dead or left; removed at the end of the step
        public string LastHarm = "";

        internal Task Task;
        internal Building TaskB, TaskB2;     // where the errand goes (and where it ends, for fetches)
        internal int TaskIdx;                // cost index / resource index for the errand
        internal float TaskAmt;
        internal int Target = -1;            // flow-field target cell (-1 = not walking)
        internal float Stuck;
        internal int ShiftDay = -1;          // day the worker last took its finished goods home

        public bool Carrying => CarryAmt > 0.001f;
        public bool Sick => SickDays > 0;
        public bool Walking => Target >= 0;
    }
}
