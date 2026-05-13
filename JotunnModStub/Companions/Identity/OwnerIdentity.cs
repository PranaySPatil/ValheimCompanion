using JotunnModStub.Companions.Data;

namespace JotunnModStub.Companions.Identity
{
    internal static class OwnerIdentity
    {
        public static string GetSteamIdOf(Player p)
        {
            if (p == null) return null;
            try
            {
                long id = p.GetPlayerID();
                if (id == 0L) return null;
                return id.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static bool IsOwner(ZDO zdo, Player p)
        {
            if (zdo == null || p == null) return false;
            string steam = GetSteamIdOf(p);
            return IsOwner(zdo, steam);
        }

        public static bool IsOwner(ZDO zdo, string steamId)
        {
            if (zdo == null || string.IsNullOrEmpty(steamId)) return false;
            var stored = zdo.GetString(ZdoKeys.OwnerSteamId, null);
            return !string.IsNullOrEmpty(stored) && stored == steamId;
        }
    }
}
