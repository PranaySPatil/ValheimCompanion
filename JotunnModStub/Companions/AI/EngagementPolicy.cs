using System.Collections.Generic;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using UnityEngine;

namespace JotunnModStub.Companions.AI
{
    // Pure target-selection rules (§6.2). No Unity API calls — testable in isolation.
    internal static class EngagementPolicy
    {
        public struct Inputs
        {
            public Vector3 SelfPos;
            public Vector3 OwnerPos;
            public CompanionOrder Order;
            public CompanionStance Stance;
            public bool SelfAttackedRecently;
            public Character OwnerEngagedTarget;
            public IList<Character> Candidates;
            public float EngagementRadius;
            public float OwnerEngagementRadius;
            public HashSet<string> DoNotEngage;
        }

        public static Character SelectTarget(Inputs inputs)
        {
            // 1. Honor owner-attacked target regardless of radius (F1.4).
            if (inputs.OwnerEngagedTarget != null && IsAlive(inputs.OwnerEngagedTarget))
            {
                return inputs.OwnerEngagedTarget;
            }

            // 2. Defensive stance only engages if attacked.
            if (inputs.Stance == CompanionStance.Defensive && !inputs.SelfAttackedRecently)
            {
                return null;
            }

            float radius = inputs.Stance == CompanionStance.Aggressive
                ? inputs.EngagementRadius * 2f
                : inputs.EngagementRadius;

            Character best = null;
            float bestDist = float.MaxValue;

            if (inputs.Candidates == null) return null;
            for (int i = 0; i < inputs.Candidates.Count; i++)
            {
                var c = inputs.Candidates[i];
                if (c == null || !IsAlive(c)) continue;
                if (!IsHostile(c)) continue;
                if (IsExcluded(c, inputs.DoNotEngage)) continue;

                float distSelf = Vector3.Distance(c.transform.position, inputs.SelfPos);
                float distOwner = Vector3.Distance(c.transform.position, inputs.OwnerPos);

                bool inSelf = distSelf <= radius;
                bool inOwner = inputs.Order == CompanionOrder.Follow && distOwner <= inputs.OwnerEngagementRadius;

                if (inSelf || inOwner)
                {
                    float d = Mathf.Min(distSelf, distOwner);
                    if (d < bestDist)
                    {
                        best = c;
                        bestDist = d;
                    }
                }
            }
            return best;
        }

        private static bool IsAlive(Character c)
        {
            return c != null && !c.IsDead();
        }

        private static bool IsHostile(Character c)
        {
            if (c is Player) return false;
            if (c.IsTamed()) return false;
            if (c.GetComponent<CompanionMarker>() != null) return false;
            return c.m_faction != Character.Faction.Players;
        }

        private static bool IsExcluded(Character c, HashSet<string> doNotEngage)
        {
            if (doNotEngage == null || doNotEngage.Count == 0) return false;
            var prefabName = global::Utils.GetPrefabName(c.gameObject);
            return doNotEngage.Contains(prefabName);
        }

        public static HashSet<string> ParseDoNotEngageList(string csv)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(csv)) return set;
            foreach (var part in csv.Split(','))
            {
                var t = part.Trim();
                if (!string.IsNullOrEmpty(t)) set.Add(t);
            }
            return set;
        }
    }

    // Per-player ring buffer recording the player's most-recent damage target.
    internal static class OwnerCombatTracker
    {
        private static readonly Dictionary<string, OwnerCombatRecord> _byOwner = new Dictionary<string, OwnerCombatRecord>();
        private const float Ttl = 6f;

        public static void RecordOwnerAttack(string steamId, Character target)
        {
            if (string.IsNullOrEmpty(steamId) || target == null) return;
            _byOwner[steamId] = new OwnerCombatRecord
            {
                Target = target,
                ExpiresAt = Time.time + Ttl
            };
        }

        public static Character GetRecentTarget(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) return null;
            if (!_byOwner.TryGetValue(steamId, out var rec)) return null;
            if (rec.ExpiresAt < Time.time) return null;
            if (rec.Target == null || rec.Target.IsDead()) return null;
            return rec.Target;
        }
    }

    internal struct OwnerCombatRecord
    {
        public Character Target;
        public float ExpiresAt;
    }
}
