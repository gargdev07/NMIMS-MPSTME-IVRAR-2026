using UnityEngine;

namespace S014.MobileAR
{
    /// <summary>
    /// Integration boundary for browser-provided WebXR poses.
    /// A host bridge must call SetPose with a pose expressed in Unity world coordinates.
    /// </summary>
    public sealed class S014WebXRPoseAdapter : MonoBehaviour, IS014PoseProvider
    {
        [SerializeField] private bool tracking;
        private Vector3 position;
        private Quaternion rotation = Quaternion.identity;

        public bool IsTracking => tracking;

        public bool TryGetPose(out Vector3 currentPosition, out Quaternion currentRotation)
        {
            currentPosition = position;
            currentRotation = rotation;
            return tracking;
        }

        public void SetPose(Vector3 newPosition, Quaternion newRotation)
        {
            position = newPosition;
            rotation = newRotation;
            tracking = true;
        }

        public void SetTrackingLost()
        {
            tracking = false;
        }
    }
}
