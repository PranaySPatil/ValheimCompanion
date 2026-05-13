using JotunnModStub.Companions.Config;

namespace JotunnModStub.Companions.Diagnostics
{
    internal static class Log
    {
        private const string Prefix = "[Valhein.Companions] ";

        public static void Info(string msg)    => Jotunn.Logger.LogInfo(Prefix + msg);
        public static void Warning(string msg) => Jotunn.Logger.LogWarning(Prefix + msg);
        public static void Error(string msg)   => Jotunn.Logger.LogError(Prefix + msg);

        public static void Debug(string msg)
        {
            if (CompanionConfig.LogVerbose != null && CompanionConfig.LogVerbose.Value)
            {
                Jotunn.Logger.LogDebug(Prefix + msg);
            }
        }
    }
}
