using System.Text;
using Jotunn.Entities;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using UnityEngine;

namespace JotunnModStub.Companions.Diagnostics
{
    internal sealed class DiagCommand : ConsoleCommand
    {
        public override string Name => "valhein_diag";
        public override string Help => "valhein_diag [--perf] — dump companion diagnostics to console + clipboard.";

        public override void Run(string[] args)
        {
            bool perf = false;
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (a == "--perf") perf = true;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("== Valhein Companions diag ==");
            sb.Append("plugin: ").Append(JotunnModStub.PluginVersion);
            sb.Append("    config_hash: ").AppendLine(ConfigHash.Value);

            sb.Append("features: ")
              .Append("console=").Append((CompanionConfig.AllowConsoleSpawn?.Value ?? false) ? "on" : "off")
              .Append(" whistle=").Append((CompanionConfig.AllowCraftableSummon?.Value ?? false) ? "on" : "off")
              .Append(" promote=").Append((CompanionConfig.AllowTamePromotion?.Value ?? false) ? "on" : "off")
              .Append(" death.mode=").AppendLine((CompanionConfig.DeathModeEntry?.Value ?? DeathMode.Downed).ToString());

            int count = 0;
            var rows = new StringBuilder();
            foreach (var h in CompanionRegistry.All())
            {
                count++;
                rows.Append("  [Wolf]  ").Append(h.Name).Append("  steamid=").Append(h.OwnerSteamId).Append("  zdo=").AppendLine(h.ZdoId.ToString());
            }
            sb.Append("companions (").Append(count).AppendLine("):");
            sb.Append(rows);

            if (perf)
            {
                var ai = PerfCounters.AiStats();
                var leash = PerfCounters.LeashStats();
                sb.AppendLine("perf:");
                sb.AppendLine($"  AI tick avg: {ai.AvgMs:0.00} ms   p99: {ai.P99Ms:0.00} ms   samples: {ai.Samples}");
                sb.AppendLine($"  Leash tick avg: {leash.AvgMs:0.00} ms   p99: {leash.P99Ms:0.00} ms   samples: {leash.Samples}");
            }

            string text = sb.ToString();
            Console.instance?.Print(text);
            try { GUIUtility.systemCopyBuffer = text; } catch { }
        }
    }
}
