using System.Collections.Generic;
using UnityEngine;

namespace JotunnModStub.Companions.Data
{
    // POCO holding the cold-blob payload. Hot fields live directly on the ZDO.
    internal sealed class CompanionState
    {
        public const ushort Magic = 0x5641; // "VA"
        public const int SchemaVersion = 1;
        public const int MaxAuditEntries = 50;
        public const int MaxAuditEntryBytes = 256;
        public const int MaxBlobBytes = 8 * 1024;

        public int     AppearanceSeed;        // fieldId 1 — reserved Phase 2
        public Vector3 HomePos = NaNVector3;  // fieldId 2 — reserved Phase 2
        public long    HomeBoundZdoId;        // fieldId 3 — reserved Phase 2
        public List<string> AuditRing = new List<string>(); // fieldId 4
        public int     TameLevel;             // fieldId 5
        public AcquisitionKind Acquisition;   // fieldId 6

        public static Vector3 NaNVector3 => new Vector3(float.NaN, float.NaN, float.NaN);
    }
}
