# S014 Mobile AR Navigation

## Runtime architecture

- `S014Waypoint` is the route data model. `Copy` lets the controller own a normalized read-only route snapshot without mutating provider data.
- `IS014RouteProvider` supplies ordered waypoints. R057 can later provide an adapter without changing navigation logic.
- `IS014PoseProvider` supplies the current world position and rotation. The Unity kiosk uses `S014TransformPoseProvider` as a virtual demo camera; it is not the phone camera or a physical campus pose.
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

`S014CampusRouteProvider` reads `WebXR/route-data.json` from the master scene's serialized `TextAsset` reference and runs weighted shortest-path search over `campusPrototypeGraph` for every catalog destination. The outdoor graph is sampled from the imported paved-surface mesh and filters edges against measured building bounds; links across disconnected surface islands and unmarked building entrances remain inferred. The representative interior is inside the imported `Building_23990` shell and appears when Labs 101–103 or Student Help is selected. Other destinations end at distinct exterior approaches.

A future WebXR bridge must implement `IS014PoseProvider`, perform browser-to-Unity communication, and handle coordinate-origin alignment and tracking lifecycle. The current transform provider is not WebXR.

The phone client is `WebXR/index.html`. It uses the bundled Three.js runtime to display the browser-optimized glTF campus in virtual 3D, parses the QR handoff, and follows the route data in `WebXR/route-data.json`. This is a separate browser application: Unity does not automatically execute or render its scene in a phone browser. Immersive AR remains optional and is not required for virtual navigation.

## Final integrated scene

`Assets/Scenes/Campus_Wayfinding_Master.unity` is the presentation scene. It preserves the imported `Unity2Skfb` campus environment and adds:

- `R057DestinationCatalog`, with 22 categorized prototype destinations.
- `R057NavigationSession`, which creates a unique session and JSON handoff payload.
- `R057QRCodeHandoff`, which creates a QR image containing the deployed page's `payload` query contract: percent-encoded standard Base64 JSON with session, route, destination, floor, and version fields. The local encoder supports through QR version 12-L for the longest catalogue URL.
- `S014CampusRouteProvider`, which loads the campus graph asset assigned in `Campus_Wayfinding_Master.unity`, constructs the selected destination's route from the campus entry approach, and exposes its distance and estimated walking time.
- A world-space `LineRenderer` and route markers, which show the selected end-to-end route over the imported campus in the Unity scene. The camera is positioned at the route start when the destination is selected and moves along the graph during the demo.
- `S014MasterDemoController`, which provides the Editor/Demo kiosk and navigation flow.
- `S021NavigationAnalytics`, which records deviation excursions, wrong turns, distance, duration, floor transitions, completion, and CSV export.
- `J032SpatialTelemetry.S021SpatialTelemetry`, which exposes sampled position, waypoint, floor, state, and deviation records.

The final Unity scene is a development/presentation build. Its deterministic pose movement is explicitly labelled Editor/Demo Pose and is not participant evidence.

In Editor/Demo mode, `S014MasterDemoController` moves and rotates `DemoUserPose` through the real Unity `Update` loop, follows it with the Main Camera at human height, and updates the visible route markers. The Main Camera is therefore a simulated mobile viewpoint, not a fixed campus overview. The route coordinates remain prototype coordinates and are not claimed to be calibrated to the physical MPSTME building.

The Unity scene and local browser client both read the same 22 destination IDs and the `campusPrototypeGraph`. Each route starts at the inferred south campus entry, follows the imported walk-surface graph, and ends at its own spatially unique approach. Labs 101–103 terminate at distinct first-floor room doors inside the host-building cutaway and use the mapped stairs. The Dean’s Office ends at an inferred south-side approach to Building_001; a Unity mesh ray from this approach toward geometry entrance 003 found no wall. Weighted Dijkstra minimizes graph distance in meters. Building roles and unmarked path connectors remain inferred because the imported asset has no semantic room labels or surveyed outdoor navigation data.

Every catalog destination receives a phone handoff session. Prototype interior visibility follows the selected destination, so exterior-only destinations do not fail session creation and do not hide unrelated imported campus structures. Route rebuilds collect and remove saved route lines and waypoint markers from earlier selections before drawing the new route.

## Available campus geometry coverage audit

The imported `Unity2Skfb.gltf` is a visual glTF asset (3,539 nodes, 2,976 meshes, 1,325 materials, and a 109,833,886-byte source geometry buffer). The mobile GLB is an optimized visual copy (95 nodes and 95 meshes). These counts describe render geometry, not navigable surfaces. The master scene serializes `NavMeshSettings` with `m_NavMeshData` set to null; the scene also contains six `MeshCollider` components, but those do not provide a verified, site-wide connected walkable surface or named floor-plan semantics.

| Coverage class | Current evidence and scope |
|---|---|
| Geometry-tested in local virtual browser | Lab 101, Lab 104, Cafeteria, and Parking traversed to their graph endpoints with browser model collision checks enabled. This is evidence for those test runs only, not surveyed or campus-wide walkability. |
| Graph-connected, not individually traversed | The other 18 catalogue routes pass deterministic connectivity, endpoint, floor, transition, and Unity-catalogue parity checks. No per-route browser arrival test was performed for them in this validation pass. |
| Representative indoor preview | The cutaway still contains illustrative endpoints for indoor catalogue items, but the main route provider no longer selects them. Their room labels and positions are not verified against source floor plans. |
| Campus destination approaches | All 22 catalogue items have distinct terminal positions reached through shared internal path connectors and building-specific approaches. These use inferred connectors; the imported visual asset does not supply authoritative outdoor path polylines or establish them as safe, continuously walkable connections. |
| Not mappable from current sources | Full-campus walkable paths between buildings, exact room-level placement, and verified indoor circulation outside the representative cutaway. No authoritative site route data, floor plans, named traversable surface map, or surveyed destination coordinates are included. |

`WebXR/route-data.json` records three source-mesh AABB projections and the six generic entrance transforms checked in Unity Editor. The glTF import mirrors source X and the active `Unity2Skfb` root transform is `(-23.904045, 2.77, -15.1)` with identity rotation and unit scale. The route data and browser model apply that same reflection and root transform. The scene's `S014CampusRouteProvider` now loads that graph directly. Its connectors are inferred and have not been verified as safe or continuously walkable. The campus graph is a route overlay, not a NavMesh or surveyed route network.

Unity Editor inspection found 2,976 renderers with world bounds `(-107.48, -20.86, -138.87)` to `(102.72, 18.27, 58.64)`. The two enabled environment colliders are large `MeshCollider` grids; they do not encode named rooms or a proven pedestrian network. The three building boxes in the plan are transformed AABBs for source geometry groups, not ground-level building footprints.

At Play Mode start and after destination selection, the Unity demo pose is placed at the selected route's campus-entry approach. During campus navigation it follows the graph in world coordinates rather than requiring the scene's unconnected colliders to behave like a NavMesh. This is a virtual route overlay, not a GPS position or a verified pedestrian surface network.

The active `S014MasterDemoController` kiosk renders destination selection through its IMGUI `OnGUI` path, with category/search filters and six destination entries per page inside a scroll view. `R057DestinationScrollList` is a separate helper class and is not instantiated by this controller. The Start control is disabled until the selected route, session service, and pose are ready.

The S021 admin analytics view is separate from the visitor navigation flow. Export creates or appends `Application.persistentDataPath/S021/navigation_sessions.csv`, includes a header for an empty export, appends completed sessions only, and refuses to append the same session twice. Survey score columns remain empty unless scores were explicitly supplied. The participant ID and movement in the Editor/Demo pose are simulated, not research results.

## Capability status

- **IMPLEMENTED:** Master scene, categorized destination catalogue, search/filter UI, route summary, session payload, QR image handoff, Unity campus graph loading, distinct route construction for all catalog destinations, route-plan overlay, visible world-space campus route, campus-entry camera start, waypoint progression, frame-driven camera follow, guidance, S021 metrics, local CSV export, and reset/restart flow. The outdoor links and endpoints remain inferred and have not been surveyed.
- **BROWSER IMPLEMENTED:** The phone page loads an optimized copy of the existing campus glTF, validates the QR payload, and uses the campus graph by default. All 22 campus endpoints are spatially unique and connected by an internal path network with inferred branches to building entrances and outdoor features. Campus motion checks the named large building bounds and tries a geometry-checked detour when one is hit. The optimized GLB joins meshes and has no walkable-surface data, so small props and unnamed structures are not exhaustively classified; outdoor connectors remain inferred. The representative interior remains an optional preview in `WebXR/representative-interior.js`.
- **BROWSER ALIGNMENT:** The optimized GLB receives the Unity import's X reflection and active scene-root transform, checked against six live Unity entrance transforms and the renderer bounds. Details are recorded in `WebXR/campus/model-info.json` and `WebXR/route-data.json`.
- **ROUTE TESTS:** Twenty-two Node tests pass, including checks that the master Unity scene references the route-data asset, all 22 campus endpoints are unique, the internal graph uses distance-weighted shortest routes, arrival and pose-sampled distance handling include the start and endpoint, and WebXR tap handling remains covered. The generated `Assembly-CSharp.csproj` builds with zero warnings and errors. Unity’s live provider constructed all 22 routes from the loaded master scene; the campus connectors and facility labels remain inferred and are not proof of surveyed or safe walkability.
- **NOT VERIFIED:** Physical-phone rendering, a real QR scan, and an actual immersive AR session have not been observed in this environment. The deployed HTTPS page and its route/model assets are checked separately from physical-device behavior.
- **INTEGRATION-READY:** R057 payload/session handoff to a hosted browser URL; S014 WebXR pose transport through `S014WebXRPoseAdapter`; J032 consumption of `S021SpatialTelemetrySample`.
- **NOT IMPLEMENTED:** Browser-to-Unity pose transport, production kiosk backend, authentication, participant capture UI, and research result generation.
- **PHONE QR STATUS:** `R057QRCodeHandoff` encodes `https://gargdev07.github.io/NMIMS-MPSTME-IVRAR-2026/webxr/index.html`. GitHub Pages now serves the current WebXR client, campus route catalogue, and optimized campus model at that address. A phone scan and device-level immersive AR still need confirmation on a physical phone.

## Environment attribution

The campus environment is the existing imported `Unity2Skfb` Sketchfab asset. Preserve its source license and attribution metadata when distributing the project. Destination labels are prototype mappings unless the imported model provides a verified matching room.

## Current prototype additions

The live kiosk status screen now uses a compact bottom panel on narrow displays, with Reset Session and Export CSV kept in its fixed footer during active navigation. The phone page has a secure-context and WebXR capability check; the virtual 3D route remains usable when immersive AR is unsupported or cannot start, and the fallback is labeled virtual rather than immersive.

The route plan and asset evidence are recorded in `S014_Campus_Route_Plan.md`; the top-down overlay is `S014_Campus_Floor_Plan.svg`. The six geometry entry transforms, three large building meshes, four house meshes, and visible internal path network ground the graph. Building boxes are axis-aligned bounds projections rather than surveyed footprints. Because the GLTF has no room names or destination associations, facility labels and most gate positions are inferred candidate locations.

The phone page can open an immersive AR campus preview on compatible HTTPS browsers. It places a scaled model for preview only; placement is illustrative, it is not aligned to a surveyed campus origin, and it does not stream poses back to Unity. Virtual navigation remains available without XR.
