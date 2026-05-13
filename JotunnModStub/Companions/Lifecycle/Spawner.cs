using System;
using JotunnModStub.Companions.AI;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Identity;
using JotunnModStub.Companions.Prefabs;
using UnityEngine;

namespace JotunnModStub.Companions.Lifecycle
{
    // Single entry point for all three acquisition paths.
    internal static class Spawner
    {
        public static GameObject SpawnWolfFor(Player player, string name, AcquisitionKind acquisition, Vector3 originPos, int tameLevel = 0)
        {
            if (player == null)
            {
                Log.Error("SpawnWolfFor called with null player");
                return null;
            }

            string steamId = OwnerIdentity.GetSteamIdOf(player);
            if (string.IsNullOrEmpty(steamId))
            {
                Log.Error("could not resolve owner SteamID; spawn aborted");
                Announce("Could not resolve your SteamID — spawn aborted.");
                return null;
            }

            int cap = CompanionConfig.CombatMaxPerPlayer?.Value ?? 1;
            int current = CompanionRegistry.CountFor(steamId, ZdoKeys.CompanionTypeWolf);
            if (current >= cap)
            {
                AnnounceLoc("$valhein.cap.combat_full", current, cap);
                return null;
            }

            var prefab = WolfCompanionPrefab.Prefab;
            if (prefab == null && ZNetScene.instance != null)
            {
                prefab = ZNetScene.instance.GetPrefab(WolfCompanionPrefab.PrefabName);
            }
            if (prefab == null)
            {
                Log.Error($"prefab '{WolfCompanionPrefab.PrefabName}' not found");
                return null;
            }

            if (string.IsNullOrEmpty(name))
            {
                int seed = NameGenerator.Hash(steamId, DateTime.UtcNow.Ticks);
                name = NameGenerator.Next(seed);
            }

            var go = UnityEngine.Object.Instantiate(prefab, originPos, Quaternion.identity);
            if (go == null)
            {
                Log.Error("Object.Instantiate returned null");
                return null;
            }

            var view = go.GetComponent<ZNetView>();
            var zdo = view != null ? view.GetZDO() : null;
            if (zdo == null)
            {
                Log.Error("spawned wolf has no ZDO — destroying");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            // Hot fields (§3.1)
            zdo.Set(ZdoKeys.Companion,       ZdoKeys.CompanionTypeWolf);
            zdo.Set(ZdoKeys.OwnerSteamId,    steamId);
            zdo.Set(ZdoKeys.OwnerNameCached, player.GetPlayerName() ?? "");
            zdo.Set(ZdoKeys.Name,            name);
            zdo.Set(ZdoKeys.Order,           (int)CompanionOrder.Follow);
            zdo.Set(ZdoKeys.DeathState,      (int)CompanionDeathState.Alive);
            zdo.Set(ZdoKeys.SchemaVer,       ZdoKeys.CurrentSchemaVer);

            // Cold blob
            var state = new CompanionState
            {
                Acquisition = acquisition,
                TameLevel = tameLevel
            };
            AuditLog.Append(state, "Spawned", $"via={acquisition} name={name}");
            zdo.Set(ZdoKeys.Cold, CompanionStateCodec.Encode(state));

            // Combat baseline (§5.2 step 7)
            var humanoid = go.GetComponent<Humanoid>();
            var character = go.GetComponent<Character>();
            float hpBase = CompanionConfig.WolfHealthBase?.Value ?? 200f;
            if (humanoid != null)
            {
                humanoid.SetMaxHealth(hpBase);
                humanoid.SetHealth(hpBase);
            }
            else if (character != null)
            {
                character.SetMaxHealth(hpBase);
                character.SetHealth(hpBase);
            }

            // Star level. Tame-promotion preserves the source wolf's level (Q1.4);
            // other acquisition paths use the configured default. Valheim level = stars + 1.
            if (character != null)
            {
                int level = tameLevel;
                if (level <= 0)
                {
                    int stars = CompanionConfig.WolfDefaultStarLevel?.Value ?? 0;
                    if (stars < 0) stars = 0;
                    if (stars > 3) stars = 3;
                    level = stars + 1;
                }
                if (level > 1)
                {
                    character.SetLevel(level);
                }
            }

            // Register
            var handle = new CompanionHandle
            {
                ZdoId = zdo.m_uid,
                CompanionType = ZdoKeys.CompanionTypeWolf,
                OwnerSteamId = steamId,
                Name = name
            };
            CompanionRegistry.Add(handle);

            AnnounceLoc("$valhein.spawn.welcome", name);
            Log.Info($"spawned wolf '{name}' for {steamId} via {acquisition}");
            return go;
        }

        private static void Announce(string text)
        {
            try
            {
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
            }
            catch { }
        }

        private static void AnnounceLoc(string key, params object[] args)
        {
            try
            {
                if (MessageHud.instance == null) return;
                var loc = Localization.instance;
                string text = loc != null ? loc.Localize(key) : key;
                if (args != null && args.Length > 0) text = string.Format(text, args);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
            }
            catch { }
        }
    }
}
