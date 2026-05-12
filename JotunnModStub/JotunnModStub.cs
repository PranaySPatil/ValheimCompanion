using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;

namespace JotunnModStub
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    internal class JotunnModStub : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.jotunnmodstub";
        public const string PluginName = "JotunnModStub";
        public const string PluginVersion = "0.0.1";

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            Jotunn.Logger.LogInfo("ModStub has landed");

            CommandManager.Instance.AddConsoleCommand(new HealCommand());
        }
    }

    public class HealCommand : ConsoleCommand
    {
        public override string Name => "heal2";

        public override string Help => "Fully heal the local player. Usage: heal [amount]";

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Console.instance.Print("No local player found.");
                return;
            }

            float amount = player.GetMaxHealth();
            if (args.Length > 0 && float.TryParse(args[0], out var parsed))
            {
                amount = parsed;
            }

            player.Heal(amount);
            Console.instance.Print($"Healed {player.GetPlayerName()} for {amount} HP.");
        }
    }
}