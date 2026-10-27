using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Legacy Unity scaffold retained for compatibility with the faculty starter files.
/// It is not the browser WebXR implementation and its Transform pose must not be
/// presented as real device tracking. The browser implementation is in index.html;
/// S014WebXRPoseAdapter is the Unity-side integration boundary.
/// 
/// Course: IVRAR (Immersive Virtual, Real & Augmented Reality) - Group 11
/// Governing Standard: Activity-Based AR Navigation (Mulloni 2011) & ISO 9241-11 Usability
/// </summary>
public class MobileWebXRRouteNavigator : MonoBehaviour
{
    [System.Serializable]
    public struct NavWaypoint
    {
        public int waypointIndex;
        public Vector3 positionBIM;
        public string landmarkInstruction;
        public bool isFloorTransition;
    }

    [Header("Session Tracking")]
    [Tooltip("Participant identifier (e.g. P01 to P50).")]
    public string participantId = "P01";

    [Tooltip("Navigation Condition: SmartKiosk_Mobile_Handoff or Traditional_Paper_Map.")]
    public string condition = "SmartKiosk_Mobile_Handoff";

    [Header("Waypoints & Guidance")]
    public List<NavWaypoint> activeRouteWaypoints = new List<NavWaypoint>();
    public int currentTargetWaypointIndex = 0;
    public float waypointArrivalThresholdMeters = 1.4f;
    public float maxAllowedDeviationMeters = 4.0f;

    [Header("Live Transit Metrics")]
    public float transitElapsedDurationSec = 0f;
    public int routeDeviationCount = 0;
    public float totalDistanceWalkedMeters = 0f;
    public bool isNavigating = false;
    public bool hasReachedDestination = false;

    private Vector3 _lastUserPos;

    // Student Technical Boundaries:
    // [S014 - Dev Garg]  Camera pose update and the floating, SLERP-smoothed arrow are implemented in S014WebXRPoseAdapter.cs
    //                    and S014DirectionArrow.cs; waypoint progress in S014NavigationController.cs.
    // [S021 - Nimitt Jain] Path deviation tracking, wrong-turn counting and CSV serialisation are implemented in
    //                    S021NavigationAnalytics.cs (see docs/S021_Navigation_Analytics.md).

    void Start()
    {
        _lastUserPos = transform.position;
    }

    void Update()
    {
        if (isNavigating && !hasReachedDestination)
        {
            transitElapsedDurationSec += Time.deltaTime;
            TrackUserDisplacement();
            EvaluateWaypointProgress();
        }
    }

    /// <summary>
    /// Measures user walked distance and checks for corridor path deviations.
    /// </summary>
    private void TrackUserDisplacement()
    {
        Vector3 currentPos = transform.position;
        float stepDist = Vector3.Distance(currentPos, _lastUserPos);
        if (stepDist > 0.1f)
        {
            totalDistanceWalkedMeters += stepDist;
            _lastUserPos = currentPos;

            // Perpendicular distance to the planned segment is computed by S021NavigationAnalytics.cs.
            if (currentTargetWaypointIndex < activeRouteWaypoints.Count)
            {
                float distToTarget = Vector3.Distance(currentPos, activeRouteWaypoints[currentTargetWaypointIndex].positionBIM);
                if (distToTarget > maxAllowedDeviationMeters && stepDist > 0.5f)
                {
                    // Potential route-finding error / wrong turn
                }
            }
        }
    }

    /// <summary>
    /// Checks proximity to current waypoint and advances guidance to next turn.
    /// </summary>
    private void EvaluateWaypointProgress()
    {
        if (currentTargetWaypointIndex >= activeRouteWaypoints.Count)
        {
            hasReachedDestination = true;
            isNavigating = false;
            Debug.Log($"[MobileWebXRRouteNavigator] Destination reached in {transitElapsedDurationSec:F1}s!");
            return;
        }

        // Arrival at a waypoint: simple radius check below; the smoothed arrow orientation is handled in S014DirectionArrow.cs.
        float dist = Vector3.Distance(transform.position, activeRouteWaypoints[currentTargetWaypointIndex].positionBIM);
        if (dist <= waypointArrivalThresholdMeters)
        {
            currentTargetWaypointIndex++;
            Debug.Log($"[MobileWebXRRouteNavigator] Waypoint reached. Next index: {currentTargetWaypointIndex}");
        }
    }

    /// <summary>
    /// Formats navigation trial telemetry as a standardized CSV row.
    /// </summary>
    public string ExportCsvRow(int targetFloor, float nasaTlx, float sus)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{participantId},{condition},{transitElapsedDurationSec:F1},");
        sb.Append($"{routeDeviationCount},{totalDistanceWalkedMeters:F1},{targetFloor},");
        sb.Append($"{nasaTlx:F1},{sus:F1}");
        return sb.ToString();
    }
}
