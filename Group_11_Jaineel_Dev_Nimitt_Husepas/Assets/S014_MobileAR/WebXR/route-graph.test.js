import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { auditDestinationRoutes, findShortestPath, validateNavigationGraph } from "./route-graph.js";
import { checkImmersiveArSupport, requestImmersiveArSession } from "./xr-support.js";

const routeData = JSON.parse(await readFile(new URL("./route-data.json", import.meta.url), "utf8"));
const walkableSurface = JSON.parse(await readFile(new URL("./walkable-surface.json", import.meta.url), "utf8"));
const buildingMeshes = JSON.parse(await readFile(new URL("./campus-building-obstacles.json", import.meta.url), "utf8")).meshes;
const catalogSource = await readFile(
  new URL("../Integrated/R057DestinationCatalog.cs", import.meta.url),
  "utf8"
);
const campusProviderSource = await readFile(
  new URL("../Integrated/S014CampusRouteProvider.cs", import.meta.url),
  "utf8"
);
const masterSceneSource = await readFile(
  new URL("../../Scenes/Campus_Wayfinding_Master.unity", import.meta.url),
  "utf8"
);
const analyticsSource = await readFile(
  new URL("../Integrated/S021NavigationAnalytics.cs", import.meta.url),
  "utf8"
);
const demoControllerSource = await readFile(
  new URL("../Integrated/S014MasterDemoController.cs", import.meta.url),
  "utf8"
);
const phoneClientSource = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
const deployedWebXrFiles = ["index.html", "virtual-campus.js", "route-data.json", "route-graph.js", "xr-support.js", "representative-interior.js", "styles.css", "campus-building-obstacles.json"];
const navigationSessionSource = await readFile(
  new URL("../Integrated/R057NavigationSession.cs", import.meta.url),
  "utf8"
);
const qrHandoffSource = await readFile(
  new URL("../Integrated/R057QRCodeHandoff.cs", import.meta.url),
  "utf8"
);

const doorwayPortals = [
  { building: "Building_001", minX: 40, maxX: 50, minZ: 15, maxZ: 24 }
];
const mappedPrototypeInteriorSegments = new Set([
  "prototype-ground-threshold->prototype-ground-corridor",
  "prototype-ground-corridor->prototype-stair-bottom",
  "prototype-stair-bottom->prototype-stair-mid",
  "prototype-stair-mid->prototype-stair-top",
  "prototype-stair-top->prototype-upper-corridor",
  "prototype-upper-corridor->campus-destination-lab-101",
  "prototype-upper-corridor->campus-destination-lab-102",
  "prototype-upper-corridor->campus-destination-lab-103",
  "prototype-ground-corridor->campus-destination-student-help"
]);
const projectedMeshBins = new Map();
for (const mesh of buildingMeshes) for (const triangle of mesh.triangles) {
  for (let x = Math.floor(Math.min(triangle[0], triangle[2], triangle[4])); x <= Math.floor(Math.max(triangle[0], triangle[2], triangle[4])); x++) {
    for (let z = Math.floor(Math.min(triangle[1], triangle[3], triangle[5])); z <= Math.floor(Math.max(triangle[1], triangle[3], triangle[5])); z++) {
      const key = `${x},${z}`;
      const list = projectedMeshBins.get(key) ?? [];
      list.push({ id: mesh.id, triangle });
      projectedMeshBins.set(key, list);
    }
  }
}

function meshIdIncludes(id, buildingId) {
  return id.includes(buildingId);
}

function pointInProjectedTriangle(x, z, t) {
  const [ax, az, bx, bz, cx, cz] = t;
  const d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
  if (Math.abs(d) < 1e-9) return false;
  const a = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d;
  const b = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d;
  const c = 1 - a - b;
  return a >= -1e-6 && b >= -1e-6 && c >= -1e-6;
}

function pointSegmentDistance(x, z, ax, az, bx, bz) {
  const dx = bx - ax, dz = bz - az, denominator = dx * dx + dz * dz;
  const t = denominator > 0 ? Math.max(0, Math.min(1, ((x - ax) * dx + (z - az) * dz) / denominator)) : 0;
  return Math.hypot(x - (ax + t * dx), z - (az + t * dz));
}

function intersectsBuildingMesh(point, margin = 0.3) {
  const x = point[0], z = point[2];
  for (const { id, triangle } of projectedMeshBins.get(`${Math.floor(x)},${Math.floor(z)}`) ?? []) {
    const buildingId = ["Building_001", "Building_North", "Building_23990", "GlassHouseModel", "House_base_000", "House_base_001", "House_base_002", "House_base_003"]
      .find(candidate => meshIdIncludes(id, candidate));
    if (doorwayPortals.some(g => g.building === buildingId && x >= g.minX && x <= g.maxX && z >= g.minZ && z <= g.maxZ)) continue;
    if (pointInProjectedTriangle(x, z, triangle) || (margin > 0 && Math.min(
      pointSegmentDistance(x, z, triangle[0], triangle[1], triangle[2], triangle[3]),
      pointSegmentDistance(x, z, triangle[2], triangle[3], triangle[4], triangle[5]),
      pointSegmentDistance(x, z, triangle[4], triangle[5], triangle[0], triangle[1])) < margin)) return true;
  }
  const prototypeBounds = routeData.campusPrototypeGraph.obstacleBounds.find(b => b.id === "Building_23990");
  if (buildingMeshes.some(mesh => mesh.id.includes("Building_23990"))) return false;
  return point[0] > prototypeBounds.minX - prototypeBounds.clearance - margin &&
    point[0] < prototypeBounds.maxX + prototypeBounds.clearance + margin &&
    point[2] > prototypeBounds.minZ - prototypeBounds.clearance - margin &&
    point[2] < prototypeBounds.maxZ + prototypeBounds.clearance + margin;
}

function firstUnapprovedRouteObstruction(route) {
  for (let index = 1; index < route.waypoints.length; index++) {
    const from = route.waypoints[index - 1], to = route.waypoints[index];
    const mappedPrototypeInterior = mappedPrototypeInteriorSegments.has(`${from.id}->${to.id}`) ||
      (from.id.startsWith("prototype-stair-") && to.id.startsWith("prototype-stair-"));
    const a = from.position, b = to.position;
    const length = Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
    const samples = Math.max(1, Math.ceil(length / 0.25));
    for (let sample = 1; sample <= samples; sample++) {
      const t = sample / samples;
      const x = a[0] + (b[0] - a[0]) * t;
      const z = a[2] + (b[2] - a[2]) * t;
      for (const { id, triangle } of projectedMeshBins.get(`${Math.floor(x)},${Math.floor(z)}`) ?? []) {
        if (id.includes("Building_001") && x >= 40 && x <= 50 && z >= 15 && z <= 24) continue;
        if (mappedPrototypeInterior && id.includes("Building_23990")) continue;
        if (pointInProjectedTriangle(x, z, triangle) || Math.min(
          pointSegmentDistance(x, z, triangle[0], triangle[1], triangle[2], triangle[3]),
          pointSegmentDistance(x, z, triangle[2], triangle[3], triangle[4], triangle[5]),
          pointSegmentDistance(x, z, triangle[4], triangle[5], triangle[0], triangle[1])) <= 0.3) {
          return `${from.id} -> ${to.id} intersects ${id} at (${x.toFixed(1)}, ${z.toFixed(1)})`;
        }
      }
    }
  }
  return null;
}

test("the Unity master scene loads the whole-campus graph for its actual route provider", () => {
  assert.match(masterSceneSource, /campusRouteData: \{fileID: 4900000, guid: 06cbbf3fdc68e734cafca7b45ecaf55b, type: 3\}/);
  assert.match(campusProviderSource, /JsonUtility\.FromJson<CampusRouteData>\(campusRouteData\.text\)/);
  assert.match(campusProviderSource, /TryFindShortestPath\(data\.campusPrototypeGraph\.startNodeId, destinationNodeId/);
  assert.doesNotMatch(campusProviderSource, /representativeInterior\.TryBuildRoute/);
  assert.match(masterSceneSource, /showRouteMarkers: 1/);
});

test("GitHub Pages serves the same phone client, shortest-path graph and AR runtime as the Unity source", async () => {
  const deployedRoot = new URL("../../../../docs/webxr/", import.meta.url);
  for (const file of deployedWebXrFiles) {
    const source = await readFile(new URL(`./${file}`, import.meta.url));
    const deployed = await readFile(new URL(file, deployedRoot));
    assert.deepEqual(deployed, source, `Hosted WebXR asset ${file} must match the Unity source copy.`);
  }
});

test("whole-campus routes use the imported walk surface and distinct exterior approaches", () => {
  const graph = routeData.campusPrototypeGraph;
  const nodes = validateNavigationGraph(graph);
  assert.equal(graph.routingAlgorithm, "dijkstra-euclidean-edge-distance");
  assert.equal(graph.startNodeId, "campusApproach");
  assert.deepEqual(nodes.get("campusApproach").position, [61.995955, 2.6, -68]);
  assert.match(graph.routeBasis, /imported Unity model walk-surface mesh/i);
  assert.equal(walkableSurface.triangles.length, 2353);
  assert.ok(nodes.size > 1500);
  assert.ok(graph.edges.some(edge => edge.kind === "imported-paved-path"));
  assert.ok(graph.edges.some(edge => edge.kind === "inferred-ground-approach-connector"));
  assert.ok(graph.edges.filter(edge => edge.kind.includes("inferred"))
    .every(edge => {
      const a = nodes.get(edge.from).position, b = nodes.get(edge.to).position;
      return Math.hypot(a[0] - b[0], a[2] - b[2]) <= 7.1;
    }), "Inferred links must stay short and local.");

  const endpoints = new Set();
  const routeLengths = new Map();
  for (const destination of routeData.destinations) {
    const endpoint = nodes.get("campus-destination-" + destination.id);
    assert.ok(endpoint, "missing campus approach for " + destination.id);
    assert.equal(endpoint.destinationId, destination.id);
    assert.ok(!endpoints.has(endpoint.position.join(",")), "campus approach is shared by " + destination.id);
    endpoints.add(endpoint.position.join(","));
    const route = findShortestPath(graph, graph.startNodeId, endpoint.id);
    assert.equal(route.nodeIds[0], graph.startNodeId);
    assert.equal(route.nodeIds.at(-1), endpoint.id);
    routeLengths.set(destination.id, route.lengthMeters);
  }
  assert.equal(endpoints.size, 22);
  assert.ok(routeLengths.get("deans-office") < 250);
  assert.ok(routeLengths.get("library") < 200);
  assert.ok(nodes.get("campus-destination-lab-101").campusBuilding.includes("Prototype building"));
  assert.ok(nodes.get("campus-destination-lab-102").campusBuilding.includes("Prototype building"));
  assert.ok(nodes.get("campus-destination-lab-103").campusBuilding.includes("Prototype building"));
  assert.match(nodes.get("campus-destination-deans-office").label, /inferred .* approach/i);
  assert.equal(nodes.get("campus-destination-deans-office").floor, "Ground");
  assert.equal(nodes.get("campus-destination-lab-101").floor, "First");
});

test("demo arrival reaches its waypoint and analytics includes the start and final movement samples", () => {
  assert.match(demoControllerSource, /remainingDistance <= navigator\.WaypointArrivalRadius\s*\?\s*target\.worldPosition/);
  assert.match(demoControllerSource, /Application\.isPlaying\) Destroy\(generatedObject\);\s*else DestroyImmediate\(generatedObject\);/);
  const started = analyticsSource.slice(analyticsSource.indexOf("private void HandleStarted()"),
    analyticsSource.indexOf("private void HandleReset()"));
  assert.match(started, /poseProvider\.TryGetPose/);
  assert.match(started, /lastPosition = position/);
  const completed = analyticsSource.slice(analyticsSource.indexOf("private void HandleDestination()"),
    analyticsSource.indexOf("private S021SessionMetrics NewMetrics()"));
  assert.match(completed, /Metrics\.totalDistanceMeters \+= Vector3\.Distance\(position, lastPosition\)/);
});

test("validates the explicit graph and every destination route", () => {
  const graphNodes = validateNavigationGraph(routeData.navigationGraph);
  assert.equal(graphNodes.size, routeData.navigationGraph.nodes.length);
  for (const [id, position] of Object.entries(routeData.nodes)) {
    assert.deepEqual(graphNodes.get(id)?.position, position);
  }
  assert.equal(routeData.destinations.length, 22);
  const audit = auditDestinationRoutes(routeData);
  assert.equal(audit.length, 22);
  assert.ok(audit.every(route => route.lengthMeters > 0 && route.nodeIds.length >= 2));
  assert.ok(audit.every(route => route.coverage === "inferred-campus-approach"));
});

test("outdoor waypoints and edges clear the projected imported building meshes", () => {
  const graphNodes = validateNavigationGraph(routeData.campusPrototypeGraph);
  const graph = routeData.campusPrototypeGraph;
  assert.match(graphNodes.get("campusApproach").label, /campus entrance start/i);
  assert.equal(graph.obstacleBounds.length, 8);
  assert.ok(graph.nodes.filter(node => node.kind === "geometry-derived-walk-surface").length > 1000);
  assert.equal(new Set(routeData.destinations.map(destination =>
    graphNodes.get(destination.campusNodeId).position.join(","))).size, routeData.destinations.length,
  "Every campus destination must have a distinct spatial endpoint.");
  const outdoor = node => !node.kind.startsWith("prototype-");
  for (const node of graph.nodes.filter(outdoor)) {
    assert.equal(intersectsBuildingMesh(node.position), false, `Outdoor node ${node.id} intersects an imported building mesh.`);
  }
  for (const edge of graph.edges.filter(edge =>
    !graphNodes.get(edge.from).kind.startsWith("prototype-") &&
    !graphNodes.get(edge.to).kind.startsWith("prototype-"))) {
    const a = graphNodes.get(edge.from).position;
    const b = graphNodes.get(edge.to).position;
    const samples = Math.max(2, Math.ceil(Math.hypot(a[0] - b[0], a[2] - b[2]) / 0.2));
    for (let sample = 1; sample < samples; sample++) {
      const t = sample / samples;
      const point = [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
      assert.equal(intersectsBuildingMesh(point), false, `Outdoor edge ${edge.id} crosses an imported building mesh.`);
    }
  }

  const transitionEdges = graph.edges.filter(edge => edge.floorTransition);
  assert.equal(transitionEdges.length, 1);
  assert.equal(transitionEdges[0].kind, "stair");
  for (const destination of routeData.destinations) {
    const node = graphNodes.get(destination.campusNodeId);
    assert.equal(node?.destinationId, destination.id);
    assert.equal(node?.floor, destination.campusFloor);
    if (!["lab-101", "lab-102", "lab-103", "student-help"].includes(destination.id)) {
      assert.equal(node?.floor, "Ground", "Non-prototype destinations end outdoors at ground level.");
      assert.match(node.label, /inferred/i);
    }
    assert.ok(routeData.campusPrototypeGraph.edges.some(edge => edge.to === destination.campusNodeId &&
      edge.from === destination.campusAnchorId));
    const route = findShortestPath(
      routeData.campusPrototypeGraph,
      routeData.campusPrototypeGraph.startNodeId,
      destination.campusNodeId
    );
    assert.equal(route.destinationId, destination.id);
    assert.equal(route.transitionCount, destination.campusFloor === "First" ? 1 : 0);
    assert.ok(route.lengthMeters > 0);
    if (!(["lab-101", "lab-102", "lab-103", "student-help"].includes(destination.id))) {
      assert.match(node.label, /inferred/i);
    }
    if (["lab-101", "lab-102", "lab-103", "student-help"].includes(destination.id)) {
      assert.match(node.campusBuilding, /Prototype building/);
    }
  }

  const destinationRoutes = routeData.destinations.map(destination => findShortestPath(
    routeData.campusPrototypeGraph,
    routeData.campusPrototypeGraph.startNodeId,
    destination.campusNodeId
  ));
  assert.ok(destinationRoutes.every(route => route.waypoints.length > 1));
});

test("every mobile destination route clears building geometry and prototype entry uses mapped interior waypoints", () => {
  const prototypeInteriorIds = new Set(["lab-101", "lab-102", "lab-103", "student-help"]);
  for (const destination of routeData.destinations) {
    const route = findShortestPath(
      routeData.campusPrototypeGraph,
      routeData.campusPrototypeGraph.startNodeId,
      destination.campusNodeId
    );
    assert.equal(route.destinationId, destination.id);
    assert.equal(route.waypoints.at(-1).id, destination.campusNodeId);
    assert.equal(firstUnapprovedRouteObstruction(route), null,
      `Mobile route for ${destination.id} must not cross an imported building wall.`);
    if (prototypeInteriorIds.has(destination.id)) {
      assert.ok(route.waypoints.some(point => point.id === "prototype-ground-threshold"),
        `${destination.id} must enter through the mapped prototype doorway.`);
      assert.ok(route.waypoints.some(point => point.id === "prototype-ground-corridor"),
        `${destination.id} must follow the mapped prototype corridor.`);
    }
  }
  assert.match(phoneClientSource, /MAPPED_PROTOTYPE_INTERIOR_SEGMENTS/);
  assert.match(phoneClientSource, /allowMappedPrototypeInterior && candidate\.id\.includes\("Building_23990"\)/);
  assert.match(phoneClientSource, /isMappedPrototypeInteriorSegment\(previousWaypoint, target\)/);
  assert.match(phoneClientSource, /Campus route · prototype interior mapped/);
});

test("the lab route follows each modeled stair tread instead of cutting across the stair flights", () => {
  const graph = routeData.campusPrototypeGraph;
  const nodes = validateNavigationGraph(graph);
  const expectedTreads = [
    ...Array.from({ length: 8 }, (_, index) => `prototype-stair-ground-tread-${String(index + 1).padStart(2, "0")}`),
    ...Array.from({ length: 8 }, (_, index) => `prototype-stair-upper-tread-${String(index + 1).padStart(2, "0")}`)
  ];
  for (const id of expectedTreads) {
    assert.equal(nodes.get(id)?.kind, "prototype-stair-tread-approach", `Missing stair tread waypoint ${id}`);
  }
  for (const destinationId of ["lab-101", "lab-102", "lab-103"]) {
    const destination = routeData.destinations.find(item => item.id === destinationId);
    const route = findShortestPath(graph, graph.startNodeId, destination.campusNodeId);
    assert.ok(expectedTreads.every(id => route.nodeIds.includes(id)), `${destinationId} must follow all modeled treads`);
    for (let index = 1; index < route.nodeIds.length; index++) {
      const from = nodes.get(route.nodeIds[index - 1]);
      const to = nodes.get(route.nodeIds[index]);
      assert.ok(graph.edges.some(edge => edge.walkable &&
        ((edge.from === from.id && edge.to === to.id) || (edge.from === to.id && edge.to === from.id))),
        `Route segment ${from.id} -> ${to.id} must be an encoded walkable edge`);
    }
  }
});

test("facility assignments use building entrances and parking ends on the modeled path", () => {
  const endpoint = id => routeData.campusPrototypeGraph.nodes.find(node => node.destinationId === id);
  assert.match(endpoint("classrooms").campusBuilding, /Building_001.*academic section/i);
  assert.match(endpoint("seminar-hall").campusBuilding, /Building_001.*seminar/i);
  assert.match(endpoint("deans-office").campusBuilding, /Building_001.*entrance003/i);
  assert.match(endpoint("faculty-offices").campusBuilding, /Building_001.*entrance003/i);
  assert.match(endpoint("cafeteria").campusBuilding, /House_base_002.*role swap/i);
  assert.match(endpoint("library").campusBuilding, /House_base_001.*role swap/i);
  assert.deepEqual(endpoint("cafeteria").position, [-3, 2.6, -53.54]);
  assert.deepEqual(endpoint("library").position, [19.5, 2.6, -43.5]);
  assert.match(endpoint("visitor-reception").campusBuilding, /House_base_001/i);
  assert.deepEqual(endpoint("visitor-reception").position, [22.506061038915526, 2.6, -40.51905948077111]);
  assert.equal(intersectsBuildingMesh(endpoint("faculty-offices").position), false);
  assert.equal(intersectsBuildingMesh(endpoint("visitor-reception").position), false);
  const allTerminals = routeData.destinations.map(destination => endpoint(destination.id).position.join(","));
  assert.equal(new Set(allTerminals).size, routeData.destinations.length, "Every catalog destination must have a unique spatial endpoint.");
  assert.deepEqual(endpoint("parking").position, [70.5, 2.6, -105]);
  assert.match(endpoint("parking").label, /no modeled parking lot or parking bays/i);
  assert.equal(intersectsBuildingMesh(endpoint("parking").position), false);
  assert.ok(routeData.campusPrototypeGraph.nodes.some(node => node.kind === "geometry-derived-walk-surface" &&
    node.position[0] === 70.5 && node.position[2] === -105), "Parking arrival must meet the mapped paved network.");
});

test("phone QR routes select campus coverage before the representative cutaway", async () => {
  const script = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  assert.match(script, /let routeMode = "campus"/);
  assert.match(script, /USE REPRESENTATIVE CUTAWAY ROUTES \(OPTIONAL\)/);
});

test("the special prototype is placed inside the Unity campus and its browser layout matches", async () => {
  const origin = [-56.5, 2.5, -49.2454];
  assert.deepEqual(routeData.integrationOrigin, origin);
  assert.deepEqual(routeData.representativeInterior.origin, origin);
  assert.match(masterSceneSource, /m_Name: Representative_Academic_Interior_Reveal[\s\S]{0,600}m_LocalPosition: \{x: -56\.5, y: 2\.5, z: -49\.2454\}/);
  const graphNodes = validateNavigationGraph(routeData.campusPrototypeGraph);
  assert.deepEqual(graphNodes.get("prototype-exterior-approach").position, [-58, 2.5, -58.2454]);
  assert.deepEqual(graphNodes.get("prototype-ground-threshold").position, [-58, 2.5, -54.2454]);
  assert.ok(graphNodes.get("prototype-exterior-approach").position[2] < -55.88,
    "The prototype's outdoor approach must remain outside the host building bounds.");
  assert.ok(routeData.campusPrototypeGraph.edges.some(edge => edge.to === "prototype-exterior-approach" &&
    edge.kind === "short-inferred-prototype-gate-connector"));
  const campusScript = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  assert.match(campusScript, /gltf\.scene\.scale\.set\(-1, 1, 1\)/);
  assert.match(campusScript, /gltf\.scene\.position\.set\(\.\.\.unityTransform\.position\)/);
  assert.match(campusScript, /Only Labs 101–103 and the representative help desk have prototype interior routes/);
});

test("every campus destination can create a phone session; only prototype destinations reveal the interior", () => {
  assert.doesNotMatch(navigationSessionSource, /!destination\.hasPrototypeEndpoint/);
  assert.doesNotMatch(demoControllerSource, /!definition\.hasPrototypeEndpoint/);
  assert.doesNotMatch(qrHandoffSource, /TryGetPrototypeEndpoint/);
  assert.match(demoControllerSource, /showPrototypeInterior\s*=\s*definition\.id\s*==\s*"lab-101"/);
  assert.match(demoControllerSource, /interiorRevealSwitch\.SetRevealMode\(showPrototypeInterior\)/);
  const prototypeOnly = routeData.destinations.filter(destination =>
    ["lab-101", "lab-102", "lab-103", "student-help"].includes(destination.id));
  assert.equal(prototypeOnly.length, 4);
  assert.ok(routeData.destinations.every(destination => destination.campusNodeId));
});

test("stale serialized route markers are removed before drawing a selected destination", () => {
  assert.match(demoControllerSource, /CollectPersistedRouteVisuals\(\)/);
  assert.match(demoControllerSource, /Resources\.FindObjectsOfTypeAll<GameObject>\(\)/);
  assert.match(demoControllerSource, /candidate\.name == "CampusWideRouteLine"/);
  assert.match(demoControllerSource, /candidate\.name\.StartsWith\("RouteWaypoint_"/);
  assert.match(masterSceneSource, /m_Name: InteriorGeometry_RepresentativeOnly[\s\S]{0,180}m_IsActive: 1/);
  assert.match(masterSceneSource, /additionalExteriorRenderers: \[\]/);
  assert.match(demoControllerSource, /routeIndex != 0 && routeIndex != route\.Count - 1 && !waypoint\.isFloorTransition/);
  assert.match(phoneClientSource, /index !== 0 && index !== route\.length - 1 && !point\.transitionTo/);
});

test("campus route endpoints and floor changes match the kiosk catalogue", () => {
  for (const destination of routeData.destinations) {
    const route = findShortestPath(
      routeData.campusPrototypeGraph,
      routeData.campusPrototypeGraph.startNodeId,
      destination.campusNodeId
    );
    assert.equal(route.destinationId, destination.id);
    assert.equal(route.waypoints.at(-1).id, destination.campusNodeId);
    assert.deepEqual(route.waypoints.at(-1).position, destination.endpoint);
    assert.equal(route.waypoints.at(-1).floor, destination.campusFloor);
    assert.equal(route.transitionCount, destination.campusFloor === "First" ? 1 : 0);
    assert.ok(route.estimatedWalkingSeconds > 0);
  }
});

test("browser endpoint, name and floor entries match the Unity destination catalogue", () => {
  const endpointPattern = /case "([^"]+)": endpoint = new Vector3\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\); return true;/g;
  const endpointById = new Map();
  for (const match of catalogSource.matchAll(endpointPattern)) {
    endpointById.set(match[1], match.slice(2).map(Number));
  }
  const definitionPattern = /Add\("([^"]+)", "([^"]+)", "[^"]+", "(Ground|First)"\);/g;
  const definitionsById = new Map();
  for (const match of catalogSource.matchAll(definitionPattern)) {
    definitionsById.set(match[1], { name: match[2], floor: match[3] });
  }
  assert.equal(endpointById.size, 4);
  assert.equal(definitionsById.size, 22);
  for (const destination of routeData.destinations) {
    const localEndpoint = endpointById.get(destination.id);
    const definition = definitionsById.get(destination.id);
    assert.ok(definition, "Missing Unity definition " + destination.id);
    assert.deepEqual(definition, { name: destination.name, floor: destination.campusFloor });
    if (!localEndpoint) continue;
    const unityWorldEndpoint = localEndpoint.map((value, index) =>
      value + routeData.integrationOrigin[index]);
    for (let axis = 0; axis < 3; axis++) {
      assert.ok(
        Math.abs(unityWorldEndpoint[axis] - destination.endpoint[axis]) < 1e-5,
        "Endpoint mismatch for " + destination.id + " axis " + axis
      );
    }
  }
});

test("route reconstruction and tie-breaking are deterministic", () => {
  for (const destination of routeData.destinations) {
    const first = findShortestPath(routeData.navigationGraph, routeData.navigationGraph.startNodeId, destination.nodeId);
    for (let repeat = 0; repeat < 4; repeat++) {
      const next = findShortestPath(routeData.navigationGraph, routeData.navigationGraph.startNodeId, destination.nodeId);
      assert.deepEqual(next.nodeIds, first.nodeIds);
      assert.equal(next.lengthMeters, first.lengthMeters);
    }
  }
});

test("each destination has a distinct terminal and shared paths end in destination-specific branches", () => {
  const audit = auditDestinationRoutes(routeData);
  assert.equal(new Set(audit.map(route => route.nodeId)).size, 22);
  assert.equal(new Set(audit.map(route => route.nodeIds.join(">"))).size, 22);
  assert.equal(new Set(routeData.destinations.map(destination => destination.endpoint.join(","))).size, 22);
  assert.ok(audit.every(route => route.nodeIds[0] === routeData.campusPrototypeGraph.startNodeId));
  assert.ok(audit.some(route => route.nodeIds.length > 100), "Long campus routes must follow the connected model paths.");
});

test("unreachable endpoints fail explicitly instead of reusing a previous route", () => {
  const isolated = structuredClone(routeData.navigationGraph);
  isolated.nodes.push({
    id: "unreachable-test",
    position: [200, 0, 200],
    floor: "Ground",
    label: "Unreachable test node",
    kind: "test"
  });
  assert.throws(
    () => findShortestPath(isolated, isolated.startNodeId, "unreachable-test"),
    /No walkable route connects/
  );
});

test("graph validation rejects zero-length edges and unmarked floor changes", () => {
  const zeroLength = structuredClone(routeData.navigationGraph);
  zeroLength.edges.push({
    id: "invalid-zero-edge",
    from: "entrance",
    to: "entrance",
    walkable: true,
    kind: "prototype-outdoor"
  });
  assert.throws(() => validateNavigationGraph(zeroLength), /zero length/);

  const unmarkedTransition = structuredClone(routeData.navigationGraph);
  unmarkedTransition.edges.push({
    id: "invalid-floor-edge",
    from: "stairMidpoint",
    to: "stairTop",
    walkable: true,
    kind: "representative-interior"
  });
  assert.throws(() => validateNavigationGraph(unmarkedTransition), /floor-transition metadata/);
});

test("reverse floor transitions point to the floor actually reached", () => {
  const route = findShortestPath(
    routeData.navigationGraph,
    "upperCorridor",
    "destination-cafeteria"
  );
  const transition = route.waypoints.find(point => point.transitionTo);
  assert.ok(transition);
  assert.equal(transition.floor, "Ground");
  assert.equal(transition.transitionTo, "Ground");
});

test("immersive AR requires a secure context and a supported WebXR session", async () => {
  const insecure = await checkImmersiveArSupport({ isSecureContext: false, navigator: { xr: {
    isSessionSupported: async () => true,
    requestSession: async () => ({})
  } } });
  assert.deepEqual(insecure, { supported: false, reason: "secure-context-required" });

  const noWebXr = await checkImmersiveArSupport({ isSecureContext: true, navigator: {} });
  assert.deepEqual(noWebXr, { supported: false, reason: "webxr-unavailable" });

  const unsupported = await checkImmersiveArSupport({ isSecureContext: true, navigator: { xr: {
    isSessionSupported: async () => false,
    requestSession: async () => ({})
  } } });
  assert.deepEqual(unsupported, { supported: false, reason: "device-or-browser-unsupported" });

  const supported = await checkImmersiveArSupport({ isSecureContext: true, navigator: { xr: {
    isSessionSupported: async mode => mode === "immersive-ar",
    requestSession: async () => ({})
  } } });
  assert.deepEqual(supported, { supported: true, reason: "available" });
});

test("immersive AR capability failures preserve an explicit virtual fallback", async () => {
  const failure = new Error("permission denied by browser");
  const capability = await checkImmersiveArSupport({ isSecureContext: true, navigator: { xr: {
    isSessionSupported: async () => { throw failure; },
    requestSession: async () => ({})
  } } });
  assert.equal(capability.supported, false);
  assert.equal(capability.reason, "capability-check-failed");
  assert.equal(capability.error, failure);
});

test("immersive AR session request starts synchronously in the tap activation", async () => {
  let requestedMode = "";
  let requestedOptions;
  const expectedSession = { end() {} };
  const environment = {
    isSecureContext: true,
    navigator: { xr: {
      requestSession(mode, options) {
        requestedMode = mode;
        requestedOptions = options;
        return Promise.resolve(expectedSession);
      }
    } }
  };
  const options = { requiredFeatures: ["local"], domOverlay: { root: {} } };
  const sessionPromise = requestImmersiveArSession(environment, options);

  assert.equal(requestedMode, "immersive-ar", "requestSession must run before returning to the event loop");
  assert.equal(requestedOptions, options);
  assert.equal(await sessionPromise, expectedSession);
  assert.throws(() => requestImmersiveArSession({ isSecureContext: false, navigator: environment.navigator }, options),
    /secure browser context/);
});

test("immersive AR tap gives immediate feedback and prevents duplicate session requests", async () => {
  const script = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  const start = script.indexOf("async function startXrPreview()");
  const end = script.indexOf("async function checkOptionalAr()", start);
  assert.ok(start >= 0 && end > start);
  const handler = script.slice(start, end);
  assert.match(handler, /if \(xrStartPending\) return/);
  assert.ok(handler.indexOf("xrStartPending = true") < handler.indexOf("requestImmersiveArSession(window"));
  assert.ok(handler.indexOf("STARTING AR") < handler.indexOf("requestImmersiveArSession(window"));
  assert.ok(handler.indexOf("requestImmersiveArSession(window") < handler.indexOf("await sessionRequest"));
  assert.match(handler, /finally\s*\{\s*xrStartPending = false;/);
});

test("campus route checks for a clear side step before stopping at model geometry", async () => {
  const script = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  assert.match(script, /if \(routeMode === "campus"\) return isBlockedByProjectedCampusMeshes\(from, to, allowMappedPrototypeInterior\)/);
  assert.match(script, /function buildCampusObstacleIndex\(data\)/);
  assert.match(script, /function isBlockedByProjectedCampusMeshes\(from, to, allowMappedPrototypeInterior = false\)/);
  assert.match(script, /function pointInObstacleTriangle\(x, z, triangle\)/);
  assert.match(script, /function pointToObstacleEdgeDistance\(x, z, ax, az, bx, bz\)/);
  assert.match(script, /campus-building-obstacles\.json\?v=5\.0-surface-placement-stair-routes/);
  assert.doesNotMatch(script, /isBlockedByNamedBuildingBounds/);
  assert.match(script, /function findCampusObstacleDetour\(from, target\)/);
  assert.match(script, /if \(!isBlockedByCampus\(from, candidate\.point\)\) return candidate\.point/);
  assert.match(script, /route\.splice\(routeIndex, 0, waypoint/);
  assert.match(script, /routeDetourCount >= 12/);
  assert.match(script, /virtual camera did not pass through it/i);
});

test("mobile wayfinding keeps the campus visible behind a compact, accessible panel", async () => {
  const html = await readFile(new URL("./index.html", import.meta.url), "utf8");
  const styles = await readFile(new URL("./styles.css", import.meta.url), "utf8");
  const script = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  assert.match(html, /id="panelToggle"[^>]*aria-expanded="false"[^>]*aria-controls="panelDetails"/);
  assert.match(html, /<div class="panelHeading">[\s\S]*id="destination"[\s\S]*id="panelToggle"/);
  assert.doesNotMatch(html, /MORE OPTIONS/);
  assert.match(html, /id="panelDetails" class="panelDetails" hidden/);
  assert.match(html, /id="startRoute"/);
  assert.match(html, /id="xrPreview"/);
  assert.match(script, /matchMedia\("\(max-width: 759px\)"\)/);
  assert.match(script, /function setPanelExpanded\(expanded\)/);
  assert.match(script, /setPanelExpanded\(false\)/);
  assert.match(styles, /\.panel\.collapsed\s*\{\s*max-height:\s*none/s);
  assert.match(styles, /\.panel\.collapsed header > #floor \{ display: none; \}/);
  assert.match(styles, /\.panel\.collapsed \.routeSummary #instruction\s*\{[\s\S]*-webkit-line-clamp:\s*1/s);
  assert.match(styles, /\.panelDetails\[hidden\]\s*\{\s*display:\s*none/s);
});

test("immersive AR uses a floor-relative reference space when the phone provides one", async () => {
  const script = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  const start = script.indexOf("async function startXrPreview()");
  const end = script.indexOf("async function checkOptionalAr()", start);
  const handler = script.slice(start, end);
  assert.match(handler, /optionalFeatures:\s*\["local-floor",\s*"dom-overlay",\s*"hit-test"\]/);
  assert.match(handler, /await session\.requestReferenceSpace\("local-floor"\)/);
  assert.match(handler, /renderer\.xr\.setReferenceSpaceType\(referenceSpaceType\)/);
  assert.match(handler, /root:\s*controlPanelElement/);
  assert.match(script, /function placeArMapFromViewer\(\)/);
  assert.match(script, /const mapScale = Math\.min\(0\.01, 2\.0 \/ Math\.max\(footprint, 1\)\)/);
  assert.match(script, /viewer\.y - campusBounds\.min\.y \* mapScale/);
  assert.match(script, /target\.x - center\.x \* mapScale/);
  assert.match(script, /panelToggleButton\.textContent = "RECENTER MAP"/);
  assert.match(script, /placeArMapFromViewer\(\);\s*setToast\("Campus route map centered in front of the camera"\)/);
  assert.match(handler, /renderer\.setClearColor\(0x000000, 0\)/);
  assert.match(handler, /renderer\.xr\.getEnvironmentBlendMode\(\)/);
  assert.match(script, /session\.requestHitTestSource\(\{ space: viewerSpace \}\)/);
  assert.match(script, /function placeArMapOnSurface\(position\)/);
  assert.match(script, /depthTest: true/);
  assert.match(script, /Use Chrome on an ARCore-supported Android phone/);
});

test("virtual route camera follows a smooth horizontal look-ahead without pitching into the pavement", async () => {
  const script = await readFile(new URL("./virtual-campus.js", import.meta.url), "utf8");
  assert.match(script, /const cameraLookAheadDistance = 5/);
  assert.match(script, /function getRouteCameraLookAhead\(position, firstIndex, distanceAhead\)/);
  assert.match(script, /const desiredForward = lookAhead\.sub\(position\);\s*desiredForward\.y = 0/);
  assert.match(script, /1 - Math\.exp\(-2\.5 \* Math\.max\(0, deltaTime\)\)/);
  assert.match(script, /1 - Math\.exp\(-5 \* Math\.max\(0, deltaTime\)\)/);
  assert.match(script, /const target = eye\.clone\(\)\.addScaledVector\(cameraForward, 6\);\s*target\.y -= 0\.12/);
  assert.match(script, /routePositionMarker\.position\.copy\(markerPosition\)/);
});
