using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using JotunnModStub.Companions.Acquisition;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Diagnostics;
using UnityEngine;

namespace JotunnModStub.Companions.Prefabs
{
    internal static class WolfWhistlePrefab
    {
        public const string ItemName = "ValheinWolfWhistle";
        public const string ClonedFrom = "Horn_Bronze";

        public static GameObject Prefab { get; private set; }

        public static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            try
            {
                var go = PrefabManager.Instance.CreateClonedPrefab(ItemName, ClonedFrom);
                if (go == null)
                {
                    Log.Warning($"could not clone '{ClonedFrom}' — whistle disabled");
                    return;
                }

                var marker = go.GetComponent<WolfWhistleMarker>() ?? go.AddComponent<WolfWhistleMarker>();

                var itemDrop = go.GetComponent<ItemDrop>();
                if (itemDrop != null)
                {
                    var shared = itemDrop.m_itemData.m_shared;
                    shared.m_name = "$item_valhein_wolfwhistle";
                    shared.m_description = "$item_valhein_wolfwhistle_desc";
                    shared.m_maxStackSize = 1;
                    shared.m_useDurability = false;
                }

                var requirements = ParseRecipe(CompanionConfig.WhistleRecipe?.Value ?? "Wood:5,LeatherScraps:2,WolfFang:1");
                var recipeCfg = new RecipeConfig
                {
                    Item = ItemName,
                    CraftingStation = "piece_workbench",
                    Requirements = requirements.ToArray(),
                    Enabled = CompanionConfig.AllowCraftableSummon != null && CompanionConfig.AllowCraftableSummon.Value
                };

                var customItem = new CustomItem(go, fixReference: true, new ItemConfig
                {
                    CraftingStation = recipeCfg.CraftingStation,
                    Requirements = requirements.ToArray(),
                    Enabled = recipeCfg.Enabled
                });
                if (!ItemManager.Instance.AddItem(customItem))
                {
                    Log.Warning("whistle item registration was refused");
                    return;
                }
                Prefab = go;
                Log.Info("wolf whistle registered (recipe " + (recipeCfg.Enabled ? "enabled" : "disabled") + ")");
            }
            catch (System.Exception ex)
            {
                Log.Error($"whistle registration failed: {ex.Message}");
            }
        }

        private static List<RequirementConfig> ParseRecipe(string csv)
        {
            var result = new List<RequirementConfig>();
            if (string.IsNullOrEmpty(csv)) return result;
            foreach (var part in csv.Split(','))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                var bits = t.Split(':');
                if (bits.Length < 2) continue;
                if (!int.TryParse(bits[1].Trim(), out var amount)) continue;
                result.Add(new RequirementConfig { Item = bits[0].Trim(), Amount = amount });
            }
            return result;
        }
    }

    // Marker MonoBehaviour to identify whistle use events (consumed by WhistleItem.OnUse hook).
    internal sealed class WolfWhistleMarker : MonoBehaviour
    {
    }
}
