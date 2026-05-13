namespace JotunnModStub.Companions.Data
{
    internal static class ZdoKeys
    {
        public const string Companion        = "valhein.companion";
        public const string OwnerSteamId     = "valhein.ownerSteamId";
        public const string OwnerNameCached  = "valhein.ownerNameCached";
        public const string Name             = "valhein.name";
        public const string Order            = "valhein.order";
        public const string StayPos          = "valhein.stayPos";
        public const string DeathState       = "valhein.deathState";
        public const string DownedAtUnix     = "valhein.downedAtUnix";
        public const string SchemaVer        = "valhein.schemaVer";
        public const string Cold             = "valhein.cold";
        public const string ReviveStartTick  = "valhein.reviveStartTick";

        public const int CompanionTypeWolf   = 1;
        public const int CompanionTypeWorker = 2;

        public const int CurrentSchemaVer = 1;
    }

    internal enum CompanionOrder
    {
        Follow = 0,
        Stay = 1,
        Aggressive = 2,
        Defensive = 3
    }

    internal enum CompanionStance
    {
        Default = 0,
        Aggressive = 1,
        Defensive = 2
    }

    internal enum CompanionDeathState
    {
        Alive = 0,
        Downed = 1,
        DeadPendingMemento = 2
    }

    internal enum AcquisitionKind : byte
    {
        Console = 0,
        Whistle = 1,
        TamePromotion = 2
    }

    internal enum DeathMode
    {
        Downed,
        Permadeath,
        TagRecovery
    }
}
