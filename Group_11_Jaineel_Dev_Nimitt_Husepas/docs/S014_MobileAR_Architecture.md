# S014 Mobile AR Navigation

## Runtime architecture

- `S014Waypoint` is the route data model. `Copy` lets the controller own a normalized read-only route snapshot without mutating provider data.
- `IS014RouteProvider` supplies ordered waypoints. R057 can later provide an adapter without changing navigation logic.
- `IS014PoseProvider` supplies the current world position and rotation. The current `S014TransformPoseProvider` is a test provider; real browser WebXR is not implemented.
- `IS014PoseDrivenGuidance` receives generic pose samples. Direction and floor guidance no longer depend on the concrete transform provider.
- `S014NavigationController` owns route loading/replacement, state, 1.4 m arrival detection, progression, duplicate-event protection, and navigation events.
- `S014DirectionArrow` and `S014FloorTransitionIndicator` render guidance only.
- `S014NavigationOverlay` is developer/test UI only.

## Data flow

```text
Route provider -> S014NavigationController.LoadRoute/ReplaceRoute
Pose provider -> controller.Update -> pose-driven guidance
Controller arrival check -> waypoint/floor/destination events
```

The controller publishes `OnNavigationStarted`, `OnNavigationReset`, `OnWaypointReached`, `OnFloorTransition`, and `OnDestinationReached`. S021 and J032 can subscribe without modifying the controller.

## Mock/test setup

`S014MockRouteProvider` is a deterministic development fixture with five waypoints, a direction change, one floor transition, and a destination. `S014_Navigation_Test.unity` uses wrapper components only where Unity scene serialization requires stable single-class scripts.

The effective arrival radius remains 1.4 metres and is constrained below 1.5 metres by the controller inspector range. Arrival detection remains controller-owned.

## Integration boundaries

R057 should implement a thin `IS014RouteProvider` adapter and call `ReplaceRoute` with kiosk-derived route data. No kiosk UI or route-generation logic belongs in S014.

A future WebXR bridge must implement `IS014PoseProvider`, perform browser-to-Unity communication, and handle coordinate-origin alignment and tracking lifecycle. The current transform provider is not WebXR.
