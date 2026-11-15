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
        public float toolSpeed;   // > 0 marks a tool; work speed multiplier for a worker holding one (best tier in stock is used)
        public float value = 1;   // worth in a swap with traders (Barter)
        public bool traded;       // traders can bring it
        public float carry = 1;   // load size multiplier (grain goes in sacks: more per trip)
        public string unlockedBy; // a field crop that needs this research before it can be sown
    }

    /// <summary>One growth stage of a crop building's look: the crop rows' material and height (view only).</summary>
    [Serializable] public class CropStageDef { public string name, material; public float height; public string[] materials; }

    /// <summary>A law the council can pass (laws.json): its effects join the research effects while it is in force.</summary>
    [Serializable]
    public class LawDef
    {
        public string id, name, description, research;
        public Effect[] effects = Array.Empty<Effect>();
    }

    /// <summary>A need that starts counting at an age or with a research topic (balance.json "needs").</summary>
    [Serializable]
    public class NeedDef
    {
        public string id, name, age, research;
        public float unmet = 0.6f;   // happiness from this need while it is not met (1 = met)
    }

    [Serializable]
    public class NearbyDef
    {
        public string feature;   // trees | rocks | water | wildlife | clay | copper | tin | open (free grass, for pasture)
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
        public string color;       // optional tint over the material (sRGB hex); white if empty
        public string material;    // material layer (MaterialLibrary), e.g. "wood", "branches", "hide"
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
        public bool usesTools;       // workers wear tools down; without one they work at Balance.noToolWorkSpeed
        public bool requiresTools;   // no work at all without a tool (implies usesTools)
        public bool fieldWork;       // workers walk out to the nearby trees / rocks / herd and carry their load back
        public string workPose;      // how workers look while working (view): bend, chop, dig, stalk, sit, stand
        public PartDef[] parts = Array.Empty<PartDef>();

        // Agricultural Age mechanics (all optional).
        public string upgradesTo;        // can be rebuilt in place as this building (same footprint); residents stay meanwhile
        public string[] accepts;         // stores: only these resource categories (empty = everything)
        public string onDeposit;         // must stand on this deposit (clay / copper / tin): at least depositMin footprint cells
        public int depositMin = 1;
        public bool path;                // a 1-cell path tile: placed by dragging, walked on, faster to walk
        public bool crop;                // a field: sown in spring, grows through summer, harvested in autumn
        public bool exclusiveOutputs;    // outputs are alternatives (a crop choice), never a mix
        public float sowWork = 4, harvestWork = 6, tendWork = 1;   // worker-days per field; tending per day in summer
        public int irrigationRadius;     // with Irrigation known, water within this radius raises the harvest
        public CropStageDef[] cropStages;
        public int waterRadius;          // a well: homes within it have clean water
        public int faithRadius;          // homes within it have their faith need met (while staffed)
        public float healChance = 0.5f, healDamage = 0.4f;   // healers: sickness chance and harm multipliers in reach
        public float orderPower;         // a council: unrest x (1 - orderPower) while staffed
        public bool enablesLaws;

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

    /// <summary>A difficulty choice for a new game (balance.json "difficulties"): multipliers on the standard balance.</summary>
    [Serializable]
    public class DifficultyDef
    {
        public string id = "standard", name = "Standard", description = "";
        public float startGoods = 1f;          // starting goods at the campfire
        public float winterTemperature;        // added to winter's temperature (+ milder, - colder)
        public float firewood = 1f;            // firewood burned per home
        public float yield = 1f;               // what workplaces and fields make
        public static readonly DifficultyDef Standard = new();
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
        public float commuteLead = 0.1f;               // share of a day before the shift that workers leave home
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

        public float noToolWorkSpeed = 0.6f, toolWearDays = 40;
        public bool buildersUseTools = true;
        public int copperDeposits = 3, tinDeposits = 1;

        // Settlement simulation (Sim/Settlement*.cs).
        public int startChildren = 2, startElders = 2;
        public float migrantCheckDays = 4, migrantChance = 0.5f;
        public float foodWarnDays = 3;
        public float workHaulMin = 1f;                  // haulers pick up a workplace's goods once this much has piled up
        public float demolishRefund = 0.5f;             // share of the cost returned when a finished building is pulled down
        public float focusShare = 0.8f;                 // a focused workplace's chosen output gains this share of the other outputs' amounts
        public float homelessShelter = 0.3f;            // happiness from shelter when sleeping in the open
        public float happinessSmoothing = 0.35f;        // how fast happiness follows the needs each day
        public float happinessWorstWeight = 0.6f;       // share of happiness set by the worst-met need (the rest by the average)
        public string fuelResource = "firewood", clothingResource = "clothing", herbsResource = "herbs";

        // Agricultural Age.
        public NeedDef[] needs = Array.Empty<NeedDef>();
        public string goodsResource = "pottery";
        public float goodsWearDays = 48;
        public int riverWaterRadius = 7;            // homes this close to the river have water without a well
        public float noWaterSick = 0.004f, noWaterWorkSpeed = 0.9f;
        public float growthSpring = 0.07f, growthSummer = 0.09f, untendedGrowth = 0.65f;
        public string planFoodAge = "agricultural";  // from this age births and migrants look ahead to the next harvest
        public float migrantWinterShare = 0.8f;     // in autumn and winter, migrants want food for this share of the days to summer
        public float birthWinterShare = 0.5f;
        public string unrestAge = "agricultural";   // unrest counts from this age (a band of a dozen has no factions)
        public float unrestPopScale = 400, unrestUnhappy = 0.8f, unrestCrowding = 0.5f, unrestFaithless = 0.15f, unrestSmoothing = 0.15f;
        public float unrestWarn = 0.2f, disputeAt = 0.25f, theftAt = 0.3f, disputeChance = 2f, theftChance = 1.5f;
        public float outbreakBase = 0.003f, outbreakMinPop = 30, outbreakWarn = 0.015f, outbreakRadius = 6, outbreakCatch = 0.55f;
        public int traderValue = 40;
        public float traderRate = 0.75f;
        public int lawCooldownDays = 4;
        public float pathSpeed = 1.35f;             // walking speed on a path tile
        public int famineLeaveDays = 3;             // hungry this many days in a row: may leave to find food elsewhere
        public float famineLeaveChance = 0.2f;
        public string[] namesFemale = Array.Empty<string>(), namesMale = Array.Empty<string>();

        // Phase 4: spring floods and difficulty.
        public float floodChance = 0.4f;            // chance each spring (from floodFromYear) that the river floods
        public int floodFromYear = 1, floodDays = 2;
        public float floodStrengthMin = 0.3f, floodStrengthMax = 0.55f;   // rise of the river (world units)
        public float floodLoss = 0.15f, floodLossSpoiling = 0.4f;          // share of stored goods spoiled (goods that rot lose more)
        public float floodFertility = 0.25f;        // flooded fields: harvest bonus that year
        public DifficultyDef[] difficulties = Array.Empty<DifficultyDef>();
    }

    [Serializable] class ResourceFile { public ResourceDef[] resources; }
    [Serializable] class BuildingFile { public BuildingDef[] buildings; }
    [Serializable] class ResearchFile { public ResearchDef[] research; }
    [Serializable] class AgeFile { public AgeDef[] ages; }
    [Serializable] class LawFile { public LawDef[] laws; }

    /// <summary>All content definitions, loaded once from StreamingAssets/Data.</summary>
    public sealed class GameData
    {
        public readonly List<ResourceDef> Resources = new();
        public readonly List<BuildingDef> Buildings = new();
        public readonly List<ResearchDef> Research = new();
        public readonly List<AgeDef> Ages = new();
        public readonly List<LawDef> Laws = new();
        public Balance Balance = new();

        readonly Dictionary<string, ResourceDef> _res = new();
        readonly Dictionary<string, BuildingDef> _bld = new();
        readonly Dictionary<string, ResearchDef> _rsc = new();
        readonly Dictionary<string, AgeDef> _age = new();

        public ResourceDef Resource(string id) => _res.TryGetValue(id, out var r) ? r : null;
        public BuildingDef Building(string id) => _bld.TryGetValue(id, out var b) ? b : null;
        public ResearchDef ResearchById(string id) => _rsc.TryGetValue(id, out var r) ? r : null;
        public AgeDef Age(string id) => _age.TryGetValue(id, out var a) ? a : null;

        /// <summary>
        /// Problems found in the data after loading (unknown or duplicate ids, unknown ages, season arrays of the wrong
        /// length...). The data still loads; the game shows them on an error screen and the tests treat them as fatal.
        /// </summary>
        public readonly List<string> Problems = new();

        /// <summary>A data file that could not be read or parsed (the game cannot start without it).</summary>
        public sealed class LoadException : Exception
        {
            public readonly string File;
            public LoadException(string file, string message) : base($"{file}: {message}") { File = file; }
        }

        /// <summary>Reads every data file. Throws <see cref="LoadException"/> (naming the file) if one is missing or not valid JSON.</summary>
        public static GameData Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            var d = new GameData();
            d.Resources.AddRange(Need(Read<ResourceFile>(dir, "resources.json").resources, "resources.json", "resources"));
            d.Buildings.AddRange(Need(Read<BuildingFile>(dir, "buildings.json").buildings, "buildings.json", "buildings"));
            d.Research.AddRange(Need(Read<ResearchFile>(dir, "research.json").research, "research.json", "research"));
            d.Ages.AddRange(Need(Read<AgeFile>(dir, "ages.json").ages, "ages.json", "ages"));
            d.Balance = Read<Balance>(dir, "balance.json");
            if (File.Exists(Path.Combine(dir, "laws.json"))) d.Laws.AddRange(Read<LawFile>(dir, "laws.json").laws ?? Array.Empty<LawDef>());
            d.Resources.RemoveAll(x => x == null); d.Buildings.RemoveAll(x => x == null); d.Research.RemoveAll(x => x == null);
            d.Ages.RemoveAll(x => x == null); d.Laws.RemoveAll(x => x == null);
            d.Index(d.Resources, d._res, r => r.id, "resource");
            d.Index(d.Buildings, d._bld, b => b.id, "building");
            d.Index(d.Research, d._rsc, r => r.id, "research topic");
            d.Index(d.Ages, d._age, a => a.id, "age");
            d.Index(d.Laws, new Dictionary<string, LawDef>(), l => l.id, "law");
            d.Ages.Sort((a, b) => a.order.CompareTo(b.order));
            d.Validate();
            return d;
        }

        static T[] Need<T>(T[] items, string file, string field) => items ?? throw new LoadException(file, $"no \"{field}\" list");

        /// <summary>Fills a lookup; a missing or repeated id is a problem (the later entry would silently replace the earlier).</summary>
        void Index<T>(List<T> items, Dictionary<string, T> lookup, Func<T, string> id, string what)
        {
            for (int i = 0; i < items.Count; i++)
            {
                string key = id(items[i]);
                if (string.IsNullOrEmpty(key)) { Problem($"{what} #{i + 1} has no id"); continue; }
                if (lookup.ContainsKey(key)) Problem($"duplicate {what} id \"{key}\"");
                lookup[key] = items[i];
            }
        }

        static T Read<T>(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            T data;
            try { data = JsonUtility.FromJson<T>(File.ReadAllText(path)); }
            catch (Exception e) { throw new LoadException(file, e.Message); }
            if (data == null) throw new LoadException(file, "the file is empty");
            return data;
        }

        void Problem(string text)
        {
            Problems.Add(text);
            Debug.LogError($"[Data] {text}");
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
                if (b.requiresTools) b.usesTools = true;
                b.accepts ??= Array.Empty<string>();
            }
            foreach (var l in Laws) l.effects ??= Array.Empty<Effect>();
            Balance.needs ??= Array.Empty<NeedDef>();
            foreach (var r in Research) { r.prereqs ??= Array.Empty<string>(); r.effects ??= Array.Empty<Effect>(); }
            foreach (var a in Ages) { a.keyResearch ??= Array.Empty<string>(); a.milestones ??= Array.Empty<MilestoneDef>(); }

            // Every problem is collected (and logged); none stops the loading here.
            void Res(string owner, string what, string id) { if (string.IsNullOrEmpty(id) || Resource(id) == null) Problem($"{owner}: unknown {what} \"{id}\""); }
            void AgeId(string owner, string id) { if (string.IsNullOrEmpty(id) || Age(id) == null) Problem($"{owner}: unknown age \"{id}\""); }
            void Seasons(string owner, string what, float[] arr, bool optional)
            {
                if ((arr == null && optional) || arr?.Length == 4) return;
                Problem($"{owner}: {what} needs 4 entries (spring, summer, autumn, winter), has {arr?.Length ?? 0}");
            }

            foreach (var b in Buildings)
            {
                string o = $"building {b.id}";
                AgeId(o, b.age);
                foreach (var c in b.cost) Res(o, "cost resource", c?.id);
                foreach (var x in b.outputs) Res(o, "output", x?.id);
                foreach (var i in b.inputs) foreach (var id in i?.ids ?? Array.Empty<string>()) Res(o, "input", id);
                if (!string.IsNullOrEmpty(b.unlockedBy) && ResearchById(b.unlockedBy) == null) Problem($"{o}: unknown research \"{b.unlockedBy}\"");
                if (!string.IsNullOrEmpty(b.upgradesTo))
                {
                    var t = Building(b.upgradesTo);
                    if (t == null) Problem($"{o}: unknown upgrade \"{b.upgradesTo}\"");
                    else if (t.sizeX != b.sizeX || t.sizeY != b.sizeY) Problem($"{o}: upgrade {b.upgradesTo} must have the same footprint");
                }
                Seasons(o, "seasonYield", b.seasonYield, true);
                Seasons(o, "fuelPerDay", b.fuelPerDay, true);
            }
            foreach (var r in Resources)
                if (!string.IsNullOrEmpty(r.unlockedBy) && ResearchById(r.unlockedBy) == null) Problem($"resource {r.id}: unknown research \"{r.unlockedBy}\"");
            foreach (var l in Laws) if (!string.IsNullOrEmpty(l.research) && ResearchById(l.research) == null) Problem($"law {l.id}: unknown research \"{l.research}\"");
            foreach (var r in Research)
            {
                AgeId($"research {r.id}", r.age);
                foreach (var p in r.prereqs) if (ResearchById(p) == null) Problem($"research {r.id}: unknown prereq \"{p}\"");
            }
            foreach (var a in Ages)
            {
                if (!string.IsNullOrEmpty(a.next) && Age(a.next) == null) Problem($"age {a.id}: unknown next age \"{a.next}\"");
                foreach (var k in a.keyResearch) if (ResearchById(k) == null) Problem($"age {a.id}: unknown key research \"{k}\"");
                foreach (var m in a.milestones)
                    if (m.type == "building" && Building(m.target) == null) Problem($"age {a.id}: unknown milestone building \"{m.target}\"");
            }

            // Balance: the goods the settlement looks up by id, the season arrays it indexes by season, the ages it names.
            var B = Balance;
            Res("balance", "fuelResource", B.fuelResource);
            Res("balance", "clothingResource", B.clothingResource);
            Res("balance", "herbsResource", B.herbsResource);
            Res("balance", "goodsResource", B.goodsResource);
            foreach (var a in B.startResources ?? Array.Empty<Amount>()) Res("balance startResources", "resource", a?.id);
            if (B.seasonNames?.Length != 4) Problem($"balance: seasonNames needs 4 entries (spring, summer, autumn, winter), has {B.seasonNames?.Length ?? 0}");
            Seasons("balance", "seasonTemperature", B.seasonTemperature, false);
            Seasons("balance", "firewoodPerHome", B.firewoodPerHome, false);
            if (!string.IsNullOrEmpty(B.planFoodAge)) AgeId("balance planFoodAge", B.planFoodAge);
            if (!string.IsNullOrEmpty(B.unrestAge)) AgeId("balance unrestAge", B.unrestAge);
            foreach (var n in B.needs)
            {
                if (n == null) continue;
                if (!string.IsNullOrEmpty(n.age)) AgeId($"need {n.id}", n.age);
                if (!string.IsNullOrEmpty(n.research) && ResearchById(n.research) == null) Problem($"need {n.id}: unknown research \"{n.research}\"");
            }
        }

        public int AgeOrder(string ageId) => Age(ageId)?.order ?? 99;
    }
}
