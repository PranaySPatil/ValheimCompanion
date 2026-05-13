using JotunnModStub.Companions.Config;
using JotunnModStub.Companions.Data;
using UnityEngine;

namespace JotunnModStub.Companions.AI
{
    // F1.12 — follow-distance leash with delayed teleport, disabled in combat.
    internal sealed class LeashController : MonoBehaviour
    {
        private WolfCompanionAI _ai;
        private ZNetView _view;
        private Character _character;
        private float _outOfRangeAccumSec;
        private int _tickCounter;

        private void Awake()
        {
            _ai = GetComponent<WolfCompanionAI>();
            _view = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
        }

        private void FixedUpdate()
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return;
            if (_ai == null || _character == null) return;

            _tickCounter++;
            if ((_tickCounter & 3) != 0) return; // every 4th tick

            float dt = Time.fixedDeltaTime * 4f;

            if (_ai.Order != CompanionOrder.Follow)
            {
                _outOfRangeAccumSec = 0f;
                return;
            }

            // Disable teleport in combat — F1.12 explicit requirement.
            if (_ai.GetTargetCreature() != null)
            {
                _outOfRangeAccumSec = 0f;
                return;
            }

            var owner = _ai.ResolveOwnerPlayer();
            if (owner == null) return;

            float d = Vector3.Distance(transform.position, owner.transform.position);
            float radius = CompanionConfig.WolfLeashTeleportRadius?.Value ?? 60f;
            float secs   = CompanionConfig.WolfLeashTeleportSeconds?.Value ?? 5f;

            if (d > radius)
            {
                _outOfRangeAccumSec += dt;
                if (_outOfRangeAccumSec >= secs)
                {
                    TeleportToOwner(owner);
                    _outOfRangeAccumSec = 0f;
                }
            }
            else
            {
                _outOfRangeAccumSec = 0f;
            }
        }

        private void TeleportToOwner(Player owner)
        {
            var dir = -owner.transform.forward;
            var pos = owner.transform.position + dir * 2f + Vector3.up * 0.2f;
            transform.position = pos;
            if (_character != null)
            {
                _character.SetLookDir(owner.transform.position - pos);
            }
        }
    }
}
