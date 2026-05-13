using System.Collections.Generic;
using HarmonyLib;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;

namespace JotunnModStub.HarmonyPatches
{
    // Seed CompanionRegistry from existing ZDOs on world load (§5.3 load flow).
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix()
        {
            try
            {
                CompanionRegistry.Clear();
                if (ZDOMan.instance == null) return;

                var dict = ZDOMan.instance.m_objectsByID;
                if (dict == null) return;

                int seeded = 0;
                foreach (var kv in dict)
                {
                    var zdo = kv.Value;
                    if (zdo == null) continue;
                    int tag = zdo.GetInt(ZdoKeys.Companion, 0);
                    if (tag == 0) continue;

                    int schemaVer = zdo.GetInt(ZdoKeys.SchemaVer, 1);
                    if (schemaVer > ZdoKeys.CurrentSchemaVer)
                    {
                        Log.Error($"companion ZDO {kv.Key} has future schemaVer {schemaVer}; skipping");
                        continue;
                    }

                    string displayName = zdo.GetString(ZdoKeys.Name, "Wolf");
                    var handle = new CompanionHandle
                    {
                        ZdoId = kv.Key,
                        CompanionType = tag,
                        OwnerSteamId = zdo.GetString(ZdoKeys.OwnerSteamId, null),
                        Name = displayName,
                        DisplayName = displayName,
                        CachedHpFraction = 1f,
                        // Phase 1 ZDOs don't carry an acquired-at stamp. Phase 2's PhaseMigrations
                        // (step 2) backfills a real value; for now legacy wolves sort to "earliest".
                        AcquiredAtUnix = 0L
                    };
                    CompanionRegistry.Add(handle);
                    seeded++;
                }
                if (seeded > 0) Log.Info($"seeded registry with {seeded} companion(s) from ZDOs");
            }
            catch (System.Exception ex)
            {
                Log.Error($"registry seed failed: {ex.Message}");
            }
        }
    }
}
