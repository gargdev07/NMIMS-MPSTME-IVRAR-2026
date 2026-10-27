using UnityEngine;
using UnityEngine.XR;

namespace S014.CampusVR
{
    /// <summary>
    /// Thin wrapper over the XR InputDevices feature API, matching the input
    /// convention already used by S014QuestHeadsetWayfinding (the project runs
    /// the New Input System only, so UnityEngine.Input is unavailable).
    /// </summary>
    public static class S014VRInput
    {
        public static Vector2 LeftStick()
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (d.isValid && d.TryGetFeatureValue(CommonUsages.primary2DAxis, out var a)) return a;
            return Vector2.zero;
        }

        public static Vector2 RightStick()
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (d.isValid && d.TryGetFeatureValue(CommonUsages.primary2DAxis, out var a)) return a;
            return Vector2.zero;
        }

        public static bool RightTrigger()
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!d.isValid) return false;
            if (d.TryGetFeatureValue(CommonUsages.triggerButton, out var b)) return b;
            if (d.TryGetFeatureValue(CommonUsages.trigger, out var a)) return a > 0.7f;
            return false;
        }

        /// <summary>Left X button (primary). Used to bring the kiosk to the visitor.</summary>
        public static bool LeftPrimary()
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            return d.isValid && d.TryGetFeatureValue(CommonUsages.primaryButton, out var b) && b;
        }

        /// <summary>Left Y button (secondary). Used for RESET back to the main gate.</summary>
        public static bool LeftSecondary()
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            return d.isValid && d.TryGetFeatureValue(CommonUsages.secondaryButton, out var b) && b;
        }

        public static bool TryRightPose(Transform root, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (d.isValid &&
                d.TryGetFeatureValue(CommonUsages.devicePosition, out var p) &&
                d.TryGetFeatureValue(CommonUsages.deviceRotation, out var r))
            {
                if (root != null) { pos = root.TransformPoint(p); rot = root.rotation * r; }
                else { pos = p; rot = r; }
                return true;
            }
            return false;
        }

        public static bool ControllerConnected()
        {
            return InputDevices.GetDeviceAtXRNode(XRNode.RightHand).isValid ||
                   InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).isValid;
        }
    }
}
