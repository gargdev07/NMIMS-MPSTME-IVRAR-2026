using System;
using UnityEngine;
using S014.MobileAR;

namespace J032.Telemetry
{
    [Serializable]
    public struct S021SpatialTelemetrySample
    {
        public string sessionId;
        public string timestamp;
        public Vector3 position;
        public int waypointIndex;
        public string floor;
        public float routeDeviationMeters;
        public S014NavigationState navigationState;
    }

    public sealed class S021SpatialTelemetry : MonoBehaviour
    {
        [SerializeField] private S014NavigationController navigationController;
        [SerializeField] private MonoBehaviour poseProviderComponent;
        [SerializeField, Min(0.05f)] private float sampleIntervalSeconds = 0.25f;
        private IS014PoseProvider poseProvider;
        private float nextSampleTime;
        public S021SpatialTelemetrySample LatestSample { get; private set; }
        public event Action<S021SpatialTelemetrySample> Sampled;

        public void SetSessionId(string sessionId)
        {
            var sample = LatestSample;
            sample.sessionId = sessionId;
            LatestSample = sample;
        }

        private void Awake()
        {
            poseProvider = poseProviderComponent as IS014PoseProvider;
        }

        private void Update()
        {
            if (navigationController == null || poseProvider == null || Time.time < nextSampleTime) return;
            nextSampleTime = Time.time + sampleIntervalSeconds;
            Vector3 position;
            Quaternion rotation;
            if (!poseProvider.TryGetPose(out position, out rotation)) return;
            var waypoint = navigationController.CurrentWaypoint;
            var sample = LatestSample;
            sample.timestamp = DateTime.Now.ToString("o");
            sample.position = position;
            sample.waypointIndex = waypoint == null ? -1 : waypoint.index;
            sample.floor = waypoint == null ? string.Empty : waypoint.floor;
            sample.navigationState = navigationController.State;
            sample.routeDeviationMeters = Vector3.Distance(position, waypoint == null ? position : waypoint.worldPosition);
            LatestSample = sample;
            Sampled?.Invoke(sample);
        }
    }
}
