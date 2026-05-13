using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Lifecycle;
using UnityEngine;

namespace JotunnModStub.Companions.Acquisition
{
    internal static class TamePromotion
    {
        public static void TryPromote(Tameable source, Humanoid user, bool hold)
        {
            if (source == null || user == null) return;
            var player = user as Player;
            if (player == null) return;

            if (CompanionConfig.AllowTamePromotion == null || !CompanionConfig.AllowTamePromotion.Value) return;
            if (!hold) return;

            // Only act when the configured key is held.
            var shortcut = CompanionConfig.TamePromotionInteractKey?.Value ?? new BepInEx.Configuration.KeyboardShortcut(KeyCode.E);
            if (!Input.GetKey(shortcut.MainKey)) return;
            foreach (var mod in shortcut.Modifiers)
            {
                if (!Input.GetKey(mod)) return;
            }

            if (!source.IsTamed()) return;
            var sourceCharacter = source.GetComponent<Character>();
            if (sourceCharacter == null) return;
            var prefabName = global::Utils.GetPrefabName(source.gameObject);
            if (prefabName != "Wolf") return;

            // Capture state we need before destroying the source.
            int level = sourceCharacter.GetLevel();
            Vector3 pos = source.transform.position;
            Quaternion rot = source.transform.rotation;

            // Cap check happens inside Spawner.
            var go = Spawner.SpawnWolfFor(player, null, AcquisitionKind.TamePromotion, pos + Vector3.up * 0.1f, level);
            if (go == null) return;

            try
            {
                go.transform.rotation = rot;
                var view = source.GetComponent<ZNetView>();
                if (view != null && view.IsOwner())
                {
                    view.Destroy();
                }
                Log.Info($"promoted tame wolf at {pos} (level {level})");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"failed to clean up source tame: {ex.Message}");
            }
        }
    }
}
