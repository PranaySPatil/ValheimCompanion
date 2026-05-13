using HarmonyLib;
using JotunnModStub.Companions.AI;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Identity;
using UnityEngine;

namespace JotunnModStub.HarmonyPatches
{
    // Mutes damage if InvulnerableWhenUnattended and the owner is offline / in a distant zone.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class CharacterDamagePatch
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (__instance == null) return true;
            var marker = __instance.GetComponent<CompanionMarker>();
            if (marker == null) return true;

            var view = __instance.GetComponent<ZNetView>();
            var zdo = view != null ? view.GetZDO() : null;
            if (zdo == null) return true;

            // While Downed, the entity is invulnerable until the revive window expires
            // or DeathController explicitly removes it.
            int deathState = zdo.GetInt(ZdoKeys.DeathState, 0);
            if (deathState == (int)CompanionDeathState.Downed)
            {
                hit?.ApplyModifier(0f);
                return false;
            }

            if (CompanionConfig.DeathInvulnerableWhenUnattended == null || !CompanionConfig.DeathInvulnerableWhenUnattended.Value) return true;

            string ownerSteam = zdo.GetString(ZdoKeys.OwnerSteamId, null);
            if (string.IsNullOrEmpty(ownerSteam)) return true;

            Player ownerPlayer = null;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                try
                {
                    if (p.GetPlayerID().ToString() == ownerSteam) { ownerPlayer = p; break; }
                }
                catch { }
            }

            if (ownerPlayer == null)
            {
                hit?.ApplyModifier(0f);
                return false;
            }

            int radius = CompanionConfig.DeathUnattendedZoneRadius?.Value ?? 2;
            var ownerZone = ZoneSystem.GetZone(ownerPlayer.transform.position);
            var selfZone  = ZoneSystem.GetZone(__instance.transform.position);
            int dx = Mathf.Abs(ownerZone.x - selfZone.x);
            int dy = Mathf.Abs(ownerZone.y - selfZone.y);
            if (dx > radius || dy > radius)
            {
                hit?.ApplyModifier(0f);
                return false;
            }

            // Mark the wolf's AI that it just got hit, so engagement policy notices.
            var ai = __instance.GetComponent<WolfCompanionAI>();
            if (ai != null) ai.NoticeSelfDamaged();

            return true;
        }
    }

    // Records player-initiated damage targets so the wolf honors them past its detection radius.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class PlayerDamageTrackingPatch
    {
        private static void Postfix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null) return;
            var attackerChar = hit.GetAttacker();
            var attackerPlayer = attackerChar as Player;
            if (attackerPlayer == null) return;
            try
            {
                string sid = attackerPlayer.GetPlayerID().ToString();
                OwnerCombatTracker.RecordOwnerAttack(sid, __instance);
            }
            catch { }
        }
    }
}
