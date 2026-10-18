# Campus floor and route plan

## Asset review

The route source is the imported `node_Grid_002_27472` paved surface, with 2,353 triangles from the `Sandypath_001` and `Light_sandPath_001` submeshes. The campus asset also contains the central circular `GlassHouseModel`, `Building_001`, `Building_North`, the prototype host shell `Building_23990`, and `House_base_000` through `House_base_003`. The model names and shapes do not identify most building functions.

`WebXR/campus-building-obstacles.json` stores projected triangles exported from the eight loaded Unity `MeshFilter` meshes. The outdoor graph checks those shapes with 0.3 m edge clearance. This replaces the old large rectangular building barriers that hid real gaps and produced a 221 m perimeter shortcut. The only doorway mask is around `entrance003` on `Building_001`; its routes were ray-checked against the live Unity mesh. The route graph samples the paved surface on a 1.5 m lattice, connects its four local directions (eight neighbors), and uses Dijkstra with each edge's 3D length as its weight.

The Unity master scene also disables 105 detached imported window renderers whose centers were more than 2 m from every building mesh's renderer bounds. The remaining 176 window renderers stay enabled. This cleanup is saved as scene-instance overrides; the source GLTF asset is unchanged.

The current graph has 2,172 nodes and 6,746 edges. Its campus start is `(61.996, 2.6, -68)`, near the modeled south-east entrance. It is an inferred demo start, not a surveyed public gate. All 22 catalog terminals have distinct positions and a route from that start.

## Destination assignments

| Destination | Asset area and approach | Evidence and limits |
| --- | --- | --- |
| Auditorium | Central circular `GlassHouseModel` | Function and approach inferred from the central building's form. |
| Labs 101–103 | First-floor rooms in the prototype building | Existing room-door transforms and stair route. This is the only multi-floor interior route. |
| Student Help Desk | Prototype ground-floor service point | Representative demo service point, not a real room label from the asset. |
| Lab 104 and First Aid | Separate approaches to `House_base_000` | Both roles are inferred; endpoints remain unique. First Aid ends at `(36.019, 2.6, -82.494)`, 0.58 m from the projected facade triangles and connected to the adjacent paved graph node. |
| Classrooms, Seminar Hall, Dean's Office, Faculty Offices, Administration | Five distinct points along the outside approach at the `Building_001` `entrance003` gate | Room functions are inferred. The generic model has no room names. Endpoints are at the gate approach, rather than in the building footprint. |
| Cafeteria | Former Library approach to `House_base_002` at `(-3, 2.6, -53.54)` | Swapped with the Library route as requested. Cafeteria function remains inferred. |
| Library | Former Cafeteria approach to `House_base_001` at `(19.5, 2.6, -43.5)` | Swapped with the Cafeteria route as requested. Library function remains inferred. |
| Reception | West facade of `House_base_002` at `(-18.006, 2.6, -59.981)` | A separate modeled building from Visitor Reception, Washrooms, and First Aid. It is 0.47 m from the projected facade triangles and connected to the adjacent paved graph node. The role and gate are inferred; this asset has no named entrance here. |
| Visitor Reception | Central south/front-gate approach of `House_base_001` at `(22.506, 2.6, -40.519)` | A separate modeled building from Reception, Washrooms, and First Aid. It is 0.69 m from the projected facade triangles and connected to the adjacent paved graph node. The role and exact doorway are inferred; this asset has no named entrance marker. |
| Washrooms | South facade of `House_base_003` at `(55.506, 2.6, -31.519)` | A separate modeled building from Reception, Visitor Reception, and First Aid. It is 0.61 m from the projected facade triangles and connected to the adjacent paved graph node. The role and gate are inferred; this asset has no named entrance here. |
| Student Activity Area, Drinking Water, Main Entrance, Security Desk | Modeled outdoor court or entrance-path features | Outdoor destinations; positions derive from path geometry or the existing campus entrance markers. |
| Parking | Paved path endpoint at `(70.5, 2.6, -105)` | The asset contains no parking lot or parking-bay geometry. This is labeled as an inferred parking approach, not verified parking. |

These four destinations now terminate on unique, reachable paved-graph approaches beside four separate imported building shells. Their coordinates were selected from the projected mesh triangles rather than the broader renderer bounds; each pin lies 0.47–0.69 m from its building's projected facade and has a 0.02 m graph connector to the adjacent paved node. Visitor Reception is placed at the central south-facing front-gate area. The asset has no semantic labels or named doors for these structures, so their functions and exact door locations are explicit inferences; the route ends at the exterior approach and does not claim verified interior access.

## Route rules and checks

- Every exterior catalog route starts at the campus approach and is the weighted shortest path in the imported walkable graph. Distance uses the route segments, not the straight-line distance.
- The graph follows the paved campus mesh in all sampled directions. It does not route across projected building interiors to join path islands, and the former 221 m off-path perimeter connector is rejected.
- The Parking route ends at `(70.5, 2.6, -105)` on the mapped paved network, outside all eight projected building meshes. The asset contains no parking lot or parking-bay geometry, so the route represents an inferred outdoor parking approach.
- Unity added temporary mesh colliders to the eight loaded building models and checked each exterior route segment at several heights from ground level to 4.2 m. The latest audit found no building-mesh intersections for the 18 exterior destinations. Temporary colliders were removed, and the Unity selection was restored to Lab 101.
- `node --test Assets/S014_MobileAR/WebXR/route-graph.test.js` checks all destinations, distinct terminals, shortest-path reconstruction, mesh-derived obstacle clearance, destination assignments, and the mobile handoff/WebXR behaviors.

The route line is the shortest route available on the imported path surface plus short destination approaches. It is not a surveyed pedestrian map. The model does not establish legal public access, the real location of offices, or the real-world existence of the named facilities. Verify those facts on site before using the prototype for public wayfinding.
