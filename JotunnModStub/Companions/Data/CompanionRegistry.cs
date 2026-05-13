using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JotunnModStub.Companions.Diagnostics;

namespace JotunnModStub.Companions.Data
{
    // In-process index keyed by ZDOID. Source of truth for cap checks & valhein_diag.
    // Never persisted — rebuilt from ZDO scan on ZNetScene.Awake postfix.
    internal sealed class InProcessRegistry : ICompanionRegistry
    {
        private readonly ConcurrentDictionary<ZDOID, CompanionHandle> _byId
            = new ConcurrentDictionary<ZDOID, CompanionHandle>();

        public event Action<CompanionHandle> Added;
        public event Action<ZDOID> Removed;
        public event Action<CompanionHandle> Updated;

        public void Add(CompanionHandle handle)
        {
            if (handle == null || handle.ZdoId == ZDOID.None) return;
            _byId[handle.ZdoId] = handle;
            Log.Debug($"registry: add {handle.ZdoId} (now {_byId.Count})");
            Added?.Invoke(handle);
        }

        public void Remove(ZDOID id)
        {
            if (id == ZDOID.None) return;
            if (_byId.TryRemove(id, out var _))
            {
                Log.Debug($"registry: remove {id} (now {_byId.Count})");
                Removed?.Invoke(id);
            }
        }

        public void Clear()
        {
            _byId.Clear();
        }

        public int CountFor(string ownerSteamId, int companionType)
        {
            int n = 0;
            foreach (var h in _byId.Values)
            {
                if (h.CompanionType == companionType && h.OwnerSteamId == ownerSteamId) n++;
            }
            return n;
        }

        public IEnumerable<CompanionHandle> All() => _byId.Values;

        public IEnumerable<CompanionHandle> AllOwnedBy(string ownerSteamId)
        {
            foreach (var h in _byId.Values)
            {
                if (h.OwnerSteamId == ownerSteamId) yield return h;
            }
        }

        public bool TryGet(ZDOID id, out CompanionHandle h) => _byId.TryGetValue(id, out h);

        public void NotifyUpdated(CompanionHandle h)
        {
            if (h == null) return;
            Updated?.Invoke(h);
        }
    }

    // Static façade preserved for existing call sites. Future phases that need
    // a different backing can replace `Instance` once at boot.
    internal static class CompanionRegistry
    {
        public static ICompanionRegistry Instance { get; private set; } = new InProcessRegistry();

        // For tests / Phase 6 server-side swap. Call before any Add/Remove.
        public static void SetInstance(ICompanionRegistry impl)
        {
            if (impl == null) return;
            Instance = impl;
        }

        public static void Add(CompanionHandle h)                       => Instance.Add(h);
        public static void Remove(ZDOID id)                             => Instance.Remove(id);
        public static void Clear()                                      => Instance.Clear();
        public static int  CountFor(string steamId, int companionType)  => Instance.CountFor(steamId, companionType);
        public static IEnumerable<CompanionHandle> All()                => Instance.All();
        public static IEnumerable<CompanionHandle> AllOwnedBy(string s) => Instance.AllOwnedBy(s);
        public static bool TryGet(ZDOID id, out CompanionHandle h)      => Instance.TryGet(id, out h);

        // Convenience for callers that don't want to cast.
        public static void NotifyUpdated(CompanionHandle h)
        {
            if (Instance is InProcessRegistry ip) ip.NotifyUpdated(h);
        }
    }

    // Display-side snapshot of a companion. The registry doesn't tail-poll;
    // UI / diag callers refresh cached fields by reading the ZDO when needed.
    internal sealed class CompanionHandle
    {
        public ZDOID ZdoId;
        public int CompanionType;
        public string OwnerSteamId;
        public string Name;

        // Phase 2 additions — surfaced to roster UI without making the registry chatty.
        public string DisplayName;          // typically == Name; carved out so we can decorate later
        public float CachedHpFraction;      // 0..1 — written by UI tick, not by the registry
        public long  AcquiredAtUnix;        // unix seconds at spawn; preserved across reload via ZDO scan
    }
}
