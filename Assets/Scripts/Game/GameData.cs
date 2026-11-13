using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Firstlight.Game
{
    // ------------------------------------------------------------------ data definitions (JSON)
    // Everything content-related lives in StreamingAssets/Data/*.json so new ages, buildings,
    // research and resources can be added without code changes.

    [Serializable] public class Amount { public string id; public float amount; }
    [Serializable] public class InputDef { public string[] ids; public float amount; }
    [Serializable] public class Effect { public string stat; public float value; }

    [Serializable]
    public class ResourceDef
    {
        public string id, name, category, color, note;
        public bool isFood;
        public float spoilPerDay;
    }

    [Serializable]
    public class NearbyDef
    {
        public string feature;   // trees | rocks | water | wildlife
        public int radius;
        public int min;          // need at least this many to build
        public int full;         // this many gives full yield
    }

    [Serializable]
    public class PartDef
    {
        public string prim;
        public float[] pos;
        public float[] size;
        public float yaw;
        public string color;
    }

    [Serializable]
    public class BuildingDef
    {
        public string id, name, age, category, description, unlockedBy, workerTitle, yieldTag, color;
        public int sizeX = 2, sizeY = 2;
        public Amount[] cost = Array.Empty<Amount>();
        public float buildWork;
        public bool unique, townCenter;
        public int maxWorkers;
        public int housing;
        public float homeWarmth;
        public Amount[] outputs = Array.Empty<Amount>();
        public InputDef[] inputs = Array.Empty<InputDef>();
        public float[] seasonYield;
        public NearbyDef nearby;
        public float consumesFeaturePer;   // remove one tree / one herd animal per this many units produced
        public int storageCapacity;
        public float spoilMultiplier = 1f;
        public int warmthRadius;
        public float[] fuelPerDay;
        public int healthRadius;
        public float knowledgePerWorker;
        public PartDef[] parts = Array.Empty<PartDef>();

        public bool HasNearby => nearby != null && !string.IsNullOrEmpty(nearby.feature);
        public bool IsWorkplace => maxWorkers > 0;
        public float SeasonYield(int season) => seasonYield != null && seasonYield.Length == 4 ? seasonYield[season] : 1f;
    }

    [Serializable]
    public class ResearchDef
    {
        public string id, name, age, description;
        public float cost;
        public bool known, key;
        public string[] prereqs = Array.Empty<string>();
        public Effect[] effects = Array.Empty<Effect>();
    }

    [Serializable]
    public class MilestoneDef
    {
        public string type;    // population | wintersSurvived | building
        public string target;
        public int value;
        public string label;
    }

    [Serializable]
    public class AgeDef
    {
        public string id, name, next;
        public int order;
        public string[] keyResearch = Array.Empty<string>();
        public MilestoneDef[] milestones = Array.Empty<MilestoneDef>();
    }

    [Serializable]
    public class Balance
    {
        public int mapSize = 128;
        public float dayLengthSeconds = 24;
        public int daysPerSeason = 8;
        public string[] seasonNames = { "Spring", "Summer", "Autumn", "Winter" };
        public float[] seasonTemperature = { 0.55f, 0.9f, 0.4f, 0.05f };
        public float ageYearsPerGameYear = 4;
        public float workdayStart = 0.22f, workdayEnd = 0.8f;
        public float walkSpeed = 2f;
        public int carryCapacity = 5;
        public float builderWorkPerDay = 1f;
        public int defaultBuilders = 3;

        public int startPeople = 10;
        public Amount[] startResources = Array.Empty<Amount>();

        public float foodPerAdult = 1f, foodPerChild = 0.6f;
        public float[] firewoodPerHome = { 0.3f, 0f, 0.6f, 1.2f };

        public float warmthComfort = 0.6f, unheatedHomeFactor = 0.35f, clothingWarmth = 0.25f, campfireWarmth = 0.2f;
        public float clothingWearDays = 64;
        public float starveDamage = 0.12f, coldDamage = 0.25f, sickDamage = 0.05f, recoverPerDay = 0.05f;
        public float sickBase = 0.002f, sickPerCrowding = 0.002f, sickCold = 0.01f, sickStarving = 0.01f;
        public int crowdingFreeOccupants = 4, sickDaysMin = 4, sickDaysMax = 10;
        public float herbalistChanceMult = 0.5f, herbalistDamageMult = 0.4f, herbsPerSickDay = 0.25f;

        public float adultAge = 14, elderAge = 50, elderKnowledgeMult = 1.5f, fertileMin = 16, fertileMax = 40;
        public float birthChancePerDay = 0.02f, birthMinFoodDays = 5, birthCooldownDays = 32;
        public float oldAgeStart = 55, oldAgeChancePerYearOver = 0.0006f;

        public int migrantMinFreeHousing = 2, migrantsMin = 2, migrantsMax = 4;
        public float migrantMinFoodDays = 8, migrantMinHappiness = 0.5f;
        public float leaveHappiness = 0.3f, leaveAfterDays = 6, workSpeedMin = 0.5f;

        public int treeRegrowPerDay = 3;
        public float rockStone = 30, herdRegrowPerDay = 0.04f;
    }

    [Serializable] class ResourceFile { public ResourceDef[] resources; }
    [Serializable] class BuildingFile { public BuildingDef[] buildings; }
    [Serializable] class ResearchFile { public ResearchDef[] research; }
    [Serializable] class AgeFile { public AgeDef[] ages; }

    /// <summary>All content definitions, loaded once from StreamingAssets/Data.</summary>
    public sealed class GameData
    {
        public readonly List<ResourceDef> Resources = new();
        public readonly List<BuildingDef> Buildings = new();
        public readonly List<ResearchDef> Research = new();
        public readonly List<AgeDef> Ages = new();
        public Balance Balance = new();

        readonly Dictionary<string, ResourceDef> _res = new();
        readonly Dictionary<string, BuildingDef> _bld = new();
        readonly Dictionary<string, ResearchDef> _rsc = new();
        readonly Dictionary<string, AgeDef> _age = new();

        public ResourceDef Resource(string id) => _res.TryGetValue(id, out var r) ? r : null;
        public BuildingDef Building(string id) => _bld.TryGetValue(id, out var b) ? b : null;
        public ResearchDef ResearchById(string id) => _rsc.TryGetValue(id, out var r) ? r : null;
        public AgeDef Age(string id) => _age.TryGetValue(id, out var a) ? a : null;

        public static GameData Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            var d = new GameData();
            d.Resources.AddRange(Read<ResourceFile>(dir, "resources.json").resources);
            d.Buildings.AddRange(Read<BuildingFile>(dir, "buildings.json").buildings);
            d.Research.AddRange(Read<ResearchFile>(dir, "research.json").research);
            d.Ages.AddRange(Read<AgeFile>(dir, "ages.json").ages);
            d.Balance = Read<Balance>(dir, "balance.json");
            foreach (var r in d.Resources) d._res[r.id] = r;
            foreach (var b in d.Buildings) d._bld[b.id] = b;
            foreach (var r in d.Research) d._rsc[r.id] = r;
            foreach (var a in d.Ages) d._age[a.id] = a;
            d.Ages.Sort((a, b) => a.order.CompareTo(b.order));
            d.Validate();
            return d;
        }

        static T Read<T>(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }

        void Validate()
        {
            // JsonUtility may leave missing fields at zero/null, so normalise defaults here.
            foreach (var b in Buildings)
            {
                b.cost ??= Array.Empty<Amount>();
                b.outputs ??= Array.Empty<Amount>();
                b.inputs ??= Array.Empty<InputDef>();
                b.parts ??= Array.Empty<PartDef>();
                if (b.sizeX <= 0) b.sizeX = 2;
                if (b.sizeY <= 0) b.sizeY = 2;
                if (b.spoilMultiplier <= 0) b.spoilMultiplier = 1f;
            }
            foreach (var r in Research) { r.prereqs ??= Array.Empty<string>(); r.effects ??= Array.Empty<Effect>(); }
            foreach (var a in Ages) { a.keyResearch ??= Array.Empty<string>(); a.milestones ??= Array.Empty<MilestoneDef>(); }
            foreach (var b in Buildings)
            {
                foreach (var c in b.cost) if (Resource(c.id) == null) Debug.LogError($"[Data] {b.id}: unknown cost resource {c.id}");
                foreach (var o in b.outputs) if (Resource(o.id) == null) Debug.LogError($"[Data] {b.id}: unknown output {o.id}");
                foreach (var i in b.inputs) foreach (var id in i.ids) if (Resource(id) == null) Debug.LogError($"[Data] {b.id}: unknown input {id}");
                if (!string.IsNullOrEmpty(b.unlockedBy) && ResearchById(b.unlockedBy) == null) Debug.LogError($"[Data] {b.id}: unknown research {b.unlockedBy}");
            }
            foreach (var r in Research)
                foreach (var p in r.prereqs) if (ResearchById(p) == null) Debug.LogError($"[Data] research {r.id}: unknown prereq {p}");
            foreach (var a in Ages)
                foreach (var k in a.keyResearch) if (ResearchById(k) == null) Debug.LogError($"[Data] age {a.id}: unknown key research {k}");
        }

        public int AgeOrder(string ageId) => Age(ageId)?.order ?? 99;
    }
}
