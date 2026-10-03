using UnityEngine;

namespace S014.MobileAR
{
    public class S014TransformPoseProvider : MonoBehaviour, IS014PoseProvider
    {
        [SerializeField] private Transform poseTransform;
        public Transform PoseTransform => poseTransform != null ? poseTransform : transform;
        public bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            var source = PoseTransform;
            if (source == null) { position = default; rotation = Quaternion.identity; return false; }
            position = source.position;
            rotation = source.rotation;
            return true;
        }
    }
}
