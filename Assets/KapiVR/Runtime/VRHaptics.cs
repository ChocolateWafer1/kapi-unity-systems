using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace KapiVR
{
    /// <summary>Controller vibration that works across OpenXR headsets.</summary>
    public static class VRHaptics
    {
        public static void Pulse(VRHand hand, float amplitude, float duration)
        {
            amplitude = Mathf.Clamp01(amplitude);
            if (amplitude <= 0f || duration <= 0f) return;

            // OpenXR controllers show up in the Input System as rumble-capable controllers.
            var controller = hand == VRHand.Left ? XRController.leftHand : XRController.rightHand;
            if (controller is XRControllerWithRumble rumble)
            {
                rumble.SendImpulse(amplitude, duration);
                return;
            }

            // Fallback for XR plug-ins that only expose haptics through the XR input subsystem.
            var device = InputDevices.GetDeviceAtXRNode(hand == VRHand.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.isValid && device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0, amplitude, duration);
        }
    }
}
