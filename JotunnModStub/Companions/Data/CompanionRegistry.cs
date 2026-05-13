using System.Collections.Concurrent;
using System.Collections.Generic;
using JotunnModStub.Companions.Diagnostics;

namespace JotunnModStub.Companions.Data
{
    // In-process index keyed by ZDOID. Source of truth for cap checks & valhein_diag.
    // Never persisted — rebuilt from ZDO scan on ZNetScene.Awake postfix.
    internal static class CompanionRegistry
    {
        private static readonly ConcurrentDictionary<ZDOID, CompanionHandle> _byId
            = new ConcurrentDictionary<ZDOID, CompanionHandle>();

        public static void Add(CompanionHandle handle)
        {
            if (handle == null || handle.ZdoId == ZDOID.None) return;
            _byId[handle.ZdoId] = handle;
            Log.Debug($"registry: add {handle.ZdoId} (now {_byId.Count})");
        }

        public static void Remove(ZDOID id)
        {
            if (id == ZDOID.None) return;
            if (_byId.TryRemove(id, out var _))
            {
                Log.Debug($"registry: remove {id} (now {_byId.Count})");
            }
        }

        public static void Clear()
        {
            _byId.Clear();
        }

        public static int CountFor(string ownerSteamId, int companionType)
        {
            int n = 0;
            foreach (var h in _byId.Values)
            {
                if (h.CompanionType == companionType && h.OwnerSteamId == ownerSteamId) n++;
            }
            return n;
        }

        public static IEnumerable<CompanionHandle> All()
        {
            return _byId.Values;
        }

        public static IEnumerable<CompanionHandle> AllOwnedBy(string ownerSteamId)
        {
            foreach (var h in _byId.Values)
            {
                if (h.OwnerSteamId == ownerSteamId) yield return h;
            }
        }
    }

    internal sealed class CompanionHandle
    {
        public ZDOID ZdoId;
        public int CompanionType;
        public string OwnerSteamId;
        public string Name;
    }
}
