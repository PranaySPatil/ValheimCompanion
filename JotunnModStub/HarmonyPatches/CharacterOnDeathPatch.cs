using HarmonyLib;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Lifecycle;

namespace JotunnModStub.HarmonyPatches
{
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class CharacterOnDeathPatch
    {
        // Returning false skips vanilla OnDeath. We do that for companions so the entity
        // is not destroyed in Downed mode (which would happen as part of vanilla cleanup).
        // For Permadeath/TagRecovery, DeathController itself destroys the ZDO.
        private static bool Prefix(Character __instance)
        {
            if (__instance == null) return true;
            var marker = __instance.GetComponent<CompanionMarker>();
            if (marker == null) return true;

            var death = __instance.GetComponent<DeathController>();
            if (death == null) return true;

            death.HandleDeath();
            return false;
        }
    }
}
