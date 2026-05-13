using UnityEngine;

namespace JotunnModStub.Companions.Data
{
    // Empty marker component for fast GetComponent<>() lookups. Avoids string-tag costs.
    internal sealed class CompanionMarker : MonoBehaviour
    {
        public int CompanionType = ZdoKeys.CompanionTypeWolf;
    }
}
