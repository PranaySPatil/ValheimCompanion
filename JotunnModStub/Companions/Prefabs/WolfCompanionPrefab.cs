using Jotunn.Managers;
using JotunnModStub.Companions.AI;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Lifecycle;
using UnityEngine;

namespace JotunnModStub.Companions.Prefabs
{
    internal static class WolfCompanionPrefab
    {
        public const string PrefabName = "ValheinCompanionWolf";
        public const string ClonedFrom = "Wolf";

        public static GameObject Prefab { get; private set; }

        public static void Register()
        {
            // Unsubscribe so we don't re-register on reload.
            PrefabManager.OnVanillaPrefabsAvailable -= Register;

            var go = PrefabManager.Instance.CreateClonedPrefab(PrefabName, ClonedFrom);
            if (go == null)
            {
                Log.Error($"failed to clone '{ClonedFrom}'; companion wolf disabled");
                return;
            }

            try
            {
                Reconfigure(go);
                PrefabManager.Instance.AddPrefab(go);
                Prefab = go;
                Log.Info($"wolf prefab registered (vanilla '{ClonedFrom}' cloned)");
            }
            catch (System.Exception ex)
            {
                Log.Error($"wolf prefab registration failed: {ex.Message}");
            }
        }

        private static void Reconfigure(GameObject go)
        {
            // Add marker for fast detection.
            if (go.GetComponent<CompanionMarker>() == null)
            {
                var marker = go.AddComponent<CompanionMarker>();
                marker.CompanionType = ZdoKeys.CompanionTypeWolf;
            }

            // Strip Tameable — companion is born tame.
            var tameable = go.GetComponent<Tameable>();
            if (tameable != null) Object.DestroyImmediate(tameable);

            // Strip Procreation — companions don't breed.
            var procreation = go.GetComponent<Procreation>();
            if (procreation != null) Object.DestroyImmediate(procreation);

            // Strip Growup if present (adult-only).
            var growup = go.GetComponent<Growup>();
            if (growup != null) Object.DestroyImmediate(growup);

            // Swap MonsterAI -> WolfCompanionAI by copying configuration.
            var vanilla = go.GetComponent<MonsterAI>();
            if (vanilla != null && !(vanilla is WolfCompanionAI))
            {
                var ai = go.AddComponent<WolfCompanionAI>();
                ai.CopyFrom(vanilla);
                Object.DestroyImmediate(vanilla);
            }

            // Faction Players — friendly to other tames; not targeted by tames or player.
            var character = go.GetComponent<Character>();
            if (character != null)
            {
                character.m_faction = Character.Faction.Players;
            }

            // Attach helpers we want present from the moment ZNetView wakes.
            if (go.GetComponent<LeashController>() == null)
            {
                go.AddComponent<LeashController>();
            }
            if (go.GetComponent<DeathController>() == null)
            {
                go.AddComponent<DeathController>();
            }
        }
    }
}
