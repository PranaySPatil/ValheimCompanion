using System;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Identity;
using UnityEngine;

namespace JotunnModStub.Companions.Lifecycle
{
    // Hoverable + Interactable that handles the channeled revive interaction on a Downed companion.
    internal sealed class ReviveInteraction : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _view;
        private Character _character;
        private DeathController _death;
        private float _channelHpAccum;
        private float _lastChannelAt;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
            _death = GetComponent<DeathController>();
        }

        public string GetHoverText()
        {
            try
            {
                var zdo = _view != null ? _view.GetZDO() : null;
                if (zdo == null) return string.Empty;
                string name = zdo.GetString(ZdoKeys.Name, "Wolf");
                string item = CompanionConfig.DeathReviveItem?.Value ?? "TrophyGreydwarf";
                int count = CompanionConfig.DeathReviveItemCount?.Value ?? 1;
                return $"{name} (downed)\n[<color=yellow><b>$KEY_Use</b></color>] Revive — costs {count}x {item}";
            }
            catch { return string.Empty; }
        }

        public string GetHoverName()
        {
            var zdo = _view != null ? _view.GetZDO() : null;
            return zdo != null ? zdo.GetString(ZdoKeys.Name, "Wolf") : "Wolf";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (_view == null || !_view.IsValid()) return false;
            var zdo = _view.GetZDO();
            if (zdo == null) return false;

            var p = user as Player;
            if (p == null) return false;

            if (!OwnerIdentity.IsOwner(zdo, p))
            {
                Show("Not your companion.");
                return false;
            }

            string item = CompanionConfig.DeathReviveItem?.Value ?? "TrophyGreydwarf";
            int count = CompanionConfig.DeathReviveItemCount?.Value ?? 1;
            var inv = p.GetInventory();
            int have = inv != null ? inv.CountItems(item) : 0;
            if (have < count)
            {
                Show($"Need {count}x {item}.");
                return false;
            }

            float hpPerSec = CompanionConfig.DeathReviveChannelHpPerSec?.Value ?? 5f;
            if (!hold)
            {
                _channelHpAccum = 0f;
                zdo.Set(ZdoKeys.ReviveStartTick, (int)Time.frameCount);
            }
            _lastChannelAt = Time.time;

            float dt = Time.deltaTime;
            _channelHpAccum += hpPerSec * dt;
            if (_character != null)
            {
                float newHp = Mathf.Min(_character.GetHealth() + _channelHpAccum, _character.GetMaxHealth());
                _character.SetHealth(newHp);
                _channelHpAccum = 0f;
                if (newHp >= _character.GetMaxHealth() - 0.01f)
                {
                    inv.RemoveItem(item, count);
                    _death?.Revive();
                }
            }
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private void Update()
        {
            if (_lastChannelAt > 0 && Time.time - _lastChannelAt > 0.5f)
            {
                _channelHpAccum = 0f;
                _lastChannelAt = 0f;
            }
        }

        private static void Show(string text)
        {
            try
            {
                if (MessageHud.instance != null)
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
                }
            }
            catch (Exception ex) { Log.Debug($"show msg failed: {ex.Message}"); }
        }
    }
}
