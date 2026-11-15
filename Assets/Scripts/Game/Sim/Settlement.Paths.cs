using System.Collections.Generic;

namespace Firstlight.Game
{
    // Path upkeep (pass 2, batch 2). A flow field is a pure function of the committed walking prices and its target
    // (GameMap.LiveCost / FlowFieldCache.Cost), so building fields earlier, or several at once in one parallel batch,
    // never changes a route: it only stops the main thread from building them one at a time (about 1.35 ms each) when
    // people next set off.
    public sealed partial class Settlement
    {
        int _doorsPrefetchedFor = -1;               // FlowFieldCache.Generation the doors were last prefetched for
        readonly List<int> _walkMisses = new();
        readonly HashSet<int> _walkMissSet = new();

        /// <summary>
        /// The game loop calls this every frame, after the player's commands and before the settlement steps.
        /// <paramref name="commitPending"/>: also commit pending blocking changes now. The game passes it only while the
        /// settlement is not stepping; while it steps, the hourly step commits them (so the water's flips during a flood
        /// drop the cached paths at most once a game hour), and the commands (placing, demolishing, paths) commit their own.
        /// Whenever the cached paths were dropped since the last call (by any commit, a load included), the paths to every
        /// door are rebuilt at once in one parallel batch.
        /// </summary>
        public void CommitWalkCosts(bool commitPending)
        {
            if (commitPending) Map.CommitCostChanges();
            if (Map.Flow.Generation == _doorsPrefetchedFor) return;
            _doorsPrefetchedFor = Map.Flow.Generation;
            PrefetchPaths();
        }

        /// <summary>
        /// Called once per step before anyone moves: the targets of everyone walking whose path is not cached are built
        /// together in one parallel batch, so a miss is never built alone on the main thread (a single miss is left to
        /// Flow.Resolve, which builds it the same way).
        /// </summary>
        void PrefetchWalkers()
        {
            _walkMisses.Clear();
            _walkMissSet.Clear();
            for (int i = 0; i < Citizens.Count; i++) if (!Citizens[i].Gone) AddWalkMiss(Citizens[i]);
            for (int i = 0; i < Departing.Count; i++) AddWalkMiss(Departing[i]);
            if (_walkMisses.Count > 1) Map.Flow.Prefetch(_walkMisses);
        }

        void AddWalkMiss(Citizen c)
        {
            int t = c.Target;
            if (t < 0 || _walkMisses.Count >= Map.Flow.Capacity / 2 || Map.Flow.Has(t) || Map.CellAt(c.Pos) == t) return;
            if (_walkMissSet.Add(t)) _walkMisses.Add(t);
        }
    }
}
