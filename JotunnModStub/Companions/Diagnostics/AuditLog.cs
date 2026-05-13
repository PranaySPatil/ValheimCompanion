using System;
using System.Collections.Generic;
using JotunnModStub.Companions.Data;

namespace JotunnModStub.Companions.Diagnostics
{
    internal static class AuditLog
    {
        public static void Append(CompanionState state, string code, string payload = "")
        {
            if (state == null) return;
            long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string line = unix.ToString() + "\t" + (code ?? "") + "\t" + (payload ?? "");
            if (state.AuditRing == null) state.AuditRing = new List<string>();
            state.AuditRing.Add(line);
            while (state.AuditRing.Count > CompanionState.MaxAuditEntries)
            {
                state.AuditRing.RemoveAt(0);
            }
        }

        public static List<string> Tail(CompanionState state, int n)
        {
            var result = new List<string>();
            if (state == null || state.AuditRing == null) return result;
            int start = Math.Max(0, state.AuditRing.Count - n);
            for (int i = start; i < state.AuditRing.Count; i++)
            {
                result.Add(state.AuditRing[i]);
            }
            return result;
        }
    }
}
