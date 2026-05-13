using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Lifecycle;
using JotunnModStub.Companions.Prefabs;
using UnityEngine;

namespace JotunnModStub.Companions.Acquisition
{
    // Called from a Harmony patch on Humanoid.UseItem when the active item is our whistle.
    internal static class WhistleItem
    {
        public static bool OnUse(Player player, ItemDrop.ItemData itemData)
        {
            if (player == null || itemData == null) return false;

            // Confirm this is our whistle by name match.
            if (itemData.m_dropPrefab == null || itemData.m_dropPrefab.name != WolfWhistlePrefab.ItemName)
            {
                return false;
            }

            if (CompanionConfig.AllowCraftableSummon == null || !CompanionConfig.AllowCraftableSummon.Value)
            {
                Show("$valhein.acquire.whistle_disabled", "Whistle summoning is disabled in current config.");
                return true; // consumed event, but no spawn
            }

            Vector3 origin = player.transform.position + player.transform.forward * 2f;
            var go = Spawner.SpawnWolfFor(player, null, AcquisitionKind.Whistle, origin);
            if (go == null) return true;

            if (CompanionConfig.WhistleConsumable != null && CompanionConfig.WhistleConsumable.Value)
            {
                var inv = player.GetInventory();
                inv?.RemoveOneItem(itemData);
            }
            return true;
        }

        private static void Show(string key, string fallback)
        {
            try
            {
                if (MessageHud.instance == null) return;
                var loc = Localization.instance;
                string text = loc != null ? loc.Localize(key) : null;
                if (string.IsNullOrEmpty(text) || text == key) text = fallback;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            }
            catch (System.Exception ex) { Log.Debug($"whistle msg failed: {ex.Message}"); }
        }
    }
}
