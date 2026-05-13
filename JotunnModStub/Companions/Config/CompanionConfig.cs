using BepInEx.Configuration;
using Jotunn.Configs;
using JotunnModStub.Companions.Data;

namespace JotunnModStub.Companions.Config
{
    internal static class CompanionConfig
    {
        // [General]
        public static ConfigEntry<bool>   LogVerbose;
        public static ConfigEntry<string> NamePoolSeed;

        // [Acquisition]
        public static ConfigEntry<bool>   AllowConsoleSpawn;
        public static ConfigEntry<bool>   AllowCraftableSummon;
        public static ConfigEntry<bool>   AllowTamePromotion;
        public static ConfigEntry<bool>   WhistleConsumable;
        public static ConfigEntry<string> WhistleRecipe;
        public static ConfigEntry<KeyboardShortcut> TamePromotionInteractKey;

        // [Wolf Companion]
        public static ConfigEntry<float>  WolfHealthBase;
        public static ConfigEntry<float>  WolfDamageBlunt;
        public static ConfigEntry<float>  WolfDamageSlash;
        public static ConfigEntry<float>  WolfStaminaBase;
        public static ConfigEntry<float>  WolfEngagementRadius;
        public static ConfigEntry<float>  WolfOwnerEngagementRadius;
        public static ConfigEntry<string> WolfDoNotEngage;
        public static ConfigEntry<float>  WolfFollowDistance;
        public static ConfigEntry<float>  WolfLeashTeleportRadius;
        public static ConfigEntry<float>  WolfLeashTeleportSeconds;
        public static ConfigEntry<bool>   WolfNameplateVisible;
        public static ConfigEntry<int>    WolfDefaultStarLevel;

        // [Death]
        public static ConfigEntry<DeathMode> DeathModeEntry;
        public static ConfigEntry<int>    DeathReviveWindowSeconds;
        public static ConfigEntry<bool>   DeathInvulnerableWhenUnattended;
        public static ConfigEntry<int>    DeathUnattendedZoneRadius;
        public static ConfigEntry<string> DeathReviveItem;
        public static ConfigEntry<int>    DeathReviveItemCount;
        public static ConfigEntry<float>  DeathReviveChannelHpPerSec;
        public static ConfigEntry<float>  DeathReviveHealthPenaltyPct;
        public static ConfigEntry<bool>   DeathDropMemento;

        // [Combat]
        public static ConfigEntry<int>    CombatMaxPerPlayer;

        // [Debug]
        public static ConfigEntry<bool>   DebugVerbose;
        public static ConfigEntry<int>    DebugPerfSampleSeconds;

        public static float WolfFollowDistanceClamped
        {
            get
            {
                var v = WolfFollowDistance != null ? WolfFollowDistance.Value : 8f;
                if (v < 5f) v = 5f;
                if (v > 15f) v = 15f;
                return v;
            }
        }

        public static void Bind(ConfigFile cfg)
        {
            // Convenience: server-synced flag for shared gameplay-affecting settings.
            var serverSync = new ConfigurationManagerAttributes { IsAdminOnly = true };
            var clientLocal = new ConfigurationManagerAttributes { IsAdminOnly = false };

            LogVerbose   = cfg.Bind("General", "LogVerbose", false,
                new ConfigDescription("Enable verbose debug logs.", null, clientLocal));
            NamePoolSeed = cfg.Bind("General", "NamePoolSeed", "",
                new ConfigDescription("Optional comma-separated name pool. Empty uses built-in Norse list.", null, clientLocal));

            AllowConsoleSpawn    = cfg.Bind("Acquisition", "AllowConsoleSpawn", true,
                new ConfigDescription("Allow spawning a companion via console.", null, serverSync));
            AllowCraftableSummon = cfg.Bind("Acquisition", "AllowCraftableSummon", false,
                new ConfigDescription("Allow the Wolf Whistle craftable summoning item.", null, serverSync));
            AllowTamePromotion   = cfg.Bind("Acquisition", "AllowTamePromotion", false,
                new ConfigDescription("Allow promoting a vanilla tamed wolf into a companion.", null, serverSync));
            WhistleConsumable    = cfg.Bind("Acquisition", "WhistleConsumable", true,
                new ConfigDescription("Whether the whistle is consumed on successful summon.", null, serverSync));
            WhistleRecipe        = cfg.Bind("Acquisition", "WhistleRecipe", "Wood:5,LeatherScraps:2,WolfFang:1",
                new ConfigDescription("Recipe for the Wolf Whistle (item:count, comma separated).", null, serverSync));
            TamePromotionInteractKey = cfg.Bind("Acquisition", "TamePromotionInteractKey", new KeyboardShortcut(UnityEngine.KeyCode.E),
                new ConfigDescription("Key held during interact with a vanilla tamed wolf to promote it.", null, clientLocal));

            WolfHealthBase            = cfg.Bind("Wolf Companion", "HealthBase", 200f,
                new ConfigDescription("Base health for a companion wolf.", null, serverSync));
            WolfDamageBlunt           = cfg.Bind("Wolf Companion", "DamageBlunt", 25f,
                new ConfigDescription("Blunt damage of the bite attack.", null, serverSync));
            WolfDamageSlash           = cfg.Bind("Wolf Companion", "DamageSlash", 25f,
                new ConfigDescription("Slash damage of the bite attack.", null, serverSync));
            WolfStaminaBase           = cfg.Bind("Wolf Companion", "StaminaBase", 100f,
                new ConfigDescription("Base stamina for a companion wolf.", null, serverSync));
            WolfEngagementRadius      = cfg.Bind("Wolf Companion", "EngagementRadius", 25f,
                new ConfigDescription("Radius around the wolf at which it engages hostiles.", null, serverSync));
            WolfOwnerEngagementRadius = cfg.Bind("Wolf Companion", "OwnerEngagementRadius", 15f,
                new ConfigDescription("Radius around the owner at which the wolf engages hostiles when following.", null, serverSync));
            WolfDoNotEngage           = cfg.Bind("Wolf Companion", "DoNotEngage", "Deathsquito,Seeker,SeekerBrute,Lox",
                new ConfigDescription("Comma-separated prefab names the wolf will refuse to engage opportunistically.", null, serverSync));
            WolfFollowDistance        = cfg.Bind("Wolf Companion", "FollowDistance", 8f,
                new ConfigDescription("Distance the wolf maintains while following (clamped to [5,15]).", null, serverSync));
            WolfLeashTeleportRadius   = cfg.Bind("Wolf Companion", "LeashTeleportRadius", 60f,
                new ConfigDescription("Distance from owner that triggers leash-teleport timer.", null, serverSync));
            WolfLeashTeleportSeconds  = cfg.Bind("Wolf Companion", "LeashTeleportSeconds", 5f,
                new ConfigDescription("Seconds out of range before the leash-teleport fires.", null, serverSync));
            WolfNameplateVisible      = cfg.Bind("Wolf Companion", "NameplateVisible", true,
                new ConfigDescription("Show a floating name above the companion.", null, clientLocal));
            WolfDefaultStarLevel      = cfg.Bind("Wolf Companion", "DefaultStarLevel", 2,
                new ConfigDescription("Default star count for newly spawned companions (0=no star, 1=★, 2=★★, 3=★★★). " +
                                      "Ignored for tame-promotion, which preserves the source wolf's level.", null, serverSync));

            DeathModeEntry                  = cfg.Bind("Death", "Mode", DeathMode.Permadeath,
                new ConfigDescription("Death model. Permadeath = gone at 0 HP. Downed = revivable. TagRecovery = drops a memento.", null, serverSync));
            DeathReviveWindowSeconds        = cfg.Bind("Death", "ReviveWindowSeconds", 300,
                new ConfigDescription("Seconds the wolf stays revivable before permanent loss.", null, serverSync));
            DeathInvulnerableWhenUnattended = cfg.Bind("Death", "InvulnerableWhenUnattended", true,
                new ConfigDescription("If true, the wolf takes no damage when its owner is offline or in a distant zone.", null, serverSync));
            DeathUnattendedZoneRadius       = cfg.Bind("Death", "UnattendedZoneRadius", 2,
                new ConfigDescription("Zone-radius around owner inside which damage is allowed.", null, serverSync));
            DeathReviveItem                 = cfg.Bind("Death", "ReviveItem", "TrophyGreydwarf",
                new ConfigDescription("Item required to revive a downed companion.", null, serverSync));
            DeathReviveItemCount            = cfg.Bind("Death", "ReviveItemCount", 1,
                new ConfigDescription("How many of the revive item are consumed per revive.", null, serverSync));
            DeathReviveChannelHpPerSec      = cfg.Bind("Death", "ReviveChannelHpPerSec", 5f,
                new ConfigDescription("HP per second restored while channeling a revive.", null, serverSync));
            DeathReviveHealthPenaltyPct     = cfg.Bind("Death", "ReviveHealthPenaltyPct", 0f,
                new ConfigDescription("Percent of max HP missing after a successful revive.", null, serverSync));
            DeathDropMemento                = cfg.Bind("Death", "DropMemento", true,
                new ConfigDescription("Drop a memento at the death location.", null, serverSync));

            CombatMaxPerPlayer = cfg.Bind("Combat", "MaxPerPlayer", 1,
                new ConfigDescription("Maximum combat companions per player.", null, serverSync));

            DebugVerbose          = cfg.Bind("Debug", "Verbose", false,
                new ConfigDescription("Verbose debug output.", null, clientLocal));
            DebugPerfSampleSeconds = cfg.Bind("Debug", "PerfSampleSeconds", 60,
                new ConfigDescription("Measurement window for `valhein_diag --perf`.", null, clientLocal));
        }
    }
}
