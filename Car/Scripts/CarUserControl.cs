using System;
using UnityEngine;
using CrossPlatformInputManager = Insthync.CameraAndInput.InputManager;

namespace UnityStandardAssets.Vehicles.Car
{
    [RequireComponent(typeof (CarController))]
    public class CarUserControl : MonoBehaviour
    {
        private CarController m_Car; // the car controller we want to use


        private void Awake()
        {
            // get the car controller
            m_Car = GetComponent<CarController>();
        }


        private void FixedUpdate()
        {
            // pass the input to the car!
            float h = CrossPlatformInputManager.GetAxis("Horizontal", false);
            float v = CrossPlatformInputManager.GetAxis("Vertical", false);
#if !MOBILE_INPUT
            float handbrake = CrossPlatformInputManager.GetAxis("Jump", false);
            m_Car.Move(h, v, v, handbrake);
#else
            m_Car.Move(h, v, v, 0f);
#endif
        }
    }
}
