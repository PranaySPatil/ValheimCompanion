using HarmonyLib;
using JotunnModStub.Companions.Acquisition;

namespace JotunnModStub.HarmonyPatches
{
    // Catches whistle activation. If our whistle handles the event we skip vanilla use logic.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class HumanoidUseItemPatch
    {
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool fromInventoryGui)
        {
            var player = __instance as Player;
            if (player == null) return true;
            if (item == null || item.m_dropPrefab == null) return true;
            if (item.m_dropPrefab.name != Companions.Prefabs.WolfWhistlePrefab.ItemName) return true;

            WhistleItem.OnUse(player, item);
            return false;
        }
    }
}
