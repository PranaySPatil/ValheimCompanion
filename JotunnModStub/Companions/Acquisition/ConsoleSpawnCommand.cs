using Jotunn.Entities;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Lifecycle;
using UnityEngine;

namespace JotunnModStub.Companions.Acquisition
{
    internal sealed class ConsoleSpawnCommand : ConsoleCommand
    {
        public override string Name => "valhein_spawn";
        public override string Help => "valhein_spawn wolf [name] — spawn a Companion wolf bound to you.";

        public override void Run(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.instance?.Print("Usage: valhein_spawn wolf [name]");
                return;
            }

            string kind = args[0].ToLowerInvariant();
            if (kind != "wolf")
            {
                Console.instance?.Print($"Unknown companion type '{kind}'. Only 'wolf' is supported in Phase 1.");
                return;
            }

            if (CompanionConfig.AllowConsoleSpawn != null && !CompanionConfig.AllowConsoleSpawn.Value)
            {
                Console.instance?.Print(LocalizeOrFallback("$valhein.acquire.console_disabled",
                    "Console spawning is disabled in current config."));
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                Console.instance?.Print("No local player.");
                return;
            }

            string name = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : null;
            Vector3 origin = player.transform.position + player.transform.forward * 2f;

            var go = Spawner.SpawnWolfFor(player, name, AcquisitionKind.Console, origin);
            if (go == null)
            {
                Console.instance?.Print("Spawn failed (see log).");
            }
        }

        private static string LocalizeOrFallback(string key, string fallback)
        {
            try
            {
                var loc = Localization.instance;
                if (loc == null) return fallback;
                string text = loc.Localize(key);
                if (string.IsNullOrEmpty(text) || text == key) return fallback;
                return text;
            }
            catch { return fallback; }
        }
    }
}
