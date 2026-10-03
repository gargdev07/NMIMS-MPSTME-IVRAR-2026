using System;
using System.Collections.Generic;
using UnityEngine;

namespace S014.MobileAR
{
    public class S014NavigationController : MonoBehaviour, IS014NavigationEvents
    {
        [SerializeField] private MonoBehaviour routeProviderComponent;
        [SerializeField] private MonoBehaviour poseProviderComponent;
        [SerializeField] private S014DirectionArrow directionArrow;
        [SerializeField] private S014FloorTransitionIndicator floorTransitionIndicator;
        [SerializeField, Range(0.25f, 1.49f)] private float waypointArrivalRadius = 1.4f;
        [SerializeField] private bool startOnEnable;
        private readonly List<S014Waypoint> route = new List<S014Waypoint>();
        private IS014RouteProvider routeProvider;
        private IS014PoseProvider poseProvider;
        private IS014PoseDrivenGuidance arrowGuidance;
        private IS014PoseDrivenGuidance floorGuidance;
        private int currentIndex = -1;
        private bool waypointEventRaised;

        public event Action OnNavigationStarted;
        public event Action OnNavigationReset;
        public event Action<S014Waypoint> OnWaypointReached;
        public event Action<S014Waypoint> OnFloorTransition;
        public event Action OnDestinationReached;
        public IReadOnlyList<S014Waypoint> Route => route;
        public S014NavigationState State { get; private set; } = S014NavigationState.Idle;
        public int CurrentWaypointIndex => currentIndex;
        public S014Waypoint CurrentWaypoint => currentIndex >= 0 && currentIndex < route.Count ? route[currentIndex] : null;
        public float CurrentDistanceMeters { get; private set; }
        public float WaypointArrivalRadius => waypointArrivalRadius;
        public bool HasReachedDestination => State == S014NavigationState.Complete;

        private void Awake()
        {
            routeProvider = routeProviderComponent as IS014RouteProvider;
            poseProvider = poseProviderComponent as IS014PoseProvider;
            arrowGuidance = directionArrow as IS014PoseDrivenGuidance;
            floorGuidance = floorTransitionIndicator as IS014PoseDrivenGuidance;
            if (poseProvider == null) poseProvider = GetComponent<IS014PoseProvider>();
        }

        private void OnEnable() { if (startOnEnable) StartNavigation(); }

        private void Update()
        {
            if (State != S014NavigationState.Active || CurrentWaypoint == null || poseProvider == null) return;
            Vector3 position; Quaternion rotation;
            if (!poseProvider.TryGetPose(out position, out rotation)) return;
            CurrentDistanceMeters = Vector3.Distance(position, CurrentWaypoint.worldPosition);
            arrowGuidance?.SetPose(position, rotation);
            floorGuidance?.SetPose(position, rotation);
            if (!waypointEventRaised && CurrentDistanceMeters <= waypointArrivalRadius) AdvanceWaypoint();
        }

        public bool InitializeRoute()
        {
            if (routeProvider == null) routeProvider = routeProviderComponent as IS014RouteProvider;
            if (routeProvider == null) { Debug.LogWarning("S014 navigation has no route provider."); return false; }
            return LoadRoute(routeProvider.GetRoute());
        }

        public bool LoadRoute(IEnumerable<S014Waypoint> waypoints)
        {
            route.Clear();
            if (waypoints == null) { ResetRouteState(); Debug.LogWarning("S014 route cannot be loaded from null data."); return false; }
            foreach (var waypoint in waypoints)
            {
                if (waypoint == null) continue;
                route.Add(waypoint.Copy(route.Count));
            }
            ResetRouteState();
            SetGuidance();
            return route.Count > 0;
        }

        public bool ReplaceRoute(IEnumerable<S014Waypoint> waypoints)
        {
            var loaded = LoadRoute(waypoints);
            OnNavigationReset?.Invoke();
            return loaded;
        }

        public void StartNavigation()
        {
            if (route.Count == 0 && !InitializeRoute()) return;
            if (State == S014NavigationState.Complete)
            {
                ResetRouteState();
                SetGuidance();
            }
            if (currentIndex < 0) { Debug.LogWarning("S014 cannot start without a valid waypoint."); return; }
            State = S014NavigationState.Active;
            waypointEventRaised = false;
            SetGuidance();
            OnNavigationStarted?.Invoke();
        }

        public void PauseNavigation() { if (State == S014NavigationState.Active) State = S014NavigationState.Paused; }
        public void ResumeNavigation() { if (State == S014NavigationState.Paused) State = S014NavigationState.Active; }

        public void ResetNavigation()
        {
            ResetRouteState();
            SetGuidance();
            OnNavigationReset?.Invoke();
        }

        private void ResetRouteState()
        {
            State = S014NavigationState.Idle;
            currentIndex = route.Count > 0 ? 0 : -1;
            CurrentDistanceMeters = 0f;
            waypointEventRaised = false;
        }

        private void AdvanceWaypoint()
        {
            waypointEventRaised = true;
            var reached = CurrentWaypoint;
            OnWaypointReached?.Invoke(reached);
            if (reached.isFloorTransition) OnFloorTransition?.Invoke(reached);
            currentIndex++;
            if (currentIndex >= route.Count)
            {
                State = S014NavigationState.Complete;
                currentIndex = route.Count - 1;
                directionArrow?.ClearTarget();
                floorTransitionIndicator?.Hide();
                OnDestinationReached?.Invoke();
                return;
            }
            waypointEventRaised = false;
            SetGuidance();
        }

        private void SetGuidance()
        {
            var target = CurrentWaypoint;
            if (directionArrow != null)
            {
                if (target == null || State == S014NavigationState.Complete) directionArrow.ClearTarget();
                else directionArrow.SetTarget(target);
            }
            if (floorTransitionIndicator != null)
            {
                if (target != null && target.isFloorTransition && State != S014NavigationState.Complete) floorTransitionIndicator.Show(target);
                else floorTransitionIndicator.Hide();
            }
        }
    }
}
