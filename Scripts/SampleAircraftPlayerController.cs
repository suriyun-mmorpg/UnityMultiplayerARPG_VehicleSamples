using Insthync.CameraAndInput;
using UnityEngine;

namespace MultiplayerARPG
{
    public sealed class SampleAircraftPlayerController : BaseVehiclePlayerController
    {
        [SerializeField] private string _rollAxis = "Horizontal";
        [SerializeField] private string _pitchAxis = "Vertical";
        [SerializeField] private string _yawAxis;
        [SerializeField] private string _throttleChangeAxis;
        [SerializeField] private string _airbrakeButton = "Jump";
        [SerializeField, Min(0f)] private float _throttleChangeSpeed = 0.3f;
        private SampleVehicleEntityMovement _movement;
        private SampleVehicleInput _input;
        private bool _mobile;

        protected override void OnActivated()
        {
            _movement = Vehicle.Entity.GetComponent<SampleVehicleEntityMovement>();
            _mobile = false;
            ResetInput();
        }

        protected override void UpdateControls(float deltaTime)
        {
            if (!CanDrive || _movement == null) return;
            if (!_mobile)
            {
                _input.steering = Axis(_rollAxis);
                // W pitches down; S pulls the nose up, matching the sample's pitch torque.
                _input.pitch = Axis(_pitchAxis);
                float yaw = 0f, throttleChange = 0f;
                if (InputManager.IsUseNonMobileInput())
                {
#if ENABLE_INPUT_SYSTEM
                    var keyboard = UnityEngine.InputSystem.Keyboard.current;
                    if (keyboard != null)
                    {
                        yaw = (keyboard.periodKey.isPressed ? 1f : 0f) - (keyboard.commaKey.isPressed ? 1f : 0f);
                        throttleChange = (keyboard.leftShiftKey.isPressed ? 1f : 0f) - (keyboard.leftCtrlKey.isPressed ? 1f : 0f);
                    }
#elif ENABLE_LEGACY_INPUT_MANAGER
                    yaw = (Input.GetKey(KeyCode.Period) ? 1f : 0f) - (Input.GetKey(KeyCode.Comma) ? 1f : 0f);
                    throttleChange = (Input.GetKey(KeyCode.LeftShift) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftControl) ? 1f : 0f);
#endif
                }
                _input.yaw = string.IsNullOrEmpty(_yawAxis) ? yaw : Axis(_yawAxis);
                throttleChange = string.IsNullOrEmpty(_throttleChangeAxis) ? throttleChange : Axis(_throttleChangeAxis);
                _input.throttle = Mathf.Clamp01(_input.throttle + throttleChange * _throttleChangeSpeed * deltaTime);
                _input.handbrake = !string.IsNullOrEmpty(_airbrakeButton) && InputManager.GetButton(_airbrakeButton);
            }
            _movement.SetInput(_input);
        }
        private static float Axis(string name) => string.IsNullOrEmpty(name) ? 0f : InputManager.GetAxis(name, false);
        protected override void ResetInput()
        {
            _input = default;
            if (_movement != null && CanDrive) _movement.SetInput(SampleVehicleInput.Parked);
        }
        protected override void OnDeactivated() { _movement = null; _mobile = false; }
        public void SetRoll(float value) { _mobile = true; _input.steering = value; }
        public void SetPitch(float value) { _mobile = true; _input.pitch = value; }
        public void SetYaw(float value) { _mobile = true; _input.yaw = value; }
        public void SetThrottle(float value) { _mobile = true; _input.throttle = value; }
        public void SetAirbrakes(bool value) { _mobile = true; _input.handbrake = value; }
        public void UseBoundInputs() { _mobile = false; ResetInput(); }
    }
}
