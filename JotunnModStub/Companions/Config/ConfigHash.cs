using System;
using System.Security.Cryptography;
using System.Text;

namespace JotunnModStub.Companions.Config
{
    internal static class ConfigHash
    {
        public static string Value { get; private set; } = "unknown";

        public static void Compute()
        {
            var sb = new StringBuilder();
            void Add(string key, object val) => sb.Append(key).Append('=').Append(val).Append(';');

            Add("AllowConsoleSpawn",    CompanionConfig.AllowConsoleSpawn?.Value);
            Add("AllowCraftableSummon", CompanionConfig.AllowCraftableSummon?.Value);
            Add("AllowTamePromotion",   CompanionConfig.AllowTamePromotion?.Value);
            Add("WhistleConsumable",    CompanionConfig.WhistleConsumable?.Value);
            Add("WhistleRecipe",        CompanionConfig.WhistleRecipe?.Value);
            Add("WolfHealthBase",       CompanionConfig.WolfHealthBase?.Value);
            Add("WolfDamageBlunt",      CompanionConfig.WolfDamageBlunt?.Value);
            Add("WolfDamageSlash",      CompanionConfig.WolfDamageSlash?.Value);
            Add("WolfStaminaBase",      CompanionConfig.WolfStaminaBase?.Value);
            Add("WolfEngagementRadius", CompanionConfig.WolfEngagementRadius?.Value);
            Add("WolfOwnerEngagementRadius", CompanionConfig.WolfOwnerEngagementRadius?.Value);
            Add("WolfDoNotEngage",      CompanionConfig.WolfDoNotEngage?.Value);
            Add("WolfFollowDistance",   CompanionConfig.WolfFollowDistance?.Value);
            Add("WolfLeashTeleportRadius",  CompanionConfig.WolfLeashTeleportRadius?.Value);
            Add("WolfLeashTeleportSeconds", CompanionConfig.WolfLeashTeleportSeconds?.Value);
            Add("DeathMode",            CompanionConfig.DeathModeEntry?.Value);
            Add("DeathReviveWindow",    CompanionConfig.DeathReviveWindowSeconds?.Value);
            Add("DeathInvuln",          CompanionConfig.DeathInvulnerableWhenUnattended?.Value);
            Add("DeathReviveItem",      CompanionConfig.DeathReviveItem?.Value);
            Add("DeathReviveItemCount", CompanionConfig.DeathReviveItemCount?.Value);
            Add("DeathDropMemento",     CompanionConfig.DeathDropMemento?.Value);
            Add("CombatMaxPerPlayer",   CompanionConfig.CombatMaxPerPlayer?.Value);

            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(16);
                for (int i = 0; i < 4; i++) hex.Append(bytes[i].ToString("x2"));
                Value = hex.ToString();
            }
        }
    }
}
