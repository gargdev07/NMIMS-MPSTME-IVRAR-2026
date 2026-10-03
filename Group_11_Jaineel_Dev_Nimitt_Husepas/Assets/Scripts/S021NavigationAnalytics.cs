using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using S014.MobileAR;

namespace S021.Analytics
{
    [Serializable]
    public sealed class S021SessionMetrics
    {
        public string participantId;
        public string condition;
        public float transitDurationSeconds;
        public int routeDeviationCount;
        public int wrongTurnCount;
        public float totalDistanceMeters;
        public string targetFloor;
        public float nasaTlx;
        public float sus;
    }

    /// <summary>
    /// S021-only analytics subscriber. It does not alter S014 navigation state.
    /// </summary>
    public sealed class S021NavigationAnalytics : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour navigationEventsComponent;
        [SerializeField] private MonoBehaviour routeProviderComponent;
        [SerializeField] private MonoBehaviour poseProviderComponent;
        [SerializeField] private string participantId = "MOCK-TEST";
        [SerializeField] private string condition = "MOCK";
        [SerializeField, Min(0.01f)] private float deviationThresholdMeters = 4f;

        private IS014NavigationEvents navigationEvents;
        private IS014RouteProvider routeProvider;
        private IS014PoseProvider poseProvider;
        private readonly List<S014Waypoint> route = new List<S014Waypoint>();
        private bool subscribed;
        private bool currentlyDeviating;
        private bool navigationActive;
        private int segmentStartIndex;
        private Vector3 lastPosition;
        private bool hasLastPosition;
        private float transitDurationSeconds;
        private string targetFloor = string.Empty;

        public S021SessionMetrics Metrics { get; private set; }
        public float DeviationThresholdMeters => deviationThresholdMeters;

        public void ConfigureSession(string newParticipantId, string newCondition)
        {
            participantId = string.IsNullOrWhiteSpace(newParticipantId) ? "MOCK-TEST" : newParticipantId;
            condition = string.IsNullOrWhiteSpace(newCondition) ? "MOCK" : newCondition;
            if (Metrics != null)
            {
                Metrics.participantId = participantId;
                Metrics.condition = condition;
            }
        }

        public void SetTargetFloor(string newTargetFloor)
        {
            targetFloor = newTargetFloor ?? string.Empty;
            if (Metrics != null) Metrics.targetFloor = targetFloor;
        }

        private void Awake()
        {
            navigationEvents = navigationEventsComponent as IS014NavigationEvents;
            routeProvider = routeProviderComponent as IS014RouteProvider;
            poseProvider = poseProviderComponent as IS014PoseProvider;
            Metrics = CreateMetrics();
            ReloadRoute();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!subscribed || navigationEvents == null || poseProvider == null || !IsActive()) return;
            transitDurationSeconds += Time.deltaTime;
            Vector3 position;
            Quaternion rotation;
            if (!poseProvider.TryGetPose(out position, out rotation)) return;
            if (hasLastPosition) Metrics.totalDistanceMeters += Vector3.Distance(position, lastPosition);
            lastPosition = position;
            hasLastPosition = true;

            var deviation = CalculateRouteDeviation(position);
            if (deviation > deviationThresholdMeters && !currentlyDeviating)
            {
                currentlyDeviating = true;
                Metrics.routeDeviationCount++;
                Metrics.wrongTurnCount++;
            }
            else if (deviation <= deviationThresholdMeters)
            {
                currentlyDeviating = false;
            }
            Metrics.transitDurationSeconds = transitDurationSeconds;
        }

        public void ReloadRoute()
        {
            route.Clear();
            if (routeProvider == null) return;
            var providedRoute = routeProvider.GetRoute();
            if (providedRoute == null) return;
            foreach (var waypoint in providedRoute)
            {
                if (waypoint != null) route.Add(waypoint.Copy(route.Count));
            }
            segmentStartIndex = 0;
            if (route.Count > 0) targetFloor = route[route.Count - 1].floor;
        }

        public float CalculateRouteDeviation(Vector3 position)
        {
            if (route.Count < 2) return 0f;
            var start = Mathf.Clamp(segmentStartIndex, 0, route.Count - 2);
            var segmentStart = route[start].worldPosition;
            var segmentEnd = route[start + 1].worldPosition;
            var segment = segmentEnd - segmentStart;
            var lengthSquared = segment.sqrMagnitude;
            var projection = lengthSquared <= Mathf.Epsilon
                ? segmentStart
                : segmentStart + segment * Mathf.Clamp01(Vector3.Dot(position - segmentStart, segment) / lengthSquared);
            return Vector3.Distance(position, projection);
        }

        public string ExportCsvRow(float nasaTlx, float sus)
        {
            Metrics.nasaTlx = nasaTlx;
            Metrics.sus = sus;
            return S021CsvSerializer.Serialize(Metrics);
        }

        public void SetSurveyScores(float nasaTlx, float sus)
        {
            Metrics.nasaTlx = nasaTlx;
            Metrics.sus = sus;
        }

        private void Subscribe()
        {
            if (subscribed || navigationEvents == null) return;
            navigationEvents.OnNavigationStarted += HandleNavigationStarted;
            navigationEvents.OnNavigationReset += HandleNavigationReset;
            navigationEvents.OnWaypointReached += HandleWaypointReached;
            navigationEvents.OnDestinationReached += HandleDestinationReached;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || navigationEvents == null) return;
            navigationEvents.OnNavigationStarted -= HandleNavigationStarted;
            navigationEvents.OnNavigationReset -= HandleNavigationReset;
            navigationEvents.OnWaypointReached -= HandleWaypointReached;
            navigationEvents.OnDestinationReached -= HandleDestinationReached;
            subscribed = false;
        }

        private void HandleNavigationStarted()
        {
            ResetMetrics();
            ReloadRoute();
            segmentStartIndex = 0;
            navigationActive = true;
        }

        private void HandleNavigationReset()
        {
            ResetMetrics();
            ReloadRoute();
            navigationActive = false;
        }

        private void HandleWaypointReached(S014Waypoint waypoint)
        {
            if (waypoint == null) return;
            segmentStartIndex = Mathf.Clamp(waypoint.index, 0, Mathf.Max(0, route.Count - 1));
            currentlyDeviating = false;
        }

        private void HandleDestinationReached()
        {
            Metrics.transitDurationSeconds = transitDurationSeconds;
            navigationActive = false;
        }

        private bool IsActive()
        {
            return navigationActive;
        }

        private void ResetMetrics()
        {
            Metrics = CreateMetrics();
            transitDurationSeconds = 0f;
            hasLastPosition = false;
            currentlyDeviating = false;
        }

        private S021SessionMetrics CreateMetrics()
        {
            return new S021SessionMetrics
            {
                participantId = participantId,
                condition = condition,
                targetFloor = targetFloor
            };
        }
    }

    public static class S021CsvSerializer
    {
        public const string Header = "participant_id,condition,transit_duration_seconds,route_deviation_count,wrong_turn_count,total_distance_meters,target_floor,nasa_tlx,sus";

        public static string Serialize(S021SessionMetrics metrics)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            var values = new[]
            {
                Escape(metrics.participantId),
                Escape(metrics.condition),
                metrics.transitDurationSeconds.ToString("F1", CultureInfo.InvariantCulture),
                metrics.routeDeviationCount.ToString(CultureInfo.InvariantCulture),
                metrics.wrongTurnCount.ToString(CultureInfo.InvariantCulture),
                metrics.totalDistanceMeters.ToString("F1", CultureInfo.InvariantCulture),
                Escape(metrics.targetFloor),
                metrics.nasaTlx.ToString("F1", CultureInfo.InvariantCulture),
                metrics.sus.ToString("F1", CultureInfo.InvariantCulture)
            };
            return string.Join(",", values);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            return escaped.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + escaped + "\"" : escaped;
        }
    }
}
