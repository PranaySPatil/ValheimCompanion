using System;
using System.Collections.Generic;
using JotunnModStub.Companions.Config;

namespace JotunnModStub.Companions.Identity
{
    internal static class NameGenerator
    {
        private static readonly string[] DefaultPool =
        {
            "Greybeard", "Skoll", "Hati", "Fenrir", "Bjorn",
            "Ulfr", "Geri", "Freki", "Vargr", "Aslak",
            "Hrolf", "Sigrun", "Astrid", "Eira", "Brynja",
            "Halvar", "Knut", "Ragnar", "Thora", "Yrsa"
        };

        public static string Next(int seed)
        {
            var pool = ResolvePool();
            if (pool.Count == 0) return "Wolf";
            var rnd = new Random(seed);
            return pool[rnd.Next(pool.Count)];
        }

        public static int Hash(string a, long b)
        {
            unchecked
            {
                int h = 17;
                if (a != null) h = h * 31 + a.GetHashCode();
                h = h * 31 + b.GetHashCode();
                return h;
            }
        }

        private static List<string> ResolvePool()
        {
            var cfg = CompanionConfig.NamePoolSeed?.Value;
            var list = new List<string>();
            if (!string.IsNullOrEmpty(cfg))
            {
                foreach (var part in cfg.Split(','))
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) list.Add(trimmed);
                }
                if (list.Count > 0) return list;
            }
            list.AddRange(DefaultPool);
            return list;
        }
    }
}
