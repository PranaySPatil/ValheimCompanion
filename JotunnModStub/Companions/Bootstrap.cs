using System.Collections.Generic;
using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using JotunnModStub.Companions.Acquisition;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Prefabs;
using JotunnModStub.HarmonyPatches;

namespace JotunnModStub.Companions
{
    internal static class Bootstrap
    {
        private static bool _aborted;

        public static void Run(BaseUnityPlugin plugin)
        {
            // 1. Bind configs.
            CompanionConfig.Bind(plugin.Config);

            // 2. Compute config hash for diagnostics.
            ConfigHash.Compute();

            // 3. Harmony patches must precede prefab registration that depends on them.
            HarmonyBootstrap.PatchAll();

            // 4. Register prefabs via Jotunn lifecycle events.
            PrefabManager.OnVanillaPrefabsAvailable += WolfCompanionPrefab.Register;
            PrefabManager.OnVanillaPrefabsAvailable += WolfWhistlePrefab.Register;

            // 5. Register localization.
            LocStrings.Strings.Register();

            // 6. Register console commands; verify each one was accepted.
            var commands = new List<ConsoleCommand>
            {
                new ConsoleSpawnCommand(),
                new OrderCommand(),
                new DismissCommand(),
                new DiagCommand()
            };
            foreach (var cmd in commands)
            {
                CommandManager.Instance.AddConsoleCommand(cmd);
            }
            var registered = CommandManager.Instance.CustomCommands;
            foreach (var cmd in commands)
            {
                bool present = false;
                foreach (var c in registered)
                {
                    if (c.Name == cmd.Name) { present = true; break; }
                }
                if (!present)
                {
                    Log.Error($"Console command '{cmd.Name}' was refused (likely vanilla collision). Aborting plugin init.");
                    _aborted = true;
                }
            }
            if (_aborted)
            {
                // Fail closed — don't proceed to wire registry rebuild etc.
                return;
            }

            // 7. The ZNetScene.Awake postfix patch (ZNetSceneAwakePatch) wires registry seed.
            Log.Info($"v{JotunnModStub.PluginVersion} — config hash {ConfigHash.Value} — features: " +
                     $"console={(CompanionConfig.AllowConsoleSpawn.Value ? "on" : "off")} " +
                     $"whistle={(CompanionConfig.AllowCraftableSummon.Value ? "on" : "off")} " +
                     $"promote={(CompanionConfig.AllowTamePromotion.Value ? "on" : "off")} " +
                     $"death.mode={CompanionConfig.DeathModeEntry.Value}");
        }
    }
}
