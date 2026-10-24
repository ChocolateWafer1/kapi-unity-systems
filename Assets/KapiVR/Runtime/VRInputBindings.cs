using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using CommonUsages = UnityEngine.InputSystem.CommonUsages;
using InputDevice = UnityEngine.InputSystem.InputDevice;
using InputDevices = UnityEngine.XR.InputDevices;

namespace KapiVR
{
    /// <summary>Which tracked part of the player a pose follows.</summary>
    public enum VRPoseSource
    {
        Head,
        LeftHand,
        RightHand,
        LeftAim,
        RightAim
    }

    public enum VRHand
    {
        Left,
        Right
    }

    /// <summary>
    /// Input System bindings for the headset and controllers. These are generic XR bindings, so they work with
    /// any OpenXR headset: Quest (standalone or Link), Index, Vive, and so on.
    /// </summary>
    public static class VRInputBindings
    {
        public static string DevicePath(VRPoseSource source)
        {
            switch (source)
            {
                case VRPoseSource.Head: return "<XRHMD>";
                case VRPoseSource.LeftHand:
                case VRPoseSource.LeftAim: return "<XRController>{LeftHand}";
                default: return "<XRController>{RightHand}";
            }
        }

        public static string DevicePath(VRHand hand) => hand == VRHand.Left ? "<XRController>{LeftHand}" : "<XRController>{RightHand}";

        /// <summary>
        /// Points a TrackedPoseDriver at the headset or a controller. Hands use the grip pose (where the controller
        /// is held); aims use the pointer pose (the direction the controller points, which lasers should follow).
        /// </summary>
        public static void Configure(TrackedPoseDriver driver, VRPoseSource source)
        {
            string device = DevicePath(source);
            string position;
            string rotation;
            switch (source)
            {
                case VRPoseSource.Head:
                    position = "/centerEyePosition";
                    rotation = "/centerEyeRotation";
                    break;
                case VRPoseSource.LeftAim:
                case VRPoseSource.RightAim:
                    position = "/pointerPosition";
                    rotation = "/pointerRotation";
                    break;
                default:
                    position = "/devicePosition";
                    rotation = "/deviceRotation";
                    break;
            }

            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.positionInput = new InputActionProperty(new InputAction($"{source} Position", InputActionType.Value, device + position, expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction($"{source} Rotation", InputActionType.Value, device + rotation, expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(new InputAction($"{source} Tracking State", InputActionType.Value, device + "/trackingState", expectedControlType: "Integer"));
        }

        public static bool IsConfigured(TrackedPoseDriver driver)
        {
            return HasBindings(driver.positionInput) && HasBindings(driver.rotationInput);
        }

        static bool HasBindings(InputActionProperty property)
        {
            var action = property.action;
            return action != null && action.bindings.Count > 0;
        }

        /// <summary>True while the controller for this hand is connected and tracked.</summary>
        public static bool IsTracked(VRHand hand)
        {
            // OpenXR can list more than one controller per hand (one per controller profile), so check them all.
            var usage = hand == VRHand.Left ? CommonUsages.LeftHand : CommonUsages.RightHand;
            foreach (var device in InputSystem.devices)
            {
                if (device is XRController controller && controller.added && HasUsage(controller, usage) && IsTracked(controller))
                    return true;
            }

            // Fall back to the XR input subsystem's view of the controller.
            var node = InputDevices.GetDeviceAtXRNode(hand == VRHand.Left ? XRNode.LeftHand : XRNode.RightHand);
            return node.isValid && node.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked;
        }

        /// <summary>The controller the Input System sees for this hand (a tracked one if there are several), or null.</summary>
        public static XRController FindController(VRHand hand)
        {
            var usage = hand == VRHand.Left ? CommonUsages.LeftHand : CommonUsages.RightHand;
            XRController found = null;
            foreach (var device in InputSystem.devices)
            {
                if (!(device is XRController controller) || !controller.added || !HasUsage(controller, usage)) continue;
                if (IsTracked(controller)) return controller;
                if (found == null) found = controller;
            }
            return found;
        }

        static bool IsTracked(XRController controller)
        {
            return (controller.isTracked != null && controller.isTracked.isPressed)
                || (controller.trackingState != null && (controller.trackingState.ReadValue() & 3) != 0);
        }

        static bool HasUsage(InputDevice device, InternedString usage)
        {
            foreach (var deviceUsage in device.usages)
                if (deviceUsage == usage) return true;
            return false;
        }
    }
}
