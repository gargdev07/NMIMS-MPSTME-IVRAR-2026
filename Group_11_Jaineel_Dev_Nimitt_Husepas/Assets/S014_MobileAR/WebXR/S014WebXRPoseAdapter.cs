using UnityEngine;

namespace S014.MobileAR
{
    /// <summary>
    /// Unity-side boundary for a future transport carrying browser WebXR poses.
    /// Editor demo mode intentionally continues to use S014TransformPoseProvider.
    /// </summary>
    public sealed class S014WebXRPoseAdapter : MonoBehaviour, IS014PoseProvider
    {
        private Vector3 position;
        private Quaternion rotation = Quaternion.identity;
        private bool tracking;

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
