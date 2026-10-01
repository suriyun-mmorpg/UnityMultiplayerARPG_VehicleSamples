using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteNetLib.Utils;
using LiteNetLibManager;
using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Server-authoritative Standard Assets vehicle adapter. Clients send controls through the kit's movement channel;
    /// the server simulates sample physics and replicates body/wheel poses and drivetrain telemetry.
    /// Owning drivers optionally predict physics with bounded server reconciliation.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Rigidbody), typeof(SampleVehiclePhysics))]
    [DisallowMultipleComponent]
    public class SampleVehicleEntityMovement : BaseNetworkedGameEntityComponent<BaseGameEntity>,
        IEntityMovementComponent, IEntityMovementDataHandler
    {
        [SerializeField, Min(0.1f)] private float _inputTimeout = 0.5f;
        [SerializeField, Min(1f)] private float _interpolationSpeed = 15f;
        [SerializeField, Min(0.1f)] private float _snapDistance = 10f;
        [SerializeField, Min(0f)] private float _stoppingDistance = 1f;
        [Header("Driver prediction")]
        [SerializeField] private bool _enablePrediction = true;
        [SerializeField, Min(0f)] private float _maxExtrapolation = 0.15f;
        [SerializeField, Min(0.1f)] private float _reconciliationSpeed = 3f;
        [SerializeField, Min(0.1f)] private float _predictionSnapDistance = 5f;
        [Tooltip("Optional moving visual parts, ordered parent before child.")]
        [SerializeField] private Transform[] _additionalVisuals = System.Array.Empty<Transform>();

        public SampleVehiclePhysics PhysicsController { get; private set; }
        public Rigidbody Body { get; private set; }
        public float StoppingDistance => _stoppingDistance;
        public MovementState MovementState { get; private set; }
        public ExtraMovementState ExtraMovementState => ExtraMovementState.None;
        public DirectionVector2 Direction2D { get; set; }
        public float CurrentMoveSpeed => IsServer || _predicting ? Body.velocity.magnitude : _serverVelocity.magnitude;
        public bool IsPredicting => _predicting;
        public SampleVehicleTelemetry Telemetry => IsServer || _predicting ? PhysicsController.Capture() : _telemetry;

        private SampleVehicleInput _localInput = SampleVehicleInput.Parked;
        private SampleVehicleInput _simulationInput = SampleVehicleInput.Parked;
        private readonly SampleVehicleControlSession _controls = new SampleVehicleControlSession();
        private Transform[] _wheels;
        private WheelCollider[] _wheelColliders;
        private Vector3[] _wheelPositions;
        private Quaternion[] _wheelRotations;
        private Vector3[] _visualPositions;
        private Quaternion[] _visualRotations;
        private Vector3 _serverPosition;
        private Quaternion _serverRotation;
        private Vector3 _serverVelocity;
        private Vector3 _serverAngularVelocity;
        private SampleVehicleTelemetry _telemetry;
        private float _snapshotTime;
        private float _snapshotTransitTime;
        private bool _predicting;
        private bool _simulating;
        private RigidbodyInterpolation _simulationInterpolation;
        private bool _serverSimulationEnabled;
        private long _snapshotOwnerId = long.MinValue;
        private uint _snapshotDriverId;
        private uint _receivedGeneration;
        private bool _initialized;
        private bool _hasSnapshot;
        private long _snapshotTimestamp = long.MinValue;
        private float _lastLocalInputTime = float.NegativeInfinity;
        private uint _teleportRevision;
        private uint _receivedTeleportRevision;
        private readonly List<EntityMovementForceApplier> _forces = new List<EntityMovementForceApplier>();

        private bool HasDriver => Entity is IVehicleEntity vehicle && vehicle.HasDriver;
        private uint DriverId => HasDriver ? ((IVehicleEntity)Entity).GetPassenger(0).ObjectId : 0;
        private bool IsLocalDriver => IsOwnerClient && HasDriver && ((IVehicleEntity)Entity).GetPassenger(0).IsOwnerClient;
        private bool MatchesSnapshotDriver => _hasSnapshot && ConnectionId == _snapshotOwnerId && DriverId == _snapshotDriverId;
        private bool CanDriveNow => HasDriver && Entity.CanMove() && !(Entity is IDamageableEntity damageable && damageable.IsDead());

        private void Awake()
        {
            PhysicsController = GetComponent<SampleVehiclePhysics>();
            PhysicsController.Initialize();
            Body = GetComponent<Rigidbody>();
            _simulationInterpolation = Body.interpolation;
            _wheels = PhysicsController.Wheels;
            _wheelColliders = GetComponentsInChildren<WheelCollider>(true);
            _wheelPositions = new Vector3[_wheels.Length];
            _wheelRotations = new Quaternion[_wheels.Length];
            _visualPositions = new Vector3[_additionalVisuals.Length];
            _visualRotations = new Quaternion[_additionalVisuals.Length];
            LiteNetLibTransform legacyTransform = GetComponent<LiteNetLibTransform>();
            if (legacyTransform != null)
                legacyTransform.enabled = false;
            SetSimulation(false);
        }

        private void OnEnable()
        {
            if (Body != null)
                RefreshSimulation();
        }

        private void OnDisable()
        {
            ResetControls();
            if (IsServer) _controls.Invalidate();
            _forces.Clear();
            if (Body != null)
                SetSimulation(false);
        }

        public override void OnIdentityInitialize()
        {
            _initialized = true;
            _hasSnapshot = false;
            _snapshotTimestamp = long.MinValue;
            ResetControls();
            _forces.Clear();
            PhysicsController.ResetState();
            CurrentGameManager.EntityMovementDataHandlers[ObjectId] = this;
            _controls.Invalidate();
            RefreshInputOwner();
            SetSimulation(IsServer && enabled);
        }

        public override void OnNetworkDestroy(byte reasons)
        {
            CurrentGameManager.EntityMovementDataHandlers.TryRemove(ObjectId, out _);
            _initialized = false;
            _hasSnapshot = false;
            ResetControls();
            _forces.Clear();
            SetSimulation(false);
        }

        public override void OnSetOwnerClient(bool isOwnerClient)
        {
            ResetControls();
            if (IsServer) _controls.Invalidate();
            if (PhysicsController != null)
            {
                PhysicsController.ResetState();
                RefreshSimulation();
            }
        }

        private void ResetControls()
        {
            _localInput = _simulationInput = SampleVehicleInput.Parked;
            _lastLocalInputTime = float.NegativeInfinity;
            _controls.ClearInput();
        }

        private void RefreshSimulation()
        {
            bool predict = _initialized && enabled && SampleVehiclePrediction.CanPredict(_enablePrediction && _serverSimulationEnabled,
                IsServer, IsLocalDriver, _hasSnapshot, ConnectionId, DriverId, _snapshotOwnerId, _snapshotDriverId);
            if (predict != _predicting)
            {
                _predicting = predict;
                PhysicsController.ResetState();
                // Start each owner from a known server pose; old local forces never survive a handover.
                if (_hasSnapshot && !IsServer)
                {
                    Body.position = _serverPosition;
                    Body.rotation = _serverRotation;
                    PhysicsController.ApplyTelemetry(_telemetry, _serverVelocity);
                }
            }
            bool simulate = _initialized && enabled && (IsServer || predict);
            if (simulate != _simulating)
            {
                SetSimulation(simulate);
                if (predict)
                {
                    Body.velocity = _serverVelocity;
                    Body.angularVelocity = _serverAngularVelocity;
                }
            }
        }

        private void SetSimulation(bool simulate)
        {
            _simulating = simulate;
            if (!simulate) _predicting = false;
            if (!simulate && !Body.isKinematic)
            {
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Body.isKinematic = !simulate;
            Body.interpolation = simulate ? _simulationInterpolation : RigidbodyInterpolation.None;
            PhysicsController.SetSimulation(simulate);
        }

        private void RefreshInputOwner()
        {
            if (IsServer && _controls.UpdateDriver(ConnectionId, DriverId))
            {
                _localInput = _simulationInput = SampleVehicleInput.Parked;
                _lastLocalInputTime = float.NegativeInfinity;
                PhysicsController.ResetState();
            }
        }

        private void FixedUpdate()
        {
            if (!_initialized)
                return;
            RefreshSimulation();
            if (!IsServer && !_predicting)
                return;
            RefreshInputOwner();
            _simulationInput = IsServer ? _controls.GetInput(Time.unscaledTime, _inputTimeout, CanDriveNow)
                : CanDriveNow && Time.unscaledTime - _lastLocalInputTime <= _inputTimeout ? _localInput : SampleVehicleInput.Parked;
            if (_predicting)
                ReconcilePrediction();
            PhysicsController.Simulate(_simulationInput);
            MovementState = PhysicsController.IsGrounded ? MovementState.IsGrounded : MovementState.None;
            if (Body.velocity.sqrMagnitude > 0.01f)
                MovementState |= Vector3.Dot(Body.velocity, transform.forward) < 0f ? MovementState.Backward : MovementState.Forward;
            _forces.UpdateForces(Time.fixedDeltaTime, 0f, out Vector3 forceVelocity, out EntityMovementForceApplier replacement);
            if (replacement != null)
                Body.velocity = replacement.Velocity + forceVelocity;
            else if (forceVelocity.sqrMagnitude > 0f)
                Body.AddForce(forceVelocity * Time.fixedDeltaTime, ForceMode.VelocityChange);
        }

        private void Update()
        {
            if (!_initialized || IsServer || !_hasSnapshot)
                return;
            RefreshSimulation();
            if (_predicting)
                return;
            UpdateRemoteMovement(Time.deltaTime);
        }

        private void UpdateRemoteMovement(float deltaTime)
        {
            float factor = 1f - Mathf.Exp(-_interpolationSpeed * deltaTime);
            PhysicsController.ApplyTelemetry(_telemetry, _serverVelocity);
            float lead = _serverSimulationEnabled
                ? SampleVehiclePrediction.ExtrapolationTime(Time.unscaledTime - _snapshotTime + _snapshotTransitTime, _maxExtrapolation)
                : 0f;
            Body.position = Vector3.Lerp(Body.position, _serverPosition + _serverVelocity * lead, factor);
            Body.rotation = Quaternion.Slerp(Body.rotation,
                SampleVehiclePrediction.ExtrapolateRotation(_serverRotation, _serverAngularVelocity, lead), factor);
        }

        private void LateUpdate()
        {
            if (_initialized && !IsServer && _hasSnapshot && !_predicting)
                ApplyRemoteVisuals();
        }

        private void ApplyRemoteVisuals()
        {
            // Parent visual poses must be applied before the wheel views they carry.
            for (int i = 0; i < _additionalVisuals.Length; ++i)
            {
                if (_additionalVisuals[i] == null) continue;
                _additionalVisuals[i].SetPositionAndRotation(transform.TransformPoint(_visualPositions[i]),
                    transform.rotation * _visualRotations[i]);
            }
            for (int i = 0; i < _wheels.Length; ++i)
            {
                if (_wheels[i] == null)
                    continue;
                Transform view = _wheels[i];
                view.SetPositionAndRotation(transform.TransformPoint(_wheelPositions[i]), transform.rotation * _wheelRotations[i]);
            }
        }

        public void SetInput(SampleVehicleInput input)
        {
            if (!_initialized || !enabled || !IsLocalDriver)
                return;
            RefreshInputOwner();
            _localInput = input.Sanitize();
            _lastLocalInputTime = Time.unscaledTime;
            if (IsServer)
                _controls.SetLocal(_localInput, Time.unscaledTime);
        }

        public bool WriteClientState(long writeTimestamp, NetDataWriter writer, out bool shouldSendReliably)
        {
            shouldSendReliably = false;
            if (!_initialized || !IsLocalDriver || !MatchesSnapshotDriver)
                return false;
            writer.Put(_receivedGeneration);
            (enabled && Time.unscaledTime - _lastLocalInputTime <= _inputTimeout ? _localInput : SampleVehicleInput.Parked).Write(writer);
            return true;
        }

        public void ReadClientStateAtServer(long peerTimestamp, NetDataReader reader)
        {
            uint generation = reader.GetUInt();
            SampleVehicleInput input = SampleVehicleInput.Read(reader);
            if (!IsServer || !enabled || !HasDriver)
                return;
            RefreshInputOwner();
            // BaseGameNetworkManager already validates the packet's connection against this owner.
            if (((IVehicleEntity)Entity).GetPassenger(0).ConnectionId != ConnectionId)
                return;
            _controls.Accept(generation, peerTimestamp, input, Time.unscaledTime);
        }

        public bool WriteServerState(long writeTimestamp, NetDataWriter writer, out bool shouldSendReliably)
        {
            shouldSendReliably = false;
            if (!_initialized || !IsServer)
                return false;
            RefreshInputOwner();
            writer.Put(_teleportRevision);
            writer.Put(_controls.Generation);
            writer.Put(_controls.OwnerId);
            writer.Put(_controls.DriverId);
            writer.Put(enabled);
            writer.PutVector3(Body.position);
            writer.PutQuaternion(Body.rotation);
            writer.PutVector3(Body.velocity);
            writer.PutVector3(Body.angularVelocity);
            PhysicsController.Capture().Write(writer);
            writer.Put((uint)MovementState);
            writer.Put((ushort)_additionalVisuals.Length);
            writer.Put((ushort)_wheels.Length);
            for (int i = 0; i < _wheels.Length; ++i)
            {
                Transform view = _wheels[i];
                writer.PutVector3(view != null ? transform.InverseTransformPoint(view.position) : Vector3.zero);
                writer.PutQuaternion(view != null ? Quaternion.Inverse(transform.rotation) * view.rotation : Quaternion.identity);
            }
            foreach (Transform visual in _additionalVisuals)
            {
                writer.PutVector3(visual != null ? transform.InverseTransformPoint(visual.position) : Vector3.zero);
                writer.PutQuaternion(visual != null ? Quaternion.Inverse(transform.rotation) * visual.rotation : Quaternion.identity);
            }
            return true;
        }

        public void ReadServerStateAtClient(long peerTimestamp, NetDataReader reader)
        {
            uint revision = reader.GetUInt();
            uint generation = reader.GetUInt();
            long ownerId = reader.GetLong();
            uint driverId = reader.GetUInt();
            bool serverSimulationEnabled = reader.GetBool();
            Vector3 position = reader.GetVector3();
            Quaternion rotation = reader.GetQuaternion();
            Vector3 velocity = reader.GetVector3();
            Vector3 angularVelocity = reader.GetVector3();
            SampleVehicleTelemetry telemetry = SampleVehicleTelemetry.Read(reader);
            MovementState movementState = (MovementState)reader.GetUInt();
            int visualCount = reader.GetUShort();
            int wheelCount = reader.GetUShort();
            bool accept = !IsServer && peerTimestamp > _snapshotTimestamp && wheelCount == _wheels.Length && visualCount == _additionalVisuals.Length;
            for (int i = 0; i < wheelCount; ++i)
            {
                Vector3 wheelPosition = reader.GetVector3();
                Quaternion wheelRotation = reader.GetQuaternion();
                if (accept)
                {
                    _wheelPositions[i] = wheelPosition;
                    _wheelRotations[i] = wheelRotation;
                }
            }
            for (int i = 0; i < visualCount; ++i)
            {
                Vector3 visualPosition = reader.GetVector3();
                Quaternion visualRotation = reader.GetQuaternion();
                if (!accept) continue;
                _visualPositions[i] = visualPosition;
                _visualRotations[i] = visualRotation;
            }
            if (!accept)
                return;
            _snapshotTimestamp = peerTimestamp;
            bool newSession = !_hasSnapshot || generation != _receivedGeneration;
            bool teleport = !_hasSnapshot || revision != _receivedTeleportRevision;
            if (newSession)
                ResetControls();
            if (teleport || newSession || Vector3.Distance(Body.position, position) > (_predicting ? _predictionSnapDistance : _snapDistance))
            {
                Body.position = position;
                Body.rotation = rotation;
                if (!Body.isKinematic)
                {
                    Body.velocity = velocity;
                    Body.angularVelocity = angularVelocity;
                }
                PhysicsController.ResetState();
            }
            _receivedTeleportRevision = revision;
            _receivedGeneration = generation;
            _snapshotOwnerId = ownerId;
            _snapshotDriverId = driverId;
            _serverSimulationEnabled = serverSimulationEnabled;
            _serverPosition = position;
            _serverRotation = rotation;
            _serverVelocity = velocity;
            _serverAngularVelocity = angularVelocity;
            _telemetry = telemetry;
            _snapshotTime = Time.unscaledTime;
            _snapshotTransitTime = Entity != null && CurrentGameManager != null ? Mathf.Min(_maxExtrapolation, CurrentGameManager.Rtt * 0.0005f) : 0f;
            MovementState = movementState;
            _hasSnapshot = true;
            if (_predicting && (teleport || newSession))
                PhysicsController.ApplyTelemetry(telemetry, velocity);
        }

        private void ReconcilePrediction()
        {
            float age = Time.unscaledTime - _snapshotTime;
            // A disconnected owner must not keep predicting indefinitely or keep the throttle held.
            if (age > _inputTimeout)
            {
                _simulationInput = SampleVehicleInput.Parked;
                return;
            }
            float lead = SampleVehiclePrediction.ExtrapolationTime(age + _snapshotTransitTime, _maxExtrapolation);
            Vector3 target = _serverPosition + _serverVelocity * lead;
            Quaternion rotation = SampleVehiclePrediction.ExtrapolateRotation(_serverRotation, _serverAngularVelocity, lead);
            float blend = 1f - Mathf.Exp(-_reconciliationSpeed * Time.fixedDeltaTime);
            if (Vector3.Distance(Body.position, target) > _predictionSnapDistance)
                blend = 1f;
            Body.position = Vector3.Lerp(Body.position, target, blend);
            Body.rotation = Quaternion.Slerp(Body.rotation, rotation, blend);
            Body.velocity = Vector3.Lerp(Body.velocity, _serverVelocity, blend);
            Body.angularVelocity = Vector3.Lerp(Body.angularVelocity, _serverAngularVelocity, blend);
        }

        public Bounds GetMovementBounds()
        {
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.isTrigger && !(collider is WheelCollider))
                    bounds.Encapsulate(collider.bounds);
            }
            return bounds;
        }

        public void StopMove()
        {
            ResetControls();
        }

        // Generic locomotion commands do not steer a car or rotate its physical body.
        // SampleVehiclePlayerController exclusively supplies SetInput instead.
        public void KeyMovement(Vector3 moveDirection, MovementState moveState) { }
        public void PointClickMovement(Vector3 position) { }
        public void SetExtraMovementState(ExtraMovementState state) { }
        public void SetLookRotation(Quaternion rotation, bool immediately) { }
        public Quaternion GetLookRotation() => Body.rotation;
        public void SetSmoothTurnSpeed(float speed) { }
        public float GetSmoothTurnSpeed() => 0f;

        public void Teleport(Vector3 position, Quaternion rotation, bool stillMoveAfterTeleport)
        {
            if (!IsServer)
                return;
            Body.position = position;
            Body.rotation = rotation;
            GetComponent<SampleVehicleHitDamage>()?.ResetSweepHistory();
            if (!stillMoveAfterTeleport)
            {
                StopMove();
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                _forces.Clear();
            }
            PhysicsController.ResetState();
            ++_teleportRevision;
        }

        public bool FindGroundedPosition(Vector3 fromPosition, float findDistance, out Vector3 result)
        {
            result = fromPosition;
            return false; // Keep the requested chassis height; snapping its origin buries the wheels.
        }

        public void ApplyForce(ApplyMovementForceMode mode, Vector3 direction, ApplyMovementForceSourceType sourceType,
            int sourceDataId, int sourceLevel, float force, float deceleration, float duration, bool clearForces)
        {
            if (!IsServer)
                return;
            if (clearForces)
                _forces.Clear();
            _forces.Add(new EntityMovementForceApplier().Apply(mode, direction, sourceType, sourceDataId, sourceLevel, force, deceleration, duration));
        }

        public EntityMovementForceApplier FindForceByActionKey(ApplyMovementForceSourceType sourceType, int sourceDataId) => _forces.FindBySource(sourceType, sourceDataId);
        public void ClearAllForces() { if (IsServer) _forces.Clear(); }
        public bool AllowToJump() => false;
        public bool AllowToDash() => false;
        public bool AllowToCrouch() => false;
        public bool AllowToCrawl() => false;
        public bool AllowToStand() => true;
        // Teleport revisions are server authoritative; clients apply them from movement snapshots.
        public UniTask WaitClientTeleportConfirm() => UniTask.CompletedTask;
        public bool IsWaitingClientTeleportConfirm() => false;
    }
}
