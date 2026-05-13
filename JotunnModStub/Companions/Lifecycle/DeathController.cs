using System;
using JotunnModStub.Companions.AI;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using UnityEngine;

namespace JotunnModStub.Companions.Lifecycle
{
    // Death state machine entry. The OnDeath patch routes here.
    internal sealed class DeathController : MonoBehaviour
    {
        private ZNetView _view;
        private Character _character;
        private WolfCompanionAI _ai;
        private ReviveInteraction _revive;
        private float _lastDownedCheck;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
            _ai = GetComponent<WolfCompanionAI>();
        }

        public void HandleDeath()
        {
            if (_view == null || !_view.IsValid()) return;
            var zdo = _view.GetZDO();
            if (zdo == null) return;

            var mode = CompanionConfig.DeathModeEntry?.Value ?? DeathMode.Downed;
            var name = zdo.GetString(ZdoKeys.Name, "Wolf");

            switch (mode)
            {
                case DeathMode.Permadeath:
                    Announce("$valhein.death.fallen", name);
                    AppendAudit(zdo, "Permadeath", string.Empty);
                    DestroyZdoNow();
                    break;

                case DeathMode.TagRecovery:
                    SpawnMemento(transform.position, zdo);
                    Announce("$valhein.death.fallen", name);
                    AppendAudit(zdo, "TagRecoveryDropped", string.Empty);
                    zdo.Set(ZdoKeys.DeathState, (int)CompanionDeathState.DeadPendingMemento);
                    DestroyZdoNow();
                    break;

                case DeathMode.Downed:
                default:
                    EnterDowned(zdo, name);
                    break;
            }
        }

        private void EnterDowned(ZDO zdo, string name)
        {
            zdo.Set(ZdoKeys.DeathState, (int)CompanionDeathState.Downed);
            zdo.Set(ZdoKeys.DownedAtUnix, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            if (_character != null)
            {
                _character.SetMaxHealth(_character.GetMaxHealth());
                _character.SetHealth(1f);
            }

            if (_ai != null)
            {
                _ai.enabled = false;
            }

            if (_revive == null)
            {
                _revive = gameObject.AddComponent<ReviveInteraction>();
            }

            AppendAudit(zdo, "EnteredDowned", string.Empty);

            int window = CompanionConfig.DeathReviveWindowSeconds?.Value ?? 300;
            Announce("$valhein.death.down", name, window);
        }

        private void Update()
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return;
            if (Time.time < _lastDownedCheck + 1f) return;
            _lastDownedCheck = Time.time;

            var zdo = _view.GetZDO();
            if (zdo == null) return;
            int ds = zdo.GetInt(ZdoKeys.DeathState, 0);
            if (ds != (int)CompanionDeathState.Downed) return;

            long downedAt = zdo.GetLong(ZdoKeys.DownedAtUnix, 0L);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int window = CompanionConfig.DeathReviveWindowSeconds?.Value ?? 300;
            if (downedAt > 0 && (now - downedAt) >= window)
            {
                AppendAudit(zdo, "PermadeathByExpiry", string.Empty);
                if (CompanionConfig.DeathDropMemento != null && CompanionConfig.DeathDropMemento.Value)
                {
                    SpawnMemento(transform.position, zdo);
                }
                Announce("$valhein.death.fallen", zdo.GetString(ZdoKeys.Name, "Wolf"));
                DestroyZdoNow();
            }
        }

        public void Revive()
        {
            if (_view == null || !_view.IsValid()) return;
            var zdo = _view.GetZDO();
            if (zdo == null) return;

            zdo.Set(ZdoKeys.DeathState, (int)CompanionDeathState.Alive);
            zdo.Set(ZdoKeys.DownedAtUnix, 0L);

            float penaltyPct = CompanionConfig.DeathReviveHealthPenaltyPct?.Value ?? 0f;
            if (_character != null)
            {
                float maxHp = _character.GetMaxHealth();
                float hp = maxHp * (1f - penaltyPct / 100f);
                _character.SetHealth(hp);
            }
            if (_ai != null) _ai.enabled = true;

            if (_revive != null)
            {
                UnityEngine.Object.Destroy(_revive);
                _revive = null;
            }
            AppendAudit(zdo, "Revived", string.Empty);
            Announce("$valhein.death.revived", zdo.GetString(ZdoKeys.Name, "Wolf"));
        }

        private void DestroyZdoNow()
        {
            if (_view != null && _view.IsValid())
            {
                CompanionRegistry.Remove(_view.GetZDO().m_uid);
                if (_view.IsOwner())
                {
                    _view.Destroy();
                }
            }
        }

        private static void SpawnMemento(Vector3 pos, ZDO zdo)
        {
            try
            {
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("TrophyWolf") : null;
                if (prefab == null) return;
                UnityEngine.Object.Instantiate(prefab, pos + Vector3.up * 0.5f, Quaternion.identity);
            }
            catch (Exception ex)
            {
                Log.Warning($"memento spawn failed: {ex.Message}");
            }
        }

        private static void AppendAudit(ZDO zdo, string code, string payload)
        {
            try
            {
                var bytes = zdo.GetByteArray(ZdoKeys.Cold);
                CompanionState state = new CompanionState();
                if (bytes != null && bytes.Length > 0)
                {
                    if (CompanionStateCodec.TryDecode(bytes, out var decoded) == CompanionStateCodec.DecodeResult.Ok)
                    {
                        state = decoded;
                    }
                }
                AuditLog.Append(state, code, payload);
                zdo.Set(ZdoKeys.Cold, CompanionStateCodec.Encode(state));
            }
            catch (Exception ex)
            {
                Log.Warning($"audit append failed: {ex.Message}");
            }
        }

        private static void Announce(string key, params object[] args)
        {
            try
            {
                if (MessageHud.instance == null) return;
                var loc = Localization.instance;
                string text = loc != null ? loc.Localize(key) : key;
                if (args != null && args.Length > 0)
                {
                    text = string.Format(text, args);
                }
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
            }
            catch (Exception ex)
            {
                Log.Debug($"announce failed: {ex.Message}");
            }
        }
    }
}
