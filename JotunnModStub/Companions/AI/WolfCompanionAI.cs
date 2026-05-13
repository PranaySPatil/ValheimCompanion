using System.Collections.Generic;
using System.Diagnostics;
using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Identity;
using UnityEngine;

namespace JotunnModStub.Companions.AI
{
    // MonsterAI subclass implementing the order state machine.
    internal sealed class WolfCompanionAI : MonsterAI
    {
        private float _nextOrderTick;
        private float _lastAttackedAtSelf;
        private const float SelfAttackedWindow = 8f;

        private string _cachedOwnerSteamId;
        private CompanionOrder _order = CompanionOrder.Follow;
        private CompanionStance _stance = CompanionStance.Default;
        private bool _zdoLoaded;

        public CompanionOrder Order => _order;
        public CompanionStance Stance => _stance;
        public string OwnerSteamId => _cachedOwnerSteamId;

        // Copies salient fields from a vanilla MonsterAI to inherit its tunables.
        public void CopyFrom(MonsterAI other)
        {
            if (other == null) return;
            m_alertRange       = other.m_alertRange;
            m_fleeIfHurtWhenTargetCantBeReached = other.m_fleeIfHurtWhenTargetCantBeReached;
            m_fleeIfLowHealth  = 0f;
            m_circulateWhileCharging = other.m_circulateWhileCharging;
            m_attackPlayerObjects = false;
            m_aggravatable     = false;
            m_consumeItems     = other.m_consumeItems;
            m_consumeRange     = other.m_consumeRange;
            m_randomCircleInterval = other.m_randomCircleInterval;
            m_randomMoveRange  = other.m_randomMoveRange;
            m_randomMoveInterval = other.m_randomMoveInterval;
        }

        private void EnsureZdoLoaded()
        {
            if (_zdoLoaded) return;
            var view = GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return;
            var zdo = view.GetZDO();
            if (zdo == null) return;

            _cachedOwnerSteamId = zdo.GetString(ZdoKeys.OwnerSteamId, null);
            _order  = (CompanionOrder)zdo.GetInt(ZdoKeys.Order, (int)CompanionOrder.Follow);
            _stance = StanceFromOrder(_order);

            m_alertRange = Mathf.Max(m_alertRange, CompanionConfig.WolfEngagementRadius?.Value ?? 25f);
            m_attackPlayerObjects = false;
            m_aggravatable = false;
            _zdoLoaded = true;
        }

        public void NoticeSelfDamaged()
        {
            _lastAttackedAtSelf = Time.time;
        }

        private static CompanionStance StanceFromOrder(CompanionOrder order)
        {
            if (order == CompanionOrder.Aggressive) return CompanionStance.Aggressive;
            if (order == CompanionOrder.Defensive) return CompanionStance.Defensive;
            return CompanionStance.Default;
        }

        public void SetOrder(CompanionOrder order, Vector3 currentPos)
        {
            _order = order;
            _stance = StanceFromOrder(order);

            var view = GetComponent<ZNetView>();
            if (view != null && view.IsOwner())
            {
                var zdo = view.GetZDO();
                if (zdo != null)
                {
                    zdo.Set(ZdoKeys.Order, (int)order);
                    if (order == CompanionOrder.Stay)
                    {
                        zdo.Set(ZdoKeys.StayPos, currentPos);
                    }
                }
            }
        }

        public Vector3 GetStayPos()
        {
            var view = GetComponent<ZNetView>();
            if (view == null) return transform.position;
            var zdo = view.GetZDO();
            if (zdo == null) return transform.position;
            return zdo.GetVec3(ZdoKeys.StayPos, transform.position);
        }

        public override bool UpdateAI(float dt)
        {
            EnsureZdoLoaded();
            var sw = PerfCounters.StartAi();
            try
            {
                var view = GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner())
                {
                    return base.UpdateAI(dt);
                }

                // Refresh order direction at most twice a second; vanilla MonsterAI handles
                // the per-frame smooth movement off m_follow.
                if (Time.time >= _nextOrderTick)
                {
                    _nextOrderTick = Time.time + 0.5f;
                    ApplyOrderToVanillaAI();
                }

                // Owner-attacked target honor & do-not-engage list — only override when our
                // policy disagrees with what vanilla would pick.
                var ownerEngaged = !string.IsNullOrEmpty(_cachedOwnerSteamId)
                    ? OwnerCombatTracker.GetRecentTarget(_cachedOwnerSteamId)
                    : null;
                if (ownerEngaged != null)
                {
                    m_targetCreature = ownerEngaged;
                }
                else if (_stance == CompanionStance.Defensive && (Time.time - _lastAttackedAtSelf) > SelfAttackedWindow)
                {
                    m_targetCreature = null;
                }
                else if (m_targetCreature != null && IsExcluded(m_targetCreature))
                {
                    m_targetCreature = null;
                }

                return base.UpdateAI(dt);
            }
            finally
            {
                PerfCounters.StopAi(sw);
            }
        }

        private void ApplyOrderToVanillaAI()
        {
            switch (_order)
            {
                case CompanionOrder.Stay:
                    // Anchor near stay pos: clear m_follow so it doesn't chase the owner;
                    // use a transient anchor at the stay position.
                    m_follow = GetOrCreateStayAnchor();
                    break;

                case CompanionOrder.Follow:
                case CompanionOrder.Aggressive:
                case CompanionOrder.Defensive:
                default:
                    var player = ResolveOwnerPlayer();
                    m_follow = player != null ? player.gameObject : null;
                    DestroyStayAnchor();
                    break;
            }
        }

        private GameObject _stayAnchor;

        private GameObject GetOrCreateStayAnchor()
        {
            if (_stayAnchor != null) return _stayAnchor;
            var stay = GetStayPos();
            _stayAnchor = new GameObject($"ValheinStayAnchor_{name}");
            _stayAnchor.transform.position = stay;
            return _stayAnchor;
        }

        private void DestroyStayAnchor()
        {
            if (_stayAnchor != null)
            {
                Destroy(_stayAnchor);
                _stayAnchor = null;
            }
        }

        private void OnDestroy()
        {
            DestroyStayAnchor();
        }

        private bool IsExcluded(Character c)
        {
            var doNotEngage = EngagementPolicy.ParseDoNotEngageList(CompanionConfig.WolfDoNotEngage?.Value);
            if (doNotEngage.Count == 0) return false;
            return doNotEngage.Contains(global::Utils.GetPrefabName(c.gameObject));
        }

        public Player ResolveOwnerPlayer()
        {
            if (string.IsNullOrEmpty(_cachedOwnerSteamId)) return null;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                try
                {
                    if (p.GetPlayerID().ToString() == _cachedOwnerSteamId) return p;
                }
                catch { }
            }
            return null;
        }
    }
}
