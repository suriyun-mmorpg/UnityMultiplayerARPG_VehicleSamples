using UnityEngine;

namespace MultiplayerARPG
{
    public class SampleVehicleDemoInstructions : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 490, 120), "Vehicle Samples — airfield");
            GUI.Label(new Rect(24, 38, 470, 90), "Use Activate to enter | ExitVehicle: leave | CameraRotate: look\n" +
                "Car: W/S gas, brake/reverse | A/D steering | Space handbrake\n" +
                "Aircraft: A/D roll | W/S nose down/up | , / . yaw left/right\n" +
                "Hold Shift/Ctrl: increase/decrease throttle | Space: brakes\n" +
                "Take off along the runway. Damaged vehicles respawn after destruction.");
        }
    }
}
