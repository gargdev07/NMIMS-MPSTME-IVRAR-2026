# S014 Mobile AR Navigation

## Runtime architecture

- `S014Waypoint` is the route data model. `Copy` lets the controller own a normalized read-only route snapshot without mutating provider data.
- `IS014RouteProvider` supplies ordered waypoints. R057 can later provide an adapter without changing navigation logic.
- `IS014PoseProvider` supplies the current world position and rotation. `S014TransformPoseProvider` remains a test provider. `S014WebXRPoseAdapter` is the Unity-side integration boundary for a host that has converted browser WebXR poses into Unity coordinates.
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

## Capability status

- **IMPLEMENTED:** Browser-side `immersive-ar` session request, `local-floor` reference space, tracked viewer pose reads, quaternion smoothing, 1.4 m waypoint arrival, floor-transition and destination events.
- **TESTED:** The browser source has been syntax-checked and the Unity S014 mock route remains covered by the existing deterministic scene validation.
- **MOCKED:** The browser route is deterministic development data until R057 supplies a handoff payload.
- **INTEGRATION-READY:** `s014-webxr-pose` browser events and `S014WebXRPoseAdapter` define the pose handoff boundary.
- **NOT-YET-IMPLEMENTED:** Browser-to-Unity transport, coordinate-origin alignment, kiosk payload ingestion, and production device validation.

The browser implementation is truthful about WebXR support: it refuses to start when `navigator.xr` or `immersive-ar` is unavailable and never substitutes Transform data for browser tracking.

`MobileWebXRRouteNavigator.cs` is retained as an unconnected faculty scaffold for compatibility. It is not used by the browser path and must not be described as a WebXR pose provider.
