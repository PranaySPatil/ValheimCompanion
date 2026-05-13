using System.Reflection;
using HarmonyLib;
using JotunnModStub.Companions.Diagnostics;

namespace JotunnModStub.HarmonyPatches
{
    internal static class HarmonyBootstrap
    {
        public const string HarmonyId = "com.jotunn.jotunnmodstub.companions";

        private static Harmony _instance;

        public static void PatchAll()
        {
            try
            {
                _instance = new Harmony(HarmonyId);
                _instance.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("Harmony patches applied");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Harmony patch failed: {ex.Message}");
            }
        }
    }
}
