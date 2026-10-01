using LiteNetLib.Utils;
using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>Shared car/aircraft controls. Aircraft throttle is absolute, so timeout always cuts power.</summary>
    public struct SampleVehicleInput
    {
        public float steering;
        public float throttle;
        public float brakeReverse;
        public float pitch;
        public bool handbrake;
        public float yaw;

        public static SampleVehicleInput Parked => new SampleVehicleInput { handbrake = true };

        public SampleVehicleInput Sanitize()
        {
            return new SampleVehicleInput
            {
                steering = ClampFinite(steering, -1f, 1f),
                throttle = ClampFinite(throttle, 0f, 1f),
                brakeReverse = ClampFinite(brakeReverse, 0f, 1f),
                pitch = ClampFinite(pitch, -1f, 1f),
                handbrake = handbrake,
                yaw = ClampFinite(yaw, -1f, 1f),
            };
        }

        private static float ClampFinite(float value, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, min, max);

        public void Write(NetDataWriter writer)
        {
            SampleVehicleInput input = Sanitize();
            writer.Put(input.steering);
            writer.Put(input.throttle);
            writer.Put(input.brakeReverse);
            writer.Put(input.pitch);
            writer.Put(input.yaw);
            writer.Put(input.handbrake);
        }

        public static SampleVehicleInput Read(NetDataReader reader)
        {
            var input = new SampleVehicleInput
            {
                steering = reader.GetFloat(), throttle = reader.GetFloat(),
                brakeReverse = reader.GetFloat(), pitch = reader.GetFloat(),
            };
            input.yaw = reader.GetFloat();
            input.handbrake = reader.GetBool();
            return input.Sanitize();
        }
    }
}
