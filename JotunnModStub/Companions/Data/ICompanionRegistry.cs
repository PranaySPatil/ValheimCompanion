using System;
using System.Collections.Generic;

namespace JotunnModStub.Companions.Data
{
    // Stable contract for the companion registry. Phase 1 ships a single in-process
    // implementation; Phase 6 multiplayer can swap in a server-backed implementation
    // without churning call sites.
    internal interface ICompanionRegistry
    {
        void Add(CompanionHandle h);
        void Remove(ZDOID id);
        void Clear();

        int  CountFor(string ownerSteamId, int companionType);
        IEnumerable<CompanionHandle> AllOwnedBy(string ownerSteamId);
        IEnumerable<CompanionHandle> All();
        bool TryGet(ZDOID id, out CompanionHandle h);

        // Fires after Add (whether the entry was new or replaced).
        event Action<CompanionHandle> Added;
        // Fires after Remove succeeds. Does not fire on Clear.
        event Action<ZDOID> Removed;
        // Fires when a known handle's display state (name, hp fraction, …) changes.
        // The handle reference is the same instance as in All() / AllOwnedBy().
        event Action<CompanionHandle> Updated;
    }
}
