using System.Collections.Generic;
using Jotunn.Managers;
using JotunnModStub.Companions.Diagnostics;

namespace JotunnModStub.Companions.LocStrings
{
    internal static class Strings
    {
        public const string SpawnWelcome        = "valhein.spawn.welcome";
        public const string CapCombatFull       = "valhein.cap.combat_full";
        public const string DeathDown           = "valhein.death.down";
        public const string DeathFallen         = "valhein.death.fallen";
        public const string DeathRevived        = "valhein.death.revived";
        public const string AcquireConsoleOff   = "valhein.acquire.console_disabled";
        public const string AcquireWhistleOff   = "valhein.acquire.whistle_disabled";
        public const string AcquirePromoteOff   = "valhein.acquire.promote_disabled";
        public const string OrderSet            = "valhein.order.set";
        public const string CmdUnknownCompanion = "valhein.cmd.unknown_companion";

        public const string ItemWhistleName    = "item_valhein_wolfwhistle";
        public const string ItemWhistleDesc    = "item_valhein_wolfwhistle_desc";

        public static void Register()
        {
            try
            {
                var loc = LocalizationManager.Instance.GetLocalization();
                var dict = new Dictionary<string, string>
                {
                    { SpawnWelcome,        "{0} is by your side." },
                    { CapCombatFull,       "You already have {0}/{1} combat companions. Dismiss one first." },
                    { DeathDown,           "{0} is down — revive within {1}s." },
                    { DeathFallen,         "{0} has fallen." },
                    { DeathRevived,        "{0} is back on its feet." },
                    { AcquireConsoleOff,   "Console spawning is disabled in current config." },
                    { AcquireWhistleOff,   "Whistle summoning is disabled in current config." },
                    { AcquirePromoteOff,   "Tame-promotion is disabled in current config." },
                    { OrderSet,            "{0}: {1}." },
                    { CmdUnknownCompanion, "No companion named '{0}' found." },
                    { ItemWhistleName,     "Wolf Whistle" },
                    { ItemWhistleDesc,     "Summon a companion wolf bound to you." }
                };
                loc.AddTranslation("English", dict);
                Log.Info("English localization registered");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"localization registration failed: {ex.Message}");
            }
        }
    }
}
