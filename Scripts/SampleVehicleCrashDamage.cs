using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>Server environmental damage using kit HP, occupant ejection and scene respawn.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleEntity), typeof(SampleVehicleEntityMovement))]
    public class SampleVehicleCrashDamage : BaseNetworkedGameEntityComponent<VehicleEntity>
    {
        [SerializeField, Min(0f)] private float _minimumImpactSpeed = 5f;
        [SerializeField, Min(0f)] private float _damagePerSpeedSquared = 2.5f;
        [SerializeField, Min(1)] private int _maximumDamage = 1000;
        [SerializeField, Min(0f)] private float _cooldown = 0.2f;
        private Rigidbody _body;
        private bool _initialized;
        private int _pendingDamage;
        private float _lastImpact = float.NegativeInfinity;

        public override void OnIdentityInitialize()
        {
            _body = GetComponent<Rigidbody>(); _initialized = true;
            _pendingDamage = 0; _lastImpact = float.NegativeInfinity;
        }
        public override void OnNetworkDestroy(byte reasons) { _initialized = false; _pendingDamage = 0; }
        private void OnDisable() => _pendingDamage = 0;
        private void OnCollisionEnter(Collision collision) => RecordCollision(collision);
        private void OnCollisionStay(Collision collision) => RecordCollision(collision);
        private void RecordCollision(Collision collision)
        {
            if (!_initialized || !IsServer || !enabled || Entity.IsDead() || Entity.IsInvincible ||
                Time.unscaledTime - _lastImpact < _cooldown) return;
            float speed = 0f;
            for (int i = 0; i < collision.contactCount; ++i)
                speed = Mathf.Max(speed, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            _pendingDamage = Mathf.Max(_pendingDamage, SampleCrashDamageModel.Calculate(speed,
                collision.impulse.magnitude / Mathf.Max(1f, _body.mass), _minimumImpactSpeed, _damagePerSpeedSquared, _maximumDamage));
        }
        private void FixedUpdate()
        {
            int damage = _pendingDamage; _pendingDamage = 0;
            if (!_initialized || !IsServer || damage <= 0 || Entity.IsDead() || Entity.IsInvincible) return;
            _lastImpact = Time.unscaledTime;
            Entity.CurrentHp = Mathf.Max(0, Entity.CurrentHp - damage);
            if (Entity.IsDead()) { GetComponent<SampleVehicleEntityMovement>().StopMove(); Entity.Destroy(); }
        }
    }
}
