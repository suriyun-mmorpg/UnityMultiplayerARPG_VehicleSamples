using Insthync.CameraAndInput;
using UnityEngine;

namespace MultiplayerARPG
{
    public class SampleVehiclePlayerController : BaseVehiclePlayerController
    {
        [Header("Kit input bindings")]
        [SerializeField] private string _steeringAxis = "Horizontal";
        [SerializeField] private string _pedalAxis = "Vertical";
        [Tooltip("Optional separate 0..1 pedal axes; leave empty to use positive/negative Vertical.")]
        [SerializeField] private string _throttleAxis;
        [SerializeField] private string _brakeReverseAxis;
        [SerializeField] private string _handbrakeButton = "Jump";
        private SampleVehicleEntityMovement _movement;
        private SampleVehicleInput _mobileInput;
        private bool _useMobileInput;

        protected override void OnActivated()
        {
            _movement = Vehicle.Entity.GetComponent<SampleVehicleEntityMovement>();
            _useMobileInput = false;
            _mobileInput = default;
            if (_movement == null)
                Debug.LogError("Sample vehicle controls require SampleVehicleEntityMovement on the vehicle root.", this);
            ResetInput();
        }

        protected override void UpdateControls(float deltaTime)
        {
            if (!CanDrive || _movement == null)
                return;
            float pedals = ReadAxis(_pedalAxis);
            SampleVehicleInput input = _useMobileInput ? _mobileInput : new SampleVehicleInput
            {
                steering = ReadAxis(_steeringAxis),
                throttle = string.IsNullOrEmpty(_throttleAxis) ? Mathf.Max(0f, pedals) : ReadAxis(_throttleAxis),
                brakeReverse = string.IsNullOrEmpty(_brakeReverseAxis) ? Mathf.Max(0f, -pedals) : ReadAxis(_brakeReverseAxis),
                handbrake = !string.IsNullOrEmpty(_handbrakeButton) && InputManager.GetButton(_handbrakeButton),
                pitch = ReadPitch(),
            };
            _movement.SetInput(input);
        }

        private static float ReadAxis(string name) => string.IsNullOrEmpty(name) ? 0f : InputManager.GetAxis(name, false);
        protected virtual float ReadPitch() => 0f;

        protected override void ResetInput()
        {
            _mobileInput = default;
            if (_movement != null && CanDrive)
                _movement.SetInput(SampleVehicleInput.Parked);
        }

        protected override void OnDeactivated() { _movement = null; _useMobileInput = false; }

        // Suitable for EventTrigger/slider UnityEvents. Pointer-up must send zero/false.
        public void SetSteering(float value) { _useMobileInput = true; _mobileInput.steering = value; }
        public void SetThrottle(float value) { _useMobileInput = true; _mobileInput.throttle = value; }
        public void SetBrakeReverse(float value) { _useMobileInput = true; _mobileInput.brakeReverse = value; }
        public void SetHandbrake(bool value) { _useMobileInput = true; _mobileInput.handbrake = value; }
        public void SetPitch(float value) { _useMobileInput = true; _mobileInput.pitch = value; }
        public void UseBoundInputs() { _useMobileInput = false; _mobileInput = default; }
    }
}
