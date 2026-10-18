using System;
using System.Collections.Generic;
using UnityEngine;

namespace S014.MobileAR
{
    public interface IS014RouteProvider { IReadOnlyList<S014Waypoint> GetRoute(); }
    public interface IS014PoseProvider { bool TryGetPose(out Vector3 position, out Quaternion rotation); }
    public interface IS014NavigationGuidance { void SetTarget(S014Waypoint waypoint); void ClearTarget(); }
    public interface IS014PoseDrivenGuidance { void SetPose(Vector3 position, Quaternion rotation); }
    public enum S014NavigationState { Idle, Active, Paused, Complete }
    public interface IS014NavigationEvents
    {
        event Action OnNavigationStarted;
        event Action OnNavigationReset;
        event Action<S014Waypoint> OnWaypointReached;
        event Action<S014Waypoint> OnFloorTransition;
        event Action OnDestinationReached;
    }
}
