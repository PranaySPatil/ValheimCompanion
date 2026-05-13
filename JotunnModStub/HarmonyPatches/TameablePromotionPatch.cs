using HarmonyLib;
using JotunnModStub.Companions.Acquisition;

namespace JotunnModStub.HarmonyPatches
{
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class TameablePromotionPatch
    {
        private static void Postfix(Tameable __instance, Humanoid user, bool hold, bool alt)
        {
            TamePromotion.TryPromote(__instance, user, hold);
        }
    }
}
