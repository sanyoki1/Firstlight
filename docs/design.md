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
Known at start: Fire, Foraging. Tools are **not** known: Basic Tools is the first research (decided 2026-11-13).

**Tools are a physical good** (decided 2026-11-13). The Toolmaker turns wood + stone into stone tools; workers in
tool-using jobs (woodcutter, stone gatherer, hunter, hide worker, miner, farmer, builders) wear them out
(`toolWearDays`). Without a tool they work at 60% speed (`noToolWorkSpeed`); mines, pits and fields can't work at all
without one. Better tiers work faster: stone ×1.0, copper ×1.15, bronze ×1.3 (best tier in stock is handed out first).

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
| Toolmaker | Wood + stone → stone tools | Basic Tools |
| Mining Pit | Steady stone from rocky ground; never runs out; needs tools | Basic Mining |

The Woodcutter and Stone Gatherer stay available from the start (deadfall and loose rock, at the slow no-tool speed),
so the band can always reach the Elders' Fire and research Basic Tools.

**Research tree** (→ means "leads to")
- Basic Tools → Spears → Tracking (hunting yield ↑)
- Basic Tools → Spears → Hide Working (tents, clothing)
- Basic Tools → Basic Mining (Mining Pit) → Flint Tools (tools last 40% longer)
- Foraging → Food Preservation; Foraging → Fishing
- Foraging → Herbal Knowledge (herbalist, first health)
- Fire → Storytelling (Elders' Fire research ↑) → Kinship
- Foraging + Herbal Knowledge → Seed Gathering

**Key research:** Farming (needs Seed Gathering + Basic Tools) and Tribes (needs Kinship + Storytelling).
**Age-up milestones:** 25 people, survive one winter, have a Storage Pile and an Elders' Fire.

### Agricultural Age (draft v1)
**Buildings:** Fields (seasonal crops), Granary (protects grain from spoiling), Thatched Hut (houses 8; can upgrade),
Animal Pen, Well (water need), Healer's Hut (health tier 1), Shaman's Hut (research tier 2 + faith need), Tribal Council
(enables the first laws; order tier 1), Potter (pottery: storage ↑, goods need), Weaver, Dirt Path (movement ↑).

**Research:** Animal Husbandry, Irrigation, Pottery, Weaving, Healing, Shamanism, Tribal Law (first laws, e.g. food
sharing, dispute settling by elders), Masonry (needs Basic Mining + Farming), Barter. **Key research toward the Bronze
Age:** Writing + Bronze Working.

**Metal chain** (decided 2026-11-13). Mining is optional in the Stone Age but required before Bronze:
- Basic Mining → Prospecting: reveals the map's hidden ore (3 copper patches in the hills, 1 small tin patch far from
  the river, so bronze forces the city to expand). Clay beds along the river banks are visible from the start.
- Prospecting + Pottery → Copper Working: Copper Mine (on a copper deposit) → Smelter (ore + firewood → copper) →
  Coppersmith (copper + wood → copper tools). Pottery also unlocks the Clay Pit; the Smelter is built with clay.
- Copper Working + Barter → Bronze Working (key): Tin Mine (on the tin deposit) → Bronze Foundry (copper + tin +
  firewood → bronze) → Bronze Smith (bronze tools).

**Age-up milestones:** 80 people, a Mining Pit, a Smelter and a Granary (decided in the quarter-build experiment: the
Granary joined the list once it existed).

**New challenges:** winter food shortages, first disease outbreaks (crowding, bad water), first disputes/theft as the
tribe grows.

#### Settled in the quarter-build experiment (2026-11-14)
These were left open by draft v1 and were decided while building the playable slice (numbers in the data files;
`docs/quarter-build-log.md`, Phase 3, has the reasons):
- **Fields** follow the year: sown in spring (sowing work), green through summer (tended crops grow faster; untended
  grow at 65%), harvested in autumn into grain (or flax fibre once Weaving is known: the crop is chosen per field and
  fixed at sowing), nothing in winter, when the farmers go to other work. Farmers work inside the field and carry grain
  in sacks (three times a normal load). Irrigation: +35% for fields with the river within 10 cells.
- **Granary**: food only, food in it spoils at 30% of the open rate; haulers move food from open piles into it.
- **Store settings (2026-11-15, RimWorld-style)**: every store has a per-good filter (within what the building can
  hold), a priority (Low / Normal / High), and per good keep at least / keep at most. Keep-at-least is a reserve:
  filled from other stores, used only once every other store is out, never traded. A **Haulers** count (bottom bar,
  default 1, after high-priority jobs and builders and never taken by them, and like builders kept only while there is
  such work: a store's settings changed from as built) does the moving between stores. Meant to
  carry into later ages (warehouses keeping a set stock of goods). A store left as built behaves exactly as before.
- **Housing upgrades in place**: Lean-to and Hide Tent → Thatched Hut (8, warm) → Stone-footed Hut (10, warmest;
  Masonry). The old building stays lived in while builders rebuild it.
- **Needs arrive with the age**: clean water (from the Agricultural Age: homes near the river or a well), pottery (goods,
  once Pottery is known: one per household, wears out) and faith (once Shamanism is known: homes in a staffed Shaman's
  Hut's reach). An unmet need caps happiness (water 0.6, pottery 0.7, faith 0.65) instead of being a survival need.
- **Health tiers**: Herbalist (radius 16) and Healer's Hut (radius 24, better treatment, holds back outbreaks).
- **Laws** (Tribal Law + a Tribal Council): four toggles with real trade-offs, each changeable once every 4 days and
  suspended without a council: Food Sharing, Elders Settle Disputes, Shared Labour, Rationing (`laws.json`).
- **Unrest** (from the Agricultural Age): grows with population, unhappiness, crowding and people without faith; a
  staffed council halves it. Above 25% disputes cost work days, above 30% goods go missing; warned at 20%.
- **Outbreaks** (from 30 people): crowding and homes without clean water raise the daily chance; several people in
  the homes round the worst home fall ill at once; a healer in reach (with herbs) limits it.
- **Barter**: trading parties call in spring, summer and autumn and swap the biggest surplus for what the settlement is
  short of (tools, clothing, pottery, herbs, tin once Bronze Working is known).
- **Masonry**: stone-footed huts and stone-lined wells (reach +50%). **Writing**: knowledge +15%. **Tribes**: migrants
  +30%.
- **Bronze buildings** (Tin Mine, Bronze Foundry, Bronze Smith) belong to the Agricultural Age, since Bronze Working is
  researched there. Mines and the clay pit must stand on their deposit; ore shows on the map after Prospecting.
- **Dirt paths**: free, immediate, dragged out tile by tile; walking on them is 35% faster and routes bend onto them.
- **End of the slice**: reaching the Bronze Age shows an end screen with a summary and "Keep playing" (sandbox).

Slice must include: map generation, grid placement, citizens with jobs/needs, resource economy, research tree UI,
service coverage (healer), milestone goals, save/load.

## Open questions
- Research costs and balance numbers (tune in prototype)
- Law system design beyond the first four toggles (how later laws are chosen and what they cost); the first laws were
  decided in the quarter-build experiment (see the Agricultural Age)
- Monetization and price point; Steam page timing
