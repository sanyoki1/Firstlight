# Firstlight — Game Design Document

Working title: **Firstlight**. Status: planning (2026-11-13).

## Pitch
A low-poly 3D city builder where a single campfire grows into a modern (and later future) city. Nothing advances on a
timer: every new idea, from farming to healthcare to policing, is unlocked by the player through research.

## Core decisions
| Area | Decision |
|---|---|
| Engine | Unity 6 (6000.4.2f1, installed), C#, URP; PC (Windows first). Switched from Godot on 2026-11-13 for late-game scale (thousands to tens of thousands of citizens) |
| Tech approach | Simulation is data-oriented: citizens are plain data in arrays (not GameObjects), updated in staggered ticks via the Jobs system + Burst. Own grid pathfinding with cached flow fields per destination. Crowds and repeated buildings drawn with GPU instancing, LOD, and only near the camera. Full ECS (Entities package) only if a stress test shows it's needed |
| Scale target | Late-game city ~20,000 people, a few thousand visible at once (to confirm) |
| Goal | Commercial release on Steam / itch.io |
| Look | 3D low-poly, neutral and grounded (not cute or cozy): believable proportions, muted natural palette, clean flat-shaded forms. Models made in Blender; angled top-down camera, rotatable |
| Structure | One city through all ages; old buildings get upgraded or replaced |
| Ages | Stone → Agricultural → Bronze/Early Civilization → Classical → Medieval → Industrial → Modern → Future (optional endgame) |
| Research | Research points come from thinkers and knowledge buildings (elders' fire → shaman → scribes → school → university → lab). Education level of citizens speeds research. |
| Unlocks | Only via research, never over time |
| Citizens | Individually simulated (light): job, home, age, a few needs, visible walking; clickable. Grouped into households at large sizes for performance |
| Jobs | Auto-fill with per-building priorities and pause |
| Services | Tiered buildings with coverage radius; higher tiers cover more and work better (e.g. healer's hut → temple of healing → hospital → medical center) |
| Challenges | Disease and disasters; crime and unrest; resource and economy limits (no rival tribes for now) |
| Game mode | Sandbox plus optional milestone goals per age; the city can decline but slowly, with warnings |
| Placement | Grid-based buildings and roads (early huts may be visually offset to look organic) |
| Map | Procedurally generated single region: river, forests, hills, wildlife, stone and ore |
| Seasons | Spring → winter cycle with real effects: crops grow only in warm months, winter needs stored food and firewood, seasonal disasters (spring floods, summer fires) |
| Needs | Layered by age: basics (food, shelter, warmth, health) first; later research adds safety, education, goods, entertainment, faith. Unmet needs lower happiness → slower work, more crime, people leave |
| Research UI | Branching tree in columns by age, nodes linked by prerequisites, research queue, whole path visible |
| Age-up | Requires the age's key research plus a few city milestones (population, key buildings). Marked by a camera pan and new music |
| Start | Small band of 8–12 people, stone tools, a few days of food; player chooses where to place the campfire |
| Population | Births (need housing + food; children grow up over a few years) plus migrants when the city is attractive. Deaths from age, sickness, disasters |
| Construction | Early ages: place a site, builders haul materials and build it up visibly. Later research unlocks construction firms and machinery that build much faster |
| Money | None at first (goods only). Barter → Currency (Bronze Age) unlocks markets, taxes, trade; later banks, credit, service budgets |

## Service progression (draft)
Each line is a chain of research unlocks; each step is a new building tier or capability.

- **Healthcare**: herbal knowledge → healer's hut → temple of healing → physicians / apothecary → hospital → vaccines & sanitation → modern medical center → (future) gene/robotic care
- **Order / policing**: elders' council → tribal law → written laws & courts → city watch → constables → police department → (future) predictive/AI civic systems
- **Education**: storytelling elders → shaman lore → scribes & writing → schools → universities → research labs → (future) AI tutors
- **Fire & disaster**: water buckets → fire watch → fire brigade → fire station → modern emergency services
- **Food**: hunting & gathering → farming → irrigation → crop rotation → mechanized farms → modern agriculture
- **Infrastructure**: footpaths → dirt roads → paved roads → canals/bridges → rail → highways & transit; wells → aqueducts → sewers → water treatment

## First playable slice: Stone → Agricultural
### Stone Age (draft v1)
Known at start: Fire, Stone Tools, Foraging.

**Buildings**
| Building | Does | Needs research |
|---|---|---|
| Campfire | Town center, warmth radius, gathering point | start |
| Lean-to | Houses 4, poor warmth | start |
| Gatherers' Camp | Berries, roots, nuts (seasonal) | start |
| Woodcutter | Wood + firewood | start |
| Stone Gatherer | Stone from loose rock | start |
| Storage Pile | Stores goods; spoils faster in open | start |
| Elders' Fire | First research building (elders produce knowledge) | start |
| Hunters' Camp | Meat + hides from wildlife | Spears |
| Drying Rack | Turns meat/fish into preserved food for winter | Food Preservation |
| Fishing Spot | Fish from river or coast | Fishing |
| Hide Tent | Houses 6, good warmth | Hide Working |
| Hide Worker | Hides → clothing (winter warmth need) | Hide Working |
| Herbalist | Herbs; small health coverage radius | Herbal Knowledge |

**Research tree** (→ means "leads to")
- Stone Tools → Spears → Tracking (hunting yield ↑)
- Stone Tools → Spears → Hide Working (tents, clothing)
- Foraging → Food Preservation; Foraging → Fishing
- Foraging → Herbal Knowledge (herbalist, first health)
- Fire → Storytelling (Elders' Fire research ↑) → Kinship
- Foraging + Herbal Knowledge → Seed Gathering

**Key research:** Farming (needs Seed Gathering) and Tribes (needs Kinship + Storytelling).
**Age-up milestones:** 25 people, survive one winter, have a Storage Pile and an Elders' Fire.

### Agricultural Age (draft v1)
**Buildings:** Fields (seasonal crops), Granary (protects grain from spoiling), Thatched Hut (houses 8; can upgrade),
Animal Pen, Well (water need), Healer's Hut (health tier 1), Shaman's Hut (research tier 2 + faith need), Tribal Council
(enables the first laws; order tier 1), Potter (pottery: storage ↑, goods need), Weaver, Dirt Path (movement ↑).

**Research:** Animal Husbandry, Irrigation, Pottery, Weaving, Healing, Shamanism, Tribal Law (first laws, e.g. food
sharing, dispute settling by elders), Masonry, Barter. **Key research toward the Bronze Age:** Writing + Bronze Working.

**New challenges:** winter food shortages, first disease outbreaks (crowding, bad water), first disputes/theft as the
tribe grows.

Slice must include: map generation, grid placement, citizens with jobs/needs, resource economy, research tree UI,
service coverage (healer), milestone goals, save/load.

## Open questions
- Research costs and balance numbers (tune in prototype)
- Law system design (how laws are chosen and what they cost)
- Monetization and price point; Steam page timing
