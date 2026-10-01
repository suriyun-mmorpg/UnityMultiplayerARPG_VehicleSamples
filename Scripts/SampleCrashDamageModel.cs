using UnityEngine;

namespace MultiplayerARPG
{
    public static class SampleCrashDamageModel
    {
        // Normal speed rejects glancing scrapes; impulse/mass rejects collisions with very light props.
        public static int Calculate(float normalSpeed, float deltaVelocity, float threshold, float damageScale, int maximum)
        {
            if (float.IsNaN(normalSpeed) || float.IsInfinity(normalSpeed) ||
                float.IsNaN(deltaVelocity) || float.IsInfinity(deltaVelocity))
                return 0;
            float excess = Mathf.Max(0f, Mathf.Min(normalSpeed, deltaVelocity) - Mathf.Max(0f, threshold));
            return Mathf.CeilToInt(Mathf.Min(Mathf.Max(0, maximum), excess * excess * Mathf.Max(0f, damageScale)));
        }

    }
}
