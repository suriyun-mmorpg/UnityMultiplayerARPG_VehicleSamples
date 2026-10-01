using System.Linq;
using LiteNetLib.Utils;
using UnityEngine;
using UnityStandardAssets.Vehicles.Car;
using UnityStandardAssets.Vehicles.Aeroplane;

namespace MultiplayerARPG
{
    /// <summary>Physics and presentation bridge for the original Standard Assets samples.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SampleVehiclePhysics : MonoBehaviour
    {
        public CarController Car { get; private set; }
        public AeroplaneController Aircraft { get; private set; }
        public Transform[] Wheels { get; private set; }
        public Rigidbody Body { get; private set; }
        private WheelCollider[] _colliders;
        public bool IsGrounded => _colliders.Any(wheel => wheel.enabled && wheel.isGrounded);

        public void Initialize()
        {
            if (Body != null) return;
            Body = GetComponent<Rigidbody>();
            Car = GetComponent<CarController>();
            Aircraft = GetComponent<AeroplaneController>();
            if ((Car == null) == (Aircraft == null))
                throw new System.InvalidOperationException("Vehicle requires exactly one CarController or AeroplaneController.");
            if (Car != null) Car.Initialize();
            if (Aircraft != null) Aircraft.Initialize();
            Wheels = Car != null ? Car.WheelMeshes.Select(wheel => wheel.transform).ToArray() : new Transform[0];
            _colliders = GetComponentsInChildren<WheelCollider>(true);
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
                if (component is CarUserControl || component is CarAIControl || component is CarSelfRighting ||
                    component is AeroplaneUserControl2Axis || component is AeroplaneUserControl4Axis || component is AeroplaneAiControl)
                    component.enabled = false;
        }

        public void SetSimulation(bool simulate)
        {
            Initialize();
            if (!simulate && !Body.isKinematic) Body.velocity = Body.angularVelocity = Vector3.zero;
            Body.isKinematic = !simulate;
            foreach (var wheel in _colliders) wheel.enabled = simulate;
        }

        public void ResetState()
        {
            if (Car != null) Car.ResetNetworkState();
            if (Aircraft != null) Aircraft.ResetNetworkState();
        }

        public void Simulate(SampleVehicleInput input)
        {
            if (Car != null) Car.Move(input.steering, input.throttle, -input.brakeReverse, input.handbrake ? 1f : 0f);
            else Aircraft.MoveWithThrottle(input.steering, input.pitch, input.yaw, input.throttle, input.handbrake);
        }

        public SampleVehicleTelemetry Capture() => Car != null
            ? new SampleVehicleTelemetry { speed = Car.CurrentSpeed, power = Car.Revs, throttle = Car.AccelInput,
                brake = Car.BrakeInput, steering = Car.CurrentSteerAngle }
            : new SampleVehicleTelemetry { speed = Aircraft.ForwardSpeed, throttle = Aircraft.Throttle,
                power = Aircraft.GetComponent<LandingGear>()?.NetworkState ?? 1,
                altitude = Aircraft.Altitude, steering = Aircraft.RollInput, pitch = Aircraft.PitchInput,
                yaw = Aircraft.YawInput, brake = Aircraft.AirBrakes ? 1f : 0f };

        public void ApplyTelemetry(SampleVehicleTelemetry data, Vector3 velocity)
        {
            if (Car != null) Car.ApplyNetworkTelemetry(data.speed, data.power, data.throttle, data.brake, data.steering);
            else
            {
                Aircraft.ApplyNetworkTelemetry(data.throttle, data.speed, data.altitude, data.steering, data.pitch, data.yaw, data.brake > 0f, velocity);
                Aircraft.GetComponent<LandingGear>()?.ApplyNetworkState(Mathf.RoundToInt(data.power));
            }
        }
    }

    public struct SampleVehicleTelemetry
    {
        public float speed, power, throttle, brake, steering, altitude, pitch, yaw;
        public void Write(NetDataWriter writer)
        {
            writer.Put(speed); writer.Put(power); writer.Put(throttle); writer.Put(brake);
            writer.Put(steering); writer.Put(altitude); writer.Put(pitch); writer.Put(yaw);
        }
        public static SampleVehicleTelemetry Read(NetDataReader reader) => new SampleVehicleTelemetry
        {
            speed = reader.GetFloat(), power = reader.GetFloat(), throttle = reader.GetFloat(), brake = reader.GetFloat(),
            steering = reader.GetFloat(), altitude = reader.GetFloat(), pitch = reader.GetFloat(), yaw = reader.GetFloat(),
        };
    }
}
