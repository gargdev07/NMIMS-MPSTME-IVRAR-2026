import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const source = JSON.parse(fs.readFileSync(path.join(here, "walkable-surface.json"), "utf8"));
const prior = JSON.parse(fs.readFileSync(path.join(here, "route-data.json"), "utf8")).campusPrototypeGraph;
const buildingMeshes = JSON.parse(fs.readFileSync(path.join(here, "campus-building-obstacles.json"), "utf8")).meshes;

// Bounds are measured from Unity Renderer.bounds for the named imported campus
// structures. The safety margin keeps a pedestrian outside the facade/overhang.
const obstacles = [
  ["Building_001", 22.95, 83.21, -5.18, 41.12],
  // Extra clearance was added after exact Unity mesh raycasts found route
  // segments grazing the round model despite clearing its renderer bounds.
  { id: "GlassHouseModel", shape: "circle", centerX: -21.68, centerZ: -27.92, radius: 20.15, clearance: 1.5 },
  ["Building_North", -59.52, 12.22, 5.06, 52.62],
  ["Building_23990", -78.01, -52.07, -55.08, -0.69],
  ["House_base_000", 12.17, 37.37, -93.57, -58.01],
  ["House_base_001", 4.16, 34.24, -40.98, 9.92],
  ["House_base_002", -29.64, 5.92, -79.64, -54.44],
  ["House_base_003", 39.91, 75.47, -34.27, -9.07]
].map(item => Array.isArray(item)
  ? ({ id: item[0], minX: item[1], maxX: item[2], minZ: item[3], maxZ: item[4], clearance: 0.8 })
  : item);

const spacing = 1.5;
const y = 2.6;
// The imported renderer bounds cover the doorway opening too. This narrow
// portal mask is restricted to entrance003, which was checked against the
// Unity mesh. Path samples still must lie on the imported walk surface.
const checkedDoorwayPortals = [
  { building: "Building_001", minX: 40.0, maxX: 51.0, minZ: 15.0, maxZ: 27.0, marker: "entrance003" }
];
const meshTriangleBins = new Map();
const projectedObstacles = [];
function obstacleIdForMesh(meshId) {
  return obstacles.find(item => meshId.includes(item.id))?.id ?? meshId;
}
for (const mesh of buildingMeshes) {
  const buildingId = obstacleIdForMesh(mesh.id);
  for (const triangle of mesh.triangles) {
    const obstacleTriangle = { buildingId, triangle };
    projectedObstacles.push(obstacleTriangle);
    const xs = [triangle[0], triangle[2], triangle[4]], zs = [triangle[1], triangle[3], triangle[5]];
    for (let bx = Math.floor(Math.min(...xs)); bx <= Math.floor(Math.max(...xs)); bx++)
      for (let bz = Math.floor(Math.min(...zs)); bz <= Math.floor(Math.max(...zs)); bz++) {
        const key = `${bx},${bz}`, bin = meshTriangleBins.get(key) ?? [];
        bin.push(obstacleTriangle); meshTriangleBins.set(key, bin);
      }
  }
}
const gridKey = (ix, iz) => `${ix},${iz}`;
const world = (ix, iz) => [ix * spacing, y, iz * spacing];
const polygonBins = new Map();
for (const triangle of source.triangles) {
  const xs = [triangle[0], triangle[2], triangle[4]];
  const zs = [triangle[1], triangle[3], triangle[5]];
  const minX = Math.floor(Math.min(...xs));
  const maxX = Math.ceil(Math.max(...xs));
  const minZ = Math.floor(Math.min(...zs));
  const maxZ = Math.ceil(Math.max(...zs));
  for (let bx = minX; bx <= maxX; bx++) {
    for (let bz = minZ; bz <= maxZ; bz++) {
      const key = `${bx},${bz}`;
      const list = polygonBins.get(key) ?? [];
      list.push(triangle);
      polygonBins.set(key, list);
    }
  }
}

function pointInTriangle(x, z, t) {
  const ax = t[0], az = t[1], bx = t[2], bz = t[3], cx = t[4], cz = t[5];
  const d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
  if (Math.abs(d) < 1e-9) return false;
  const a = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d;
  const b = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d;
  const c = 1 - a - b;
  return a >= -1e-6 && b >= -1e-6 && c >= -1e-6;
}

function onWalkSurface(x, z) {
  const triangles = polygonBins.get(`${Math.floor(x)},${Math.floor(z)}`) ?? [];
  return triangles.some(t => pointInTriangle(x, z, t));
}

function pointInProjectedTriangle(x, z, t) {
  const ax=t[0],az=t[1],bx=t[2],bz=t[3],cx=t[4],cz=t[5];
  const d=(bz-cz)*(ax-cx)+(cx-bx)*(az-cz);
  if (Math.abs(d)<1e-9) return false;
  const a=((bz-cz)*(x-cx)+(cx-bx)*(z-cz))/d;
  const b=((cz-az)*(x-cx)+(ax-cx)*(z-cz))/d;
  const c=1-a-b;
  return a>=-1e-6&&b>=-1e-6&&c>=-1e-6;
}
function pointSegmentDistance(x,z,ax,az,bx,bz) {
  const dx=bx-ax,dz=bz-az,denom=dx*dx+dz*dz;
  const t=denom>0?Math.max(0,Math.min(1,((x-ax)*dx+(z-az)*dz)/denom)):0;
  return Math.hypot(x-(ax+t*dx),z-(az+t*dz));
}
function inObstacle(x, z, margin = 0.3) {
  const triangles = meshTriangleBins.get(`${Math.floor(x)},${Math.floor(z)}`) ?? [];
  for (const { buildingId, triangle } of triangles) {
    if (checkedDoorwayPortals.some(g => g.building === buildingId && x >= g.minX && x <= g.maxX && z >= g.minZ && z <= g.maxZ)) continue;
    if (pointInProjectedTriangle(x,z,triangle) || (margin > 0 && Math.min(
      pointSegmentDistance(x,z,triangle[0],triangle[1],triangle[2],triangle[3]),
      pointSegmentDistance(x,z,triangle[2],triangle[3],triangle[4],triangle[5]),
      pointSegmentDistance(x,z,triangle[4],triangle[5],triangle[0],triangle[1])) < margin)) return true;
  }
  // Keep measured shell bounds only when the Unity mesh export is unavailable.
  if (buildingMeshes.some(mesh => mesh.id.includes("Building_23990"))) return false;
  const prototypeBounds = obstacles.find(b => b.id === "Building_23990");
  return x > prototypeBounds.minX - prototypeBounds.clearance - margin &&
    x < prototypeBounds.maxX + prototypeBounds.clearance + margin &&
    z > prototypeBounds.minZ - prototypeBounds.clearance - margin &&
    z < prototypeBounds.maxZ + prototypeBounds.clearance + margin;
}

function lineClear(a, b, requireSurface = true) {
  const d = Math.hypot(b[0] - a[0], b[2] - a[2]);
  const steps = Math.max(2, Math.ceil(d / 0.25));
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const x = a[0] + (b[0] - a[0]) * t;
    const z = a[2] + (b[2] - a[2]) * t;
    if (inObstacle(x, z) || (requireSurface && !onWalkSurface(x, z))) return false;
  }
  return true;
}

const nodes = [];
const edges = [];
const samples = new Map();
const limits = source.triangles.flatMap(t => [[t[0], t[1]], [t[2], t[3]], [t[4], t[5]]]);
const minIx = Math.floor(Math.min(...limits.map(p => p[0])) / spacing);
const maxIx = Math.ceil(Math.max(...limits.map(p => p[0])) / spacing);
const minIz = Math.floor(Math.min(...limits.map(p => p[1])) / spacing);
const maxIz = Math.ceil(Math.max(...limits.map(p => p[1])) / spacing);
for (let ix = minIx; ix <= maxIx; ix++) {
  for (let iz = minIz; iz <= maxIz; iz++) {
    const p = world(ix, iz);
    if (!inObstacle(p[0], p[2]) && onWalkSurface(p[0], p[2])) {
      const id = `walk-${ix}-${iz}`;
      nodes.push({ id, position: p, floor: "Ground", label: "Imported paved campus route (walk-surface mesh)", kind: "geometry-derived-walk-surface" });
      samples.set(gridKey(ix, iz), id);
    }
  }
}

let edgeNumber = 0;
for (const [key, from] of samples) {
  const [ix, iz] = key.split(",").map(Number);
  for (const [dx, dz] of [[1, 0], [0, 1], [1, 1], [1, -1]]) {
    const to = samples.get(gridKey(ix + dx, iz + dz));
    if (!to) continue;
    const a = nodes.find(n => n.id === from).position;
    const b = nodes.find(n => n.id === to).position;
    if (!lineClear(a, b)) continue;
    edges.push({ id: `pave-${edgeNumber++}`, from, to, walkable: true, kind: "imported-paved-path" });
  }
}

// The imported sand paths contain a few separated polygons and narrow seams.
// Join only nearby polygon ends with a clear outdoor link; this never routes
// through a measured building footprint and is labeled as inferred in the UI.
function surfaceComponents() {
  const adjacency = new Map(nodes.map(node => [node.id, []]));
  for (const edge of edges) {
    adjacency.get(edge.from)?.push(edge.to);
    adjacency.get(edge.to)?.push(edge.from);
  }
  const seen = new Set();
  const components = [];
  for (const id of samples.values()) {
    if (seen.has(id)) continue;
    const pending = [id], component = [];
    seen.add(id);
    while (pending.length) {
      const current = pending.pop(); component.push(current);
      for (const next of adjacency.get(current) ?? []) if (!seen.has(next)) { seen.add(next); pending.push(next); }
    }
    components.push(component.filter(nodeId => surfaceNodeById.has(nodeId)));
  }
  return components.sort((a, b) => b.length - a.length);
}
const surfaceNodeById = new Map(nodes.map(node => [node.id, node]));
function outdoorPolyline(startPoint, endPoint) {
  if (lineClear(startPoint, endPoint, false)) return [startPoint, endPoint];
  const points = [startPoint, endPoint];
  for (const obstacle of obstacles) {
    if (obstacle.shape === "circle") {
      const radius = obstacle.radius + obstacle.clearance + 0.25;
      for (let i = 0; i < 48; i++) {
        const angle = Math.PI * 2 * i / 48;
        points.push([obstacle.centerX + Math.cos(angle) * radius, y, obstacle.centerZ + Math.sin(angle) * radius]);
      }
    } else {
      const minX = obstacle.minX - obstacle.clearance - 0.25, maxX = obstacle.maxX + obstacle.clearance + 0.25;
      const minZ = obstacle.minZ - obstacle.clearance - 0.25, maxZ = obstacle.maxZ + obstacle.clearance + 0.25;
      points.push([minX, y, minZ], [minX, y, maxZ], [maxX, y, minZ], [maxX, y, maxZ]);
    }
  }
  const adjacency = points.map(() => []);
  for (let i = 0; i < points.length; i++) for (let j = i + 1; j < points.length; j++) {
    if (!lineClear(points[i], points[j], false)) continue;
    const length = Math.hypot(points[i][0] - points[j][0], points[i][2] - points[j][2]);
    adjacency[i].push([j, length]); adjacency[j].push([i, length]);
  }
  const distances = new Array(points.length).fill(Infinity), previous = new Array(points.length).fill(-1), visited = new Set();
  distances[0] = 0;
  while (visited.size < points.length) {
    let current = -1;
    for (let i = 0; i < points.length; i++) if (!visited.has(i) && (current < 0 || distances[i] < distances[current])) current = i;
    if (current < 0 || !Number.isFinite(distances[current]) || current === 1) break;
    visited.add(current);
    for (const [next, length] of adjacency[current]) if (distances[current] + length < distances[next]) {
      distances[next] = distances[current] + length; previous[next] = current;
    }
  }
  if (!Number.isFinite(distances[1])) return null;
  const route = []; for (let cursor = 1; cursor >= 0; cursor = previous[cursor]) { route.push(points[cursor]); if (cursor === 0) break; }
  return route.reverse();
}
let connectorNumber = 0;
for (let components = surfaceComponents(); components.length > 1; components = surfaceComponents()) {
  const candidates = [];
  for (let a = 0; a < components.length; a++) {
    for (let b = a + 1; b < components.length; b++) {
      for (const from of components[a]) {
        const fromNode = surfaceNodeById.get(from);
        for (const to of components[b]) {
          const toNode = surfaceNodeById.get(to);
          const length = Math.hypot(fromNode.position[0] - toNode.position[0], fromNode.position[2] - toNode.position[2]);
          if (length > 40) continue;
          candidates.push({ from, to, length });
        }
      }
    }
  }
  candidates.sort((a, b) => a.length - b.length || a.from.localeCompare(b.from) || a.to.localeCompare(b.to));
  let best = null, connector = null;
  const rejected = [];
  for (const candidate of candidates) {
    const route = outdoorPolyline(surfaceNodeById.get(candidate.from).position, surfaceNodeById.get(candidate.to).position);
    const routeLength = route?.slice(1).reduce((sum, point, i) => sum + Math.hypot(point[0] - route[i][0], point[2] - route[i][2]), 0);
    // Reject perimeter detours across grass or around the entire campus. Only
    // short local seams between neighboring imported path polygons are joined.
    if (!route || route.length > 8 || routeLength > 75 || routeLength > Math.max(25, candidate.length * 4.5)) {
      rejected.push(`${candidate.from}->${candidate.to} straight=${candidate.length.toFixed(1)} route=${routeLength?.toFixed(1) ?? "blocked"} turns=${route?.length ?? 0}`);
      continue;
    }
    best = candidate; connector = route; break;
  }
  if (!best) {
    // Some imported paved islands are separated by a complete building row.
    // Keep those islands in the geometry graph for local map coverage, but do
    // not fake a connection around the campus perimeter. Every catalog
    // destination is validated against the entrance-connected component below.
    console.warn(`Kept ${components.length} separate paved components instead of adding a long obstacle detour.`);
    break;
  }
  let from = best.from;
  for (let i = 1; i < connector.length - 1; i++) {
    const id = `outdoor-turn-${connectorNumber}-${i}`;
    const node = { id, position: connector[i], floor: "Ground", label: "Inferred outdoor turn around a building footprint", kind: "inferred-building-avoidance-turn" };
    nodes.push(node); surfaceNodeById.set(id, node);
    edges.push({ id: `outdoor-gap-link-${connectorNumber}-${i}`, from, to: id, walkable: true, kind: "inferred-outdoor-gap-link" });
    from = id;
  }
  edges.push({ id: `outdoor-gap-link-${connectorNumber}-end`, from, to: best.to, walkable: true, kind: "inferred-outdoor-gap-link" });
  connectorNumber++;
}

function nearestWalkable(point, { maxDistance = 7, used = [], requireStartReachable = false } = {}) {
  const reachable = requireStartReachable ? distanceFromCampusStart() : null;
  const candidates = nodes.map(n => ({ n, d: Math.hypot(n.position[0] - point[0], n.position[2] - point[2]) }))
    .filter(({ n, d }) => d <= maxDistance && !used.some(p => Math.hypot(p[0] - n.position[0], p[2] - n.position[2]) < 1.2) &&
      lineClear(point, n.position, false) && (!reachable || Number.isFinite(reachable.get(n.id))))
    .sort((a, b) => a.d - b.d || a.n.id.localeCompare(b.n.id));
  return candidates[0]?.n ?? null;
}

function connectTerminal(id, point, label, kind, destinationId = "", floor = "Ground", campusBuilding = "", gateApproachNodeId = "") {
  const p = [point[0], point[1] ?? y, point[2]];
  const nodeId = `campus-destination-${destinationId || id}`;
  nodes.push({ id: nodeId, position: p, floor, label, kind, destinationId, campusBuilding });
  // Geometry-grounded facade approaches can intentionally sit within 1.2 m of
  // their nearest paved graph node. Preserve that measured connection rather
  // than pushing the destination marker back into an open area.
  const gateNode = gateApproachNodeId ? nodes.find(n => n.id === gateApproachNodeId) : null;
  const nearest = gateNode && Math.hypot(gateNode.position[0] - p[0], gateNode.position[2] - p[2]) < 0.05
    ? gateNode
    : nearestWalkable(p, { maxDistance: 7, requireStartReachable: true,
      used: [...terminals.map(t => t.position), p] });
  if (!nearest) throw new Error(`No obstacle-safe paved approach is within range for ${id} at ${p.join(",")}`);
  if (!lineClear(nearest.position, p, false)) throw new Error(`Terminal connector for ${id} crosses a building footprint.`);
  edges.push({ id: `approach-${id}`, from: nearest.id, to: nodeId, walkable: true, kind: "inferred-ground-approach-connector" });
  terminals.push({ id, position: p });
  return nodeId;
}

function outsideGate(point, preferredBuilding) {
  const preferred = obstacles.find(item => item.id === preferredBuilding);
  const b = preferred && preferred.shape !== "circle" &&
    (point[0] >= preferred.minX - preferred.clearance - 6 && point[0] <= preferred.maxX + preferred.clearance + 6 &&
      point[2] >= preferred.minZ - preferred.clearance - 6 && point[2] <= preferred.maxZ + preferred.clearance + 6)
    ? preferred : obstacles.find(item => !preferredBuilding && (item.shape === "circle"
      ? Math.hypot(point[0] - item.centerX, point[2] - item.centerZ) <= item.radius + item.clearance
      : point[0] >= item.minX - item.clearance && point[0] <= item.maxX + item.clearance &&
        point[2] >= item.minZ - item.clearance && point[2] <= item.maxZ + item.clearance));
  if (!b) return [...point];
  if (b.shape === "circle") {
    const dx = point[0] - b.centerX, dz = point[2] - b.centerZ;
    const length = Math.hypot(dx, dz) || 1;
    const radius = b.radius + b.clearance + 0.1;
    return [b.centerX + dx / length * radius, y, b.centerZ + dz / length * radius];
  }
  const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
  const candidates = [
    [b.minX - b.clearance - 0.1, clamp(point[2], b.minZ, b.maxZ)],
    [b.maxX + b.clearance + 0.1, clamp(point[2], b.minZ, b.maxZ)],
    [clamp(point[0], b.minX, b.maxX), b.minZ - b.clearance - 0.1],
    [clamp(point[0], b.minX, b.maxX), b.maxZ + b.clearance + 0.1]
  ].map(candidate => [candidate[0], y, candidate[1]]);
  const reachable = candidates.map(candidate => {
    const path = nearestWalkable(candidate, { maxDistance: 30, requireStartReachable: true });
    const connectorDistance = path ? Math.hypot(path.position[0] - candidate[0], path.position[2] - candidate[2]) : Infinity;
    const routeDistance = path ? distanceFromCampusStart().get(path.id) : Infinity;
    return { candidate, path, score: Number.isFinite(routeDistance) ? routeDistance + connectorDistance : Infinity };
  }).filter(item => Number.isFinite(item.score)).sort((a, c) => a.score - c.score);
  if (reachable.length) return reachable[0].candidate;
  candidates.sort((a, c) => Math.hypot(a[0] - point[0], a[2] - point[2]) - Math.hypot(c[0] - point[0], c[2] - point[2]));
  return candidates[0];
}

const terminals = [];
const priorById = new Map(prior.nodes.filter(n => n.destinationId).map(n => [n.destinationId, n]));
const destinationSourceAnchors = {
  auditorium: [-16, y, -7], "lab-104": [29.3, y, -95],
  "seminar-hall": [43.5, y, 22.5], "deans-office": [46.5, y, 22.5],
  classrooms: [40.5, y, 22.5], "faculty-offices": [49.5, y, 22.5], library: [19.5, y, -43.5], reception: [-18, y, -60],
  "student-help": [36, y, -27], administration: [45, y, 22.5],
  "student-activity": [4, y, -39], cafeteria: [-3, y, -53.54], water: [-3, y, 12],
  washrooms: [55.5, y, -31.5], "first-aid": [36, y, -82.5], "main-entrance": [61.995955, y, -65.5],
  "visitor-reception": [22.5, y, -40.5], parking: [70.5, y, -105], security: [54, y, -53]
};
const anchorBuilding = {
  "lab-104": "House_base_000", "first-aid": "House_base_000",
  classrooms: "Building_001", "seminar-hall": "Building_001",
  "deans-office": "Building_001", administration: "Building_001", "faculty-offices": "Building_001",
  reception: "House_base_002", "visitor-reception": "House_base_001",
  library: "House_base_001", "student-help": "House_base_003",
  washrooms: "House_base_003", cafeteria: "House_base_002"
};
const assignmentLabels = {
  auditorium: "Central circular building (auditorium function inferred from form)",
  "lab-104": "House_base_000 (lab function inferred)",
  classrooms: "Building_001 (academic section; classroom function inferred; entrance003 front approach)",
  "seminar-hall": "Building_001 (seminar function inferred; entrance003 front approach)",
  "deans-office": "Building_001 (dean's office function inferred; outside entrance003 approach)",
  "faculty-offices": "Building_001 (faculty office function inferred; outside entrance003 approach)",
  library: "House_base_001 (library function inferred from the requested role swap)",
  reception: "House_base_002 (reception function inferred; west-facade approach nearest the paved route)",
  "visitor-reception": "House_base_001 (visitor reception function inferred; central south/front-gate approach)",
  washrooms: "House_base_003 (washrooms function inferred; south-facade approach nearest the paved route)",
  administration: "Building_001 (administration function inferred; outside entrance003 approach)",
  cafeteria: "House_base_002 (cafeteria function inferred from the requested role swap)",
  "first-aid": "House_base_000 (first-aid function inferred; south-facade approach nearest the paved route)",
  parking: "Paved campus-path parking approach (parking function inferred; no parking asset modeled)"
};

// The imported house meshes have no named doors or entrance markers. These
// are the nearest reachable paved graph nodes to each measured mesh facade;
// destination pins sit 2 cm outward from the node, with the short connector
// kept explicit in the route graph. Each facility uses a different house.
const inferredFacadeGateNodeIds = {
  washrooms: "walk-37--21",
  reception: "walk--12--40",
  "visitor-reception": "walk-15--27",
  "first-aid": "walk-24--55"
};
function inferredFacadeApproach(nodeId, buildingId) {
  const node = nodes.find(n => n.id === nodeId);
  if (!node) throw new Error(`Missing measured facade path node ${nodeId}`);
  let nearestPoint = null, nearestDistance = Infinity;
  for (const { buildingId: obstacleId, triangle } of projectedObstacles) {
    if (obstacleId !== buildingId) continue;
    for (const [u, v] of [[0, 1], [1, 2], [2, 0]]) {
      const ax = triangle[u * 2], az = triangle[u * 2 + 1];
      const bx = triangle[v * 2], bz = triangle[v * 2 + 1];
      const dx = bx - ax, dz = bz - az;
      const t = Math.max(0, Math.min(1, ((node.position[0] - ax) * dx + (node.position[2] - az) * dz) / (dx * dx + dz * dz || 1)));
      const candidate = [ax + t * dx, az + t * dz];
      const distance = Math.hypot(node.position[0] - candidate[0], node.position[2] - candidate[1]);
      if (distance < nearestDistance) { nearestDistance = distance; nearestPoint = candidate; }
    }
  }
  if (!nearestPoint || nearestDistance < 0.3) throw new Error(`No safe exterior facade clearance for ${buildingId} at ${nodeId}`);
  const dx = node.position[0] - nearestPoint[0], dz = node.position[2] - nearestPoint[1];
  const length = Math.hypot(dx, dz);
  const point = [node.position[0] + dx / length * 0.02, y, node.position[2] + dz / length * 0.02];
  if (inObstacle(point[0], point[2], 0.3)) throw new Error(`Inferred gate approach for ${buildingId} intersects its mesh clearance.`);
  return point;
}

const start = [61.995955, y, -68];
nodes.push({ id: "campusApproach", position: start, floor: "Ground", label: "Campus entrance start on the imported outdoor route network", kind: "inferred-campus-entrance" });
const startApproach = nearestWalkable(start, { maxDistance: 18, used: [start] });
if (!startApproach || !lineClear(start, startApproach.position, false)) throw new Error("Campus entrance cannot safely connect to the imported paved network.");
if (Math.hypot(start[0] - startApproach.position[0], start[2] - startApproach.position[2]) > 0.05)
  edges.push({ id: "campus-entry-to-paved-path", from: "campusApproach", to: startApproach.id, walkable: true, kind: "short-inferred-campus-entry-connector" });

let cachedStartDistances = null;
function distanceFromCampusStart() {
  if (cachedStartDistances) return cachedStartDistances;
  const byId = new Map(nodes.map(node => [node.id, node]));
  const adjacency = new Map(nodes.map(node => [node.id, []]));
  for (const edge of edges) {
    if (!edge.walkable) continue;
    const a = byId.get(edge.from), b = byId.get(edge.to);
    if (!a || !b) continue;
    const length = Math.hypot(a.position[0] - b.position[0], a.position[1] - b.position[1], a.position[2] - b.position[2]);
    adjacency.get(a.id).push([b.id, length]); adjacency.get(b.id).push([a.id, length]);
  }
  const distance = new Map([["campusApproach", 0]]), pending = new Set(["campusApproach"]), visited = new Set();
  while (pending.size) {
    let current = null;
    for (const id of pending) if (current === null || distance.get(id) < distance.get(current)) current = id;
    pending.delete(current); visited.add(current);
    for (const [next, length] of adjacency.get(current) ?? []) {
      if (visited.has(next)) continue;
      const candidate = distance.get(current) + length;
      if (candidate < (distance.get(next) ?? Infinity)) { distance.set(next, candidate); pending.add(next); }
    }
  }
  cachedStartDistances = distance;
  return cachedStartDistances;
}

const labels = {
  auditorium: "Auditorium", "lab-101": "Lab 101", "lab-102": "Lab 102", "lab-103": "Lab 103", "lab-104": "Lab 104",
  classrooms: "Classrooms", "seminar-hall": "Seminar Hall", "deans-office": "Dean's Office", "faculty-offices": "Faculty Offices",
  library: "Library", reception: "Reception", "student-help": "Student Help Desk", administration: "Administration",
  "student-activity": "Student Activity Area", cafeteria: "Cafeteria", water: "Drinking Water", washrooms: "Washrooms",
  "first-aid": "First Aid", "main-entrance": "Main Entrance", "visitor-reception": "Visitor Reception", parking: "Parking", security: "Security Desk"
};
const labs = new Set(["lab-101", "lab-102", "lab-103"]);
for (const [destinationId, oldNode] of priorById) {
  if (labs.has(destinationId)) continue;
  if (destinationId === "student-help") continue;
  let point = [...destinationSourceAnchors[destinationId]];
  const building = anchorBuilding[destinationId];
  const gateApproachNodeId = inferredFacadeGateNodeIds[destinationId] ?? "";
  const portal = ["deans-office", "faculty-offices", "administration"].includes(destinationId)
    ? checkedDoorwayPortals.find(g => g.building === "Building_001")
    : ["classrooms", "seminar-hall"].includes(destinationId)
      ? checkedDoorwayPortals.find(g => g.building === "Building_001") : null;
  if (gateApproachNodeId) {
    point = inferredFacadeApproach(gateApproachNodeId, building);
  } else if (destinationId === "cafeteria") {
    // User requested the Library and Cafeteria route assignments be swapped.
    point = [-3, y, -53.54];
  } else if (destinationId === "library") {
    // User requested the Library and Cafeteria route assignments be swapped.
    point = [19.5, y, -43.5];
  } else if (!portal) point = outsideGate(point, building);
  const msg = destinationId === "parking"
    ? "Inferred parking access at the mapped paved-path approach; this asset contains no modeled parking lot or parking bays."
    : building
    ? `${assignmentLabels[destinationId] ?? `${building} (facility function inferred)`}; inferred front-gate approach. The asset does not identify the occupied room.`
    : `${labels[destinationId]} outdoor point inferred from the campus surface model.`;
  connectTerminal(destinationId, point, msg, building ? "inferred-exterior-building-gate" : "inferred-outdoor-destination", destinationId, "Ground", assignmentLabels[destinationId] ?? "", gateApproachNodeId);
}

// The prototype's existing Unity anchors and room-door transforms are used for
// its sole interior route. Exterior path to prototype door is connected below.
// The representative interior sits inside the imported Building_23990 shell,
// aligned to a geometry-confirmed open entry at its south-east edge. A Unity
// mesh ray check at y=3.4 found no wall on the approach-to-threshold segment.
const prototypeOrigin = [-56.5, 2.5, -49.2454];
const localToWorld = local => [prototypeOrigin[0] + local[0], prototypeOrigin[1] + local[1], prototypeOrigin[2] + local[2]];
const prototypeApproach = localToWorld([-1.5, 0, -9.0]);
const prototypeDoor = localToWorld([-1.5, 0, -5.0]);
const approachPath = nearestWalkable(prototypeApproach, { maxDistance: 30, used: terminals.map(t => t.position), requireStartReachable: true });
if (!approachPath || !lineClear(approachPath.position, prototypeApproach, false)) throw new Error("Prototype front-door approach cannot connect to the campus walk surface.");
nodes.push({ id: "prototype-exterior-approach", position: prototypeApproach, floor: "Ground", label: "Outside the prototype building's inferred front doorway", kind: "prototype-front-gate" });
nodes.push({ id: "prototype-ground-threshold", position: prototypeDoor, floor: "Ground", label: "Prototype ground-floor doorway threshold", kind: "prototype-doorway-portal" });
edges.push({ id: "campus-path-to-prototype-gate", from: approachPath.id, to: "prototype-exterior-approach", walkable: true, kind: "short-inferred-prototype-gate-connector" });
edges.push({ id: "prototype-gate-to-ground-hall", from: "prototype-exterior-approach", to: "prototype-ground-threshold", walkable: true, kind: "prototype-doorway-portal" });
nodes.push({ id: "prototype-ground-corridor", position: localToWorld([-4.5, 0, -4.5]), floor: "Ground", label: "Prototype ground-floor corridor (existing Unity waypoint)", kind: "prototype-interior-waypoint" });
nodes.push({ id: "prototype-stair-bottom", position: localToWorld([-2.0, 0, 4.0]), floor: "Ground", label: "Prototype stair bottom (existing Unity waypoint)", kind: "prototype-interior-waypoint" });
nodes.push({ id: "prototype-stair-mid", position: localToWorld([-2.0, 1.0, 1.5]), floor: "Ground", label: "Prototype stair midpoint (existing Unity waypoint)", kind: "prototype-interior-waypoint" });
nodes.push({ id: "prototype-stair-top", position: localToWorld([1.5, 3.0, 1.5]), floor: "First", label: "Prototype first-floor stair landing (existing Unity waypoint)", kind: "prototype-interior-waypoint" });
nodes.push({ id: "prototype-upper-corridor", position: localToWorld([4.5, 3.0, -1.5]), floor: "First", label: "Prototype first-floor corridor (existing Unity waypoint)", kind: "prototype-interior-waypoint" });
edges.push({ id: "ground-threshold-to-corridor", from: "prototype-ground-threshold", to: "prototype-ground-corridor", walkable: true, kind: "prototype-interior" });
edges.push({ id: "corridor-to-stair-bottom", from: "prototype-ground-corridor", to: "prototype-stair-bottom", walkable: true, kind: "prototype-interior" });

function addStairFlight(startId, endId, start, end, count, prefix, completesFloorTransition = false) {
  let previousId = startId;
  const rise = end[1] - start[1];
  for (let index = 0; index < count; index++) {
    const centerProgress = (index + 0.5) / count;
    const position = start.map((value, axis) => value + (end[axis] - value) * centerProgress);
    // Match the generated stair tread tops: each tread rises at the front
    // edge, while its center sits on that step's walking surface.
    position[1] = start[1] + rise * (index + 1) / count;
    const stepId = `${prefix}-${String(index + 1).padStart(2, "0")}`;
    nodes.push({
      id: stepId,
      position,
      floor: "Ground",
      label: `Prototype stair tread ${index + 1} of ${count}`,
      kind: "prototype-stair-tread-approach"
    });
    edges.push({
      id: `${previousId}-to-${stepId}`,
      from: previousId,
      to: stepId,
      walkable: true,
      kind: "stair"
    });
    previousId = stepId;
  }
  edges.push({
    id: `${previousId}-to-${endId}`,
    from: previousId,
    to: endId,
    walkable: true,
    kind: "stair",
    ...(completesFloorTransition ? { floorTransition: true, targetFloor: "First" } : {})
  });
}

const stairBottom = localToWorld([-2.0, 0, 4.0]);
const stairMid = localToWorld([-2.0, 1.0, 1.5]);
const stairTop = localToWorld([1.5, 3.0, 1.5]);
addStairFlight("prototype-stair-bottom", "prototype-stair-mid", stairBottom, stairMid, 8, "prototype-stair-ground-tread");
addStairFlight("prototype-stair-mid", "prototype-stair-top", stairMid, stairTop, 8, "prototype-stair-upper-tread", true);
edges.push({ id: "stair-top-to-upper-corridor", from: "prototype-stair-top", to: "prototype-upper-corridor", walkable: true, kind: "prototype-interior" });

const labAnchors = {
  "lab-101": localToWorld([-1.5, 3.0, -2.6]),
  "lab-102": localToWorld([1.5, 3.0, -2.6]),
  "lab-103": localToWorld([4.5, 3.0, -2.6])
};
for (const [id, position] of Object.entries(labAnchors)) {
  const label = `${labels[id]} door on the prototype first floor (Unity room-door transform)`;
  const nodeId = `campus-destination-${id}`;
  nodes.push({ id: nodeId, position, floor: "First", label, kind: "prototype-interior-room-door", destinationId: id, campusBuilding: "Prototype building (representative interior)" });
  edges.push({ id: `upper-corridor-to-${id}`, from: "prototype-upper-corridor", to: nodeId, walkable: true, kind: "prototype-room-approach" });
}

// Give primary ground-floor prototype service counters unique positions without
// claiming that the imported campus contains these real institutions.
const prototypeGroundTargets = [
  ["student-help", localToWorld([-3.5, 0, -2.4])]
];
for (const [id, position] of prototypeGroundTargets) {
  const nodeId = `campus-destination-${id}`;
  nodes.push({ id: nodeId, position, floor: "Ground", label: `${labels[id]} (representative ground-floor service point; inferred)`, kind: "prototype-interior-service-point", destinationId: id, campusBuilding: "Prototype building (representative interior)" });
  edges.push({ id: `ground-corridor-to-${id}`, from: "prototype-ground-corridor", to: nodeId, walkable: true, kind: "prototype-room-approach" });
}

// A few node paths use AABB clearance as an additional hard wall guard. The
// route surface itself comes from the imported Sandypath/Light_sandPath mesh.
const graph = {
  startNodeId: "campusApproach",
  routingAlgorithm: "dijkstra-euclidean-edge-distance",
  routeBasis: "Imported Unity model walk-surface mesh, sampled on a 1.5 m lattice; Dijkstra by 3D world distance.",
  walkSurfaceSource: source.source,
  buildingObstacleSource: "WebXR/campus-building-obstacles.json: projected triangles exported from the eight loaded Unity building MeshFilters.",
  obstaclePolicy: "Outdoor graph nodes and links avoid projected imported building mesh triangles with a 0.3 m edge clearance. A named entrance003 mask preserves the checked front-gate approach; prototype interior links are explicitly tagged and limited to its rooms.",
  facilityAssignments: assignmentLabels,
  obstacleBounds: obstacles,
  nodes,
  edges
};
const file = path.join(here, "route-data.json");
const original = JSON.parse(fs.readFileSync(file, "utf8"));
const oldOrigin = original.integrationOrigin;
const newOrigin = prototypeOrigin;
const originDelta = newOrigin.map((value, index) => value - oldOrigin[index]);
original.integrationOrigin = newOrigin;
original.representativeInterior.origin = newOrigin;
original.campusGeometry.buildingBoundsXZ = obstacles.filter(item => item.shape !== "circle").map(item => ({
  id: item.id,
  min: [item.minX, item.minZ],
  max: [item.maxX, item.maxZ]
}));
for (const node of original.navigationGraph.nodes) {
  node.position = node.position.map((value, index) => value + originDelta[index]);
}
const prototypeRoomDoors = new Map([
  ["destination-lab-101", localToWorld([-1.5, 3.0, -2.6])],
  ["destination-lab-102", localToWorld([1.5, 3.0, -2.6])],
  ["destination-lab-103", localToWorld([4.5, 3.0, -2.6])],
  ["destination-student-help", localToWorld([-3.5, 0, -2.4])]
]);
for (const node of original.navigationGraph.nodes) {
  if (prototypeRoomDoors.has(node.id)) node.position = [...prototypeRoomDoors.get(node.id)];
}
for (const [id, position] of Object.entries(original.nodes)) {
  original.nodes[id] = position.map((value, index) => value + originDelta[index]);
  if (prototypeRoomDoors.has(id)) original.nodes[id] = [...prototypeRoomDoors.get(id)];
}
original.campusPrototypeGraph = graph;
const graphNodeById = new Map(nodes.map(node => [node.id, node]));
for (const destination of original.destinations) {
  const campusNode = graphNodeById.get(`campus-destination-${destination.id}`);
  if (!campusNode) throw new Error(`Destination ${destination.id} is not represented in the rebuilt graph.`);
  destination.endpoint = [...campusNode.position];
  destination.floor = campusNode.floor;
  destination.campusFloor = campusNode.floor;
  const incoming = edges.find(edge => edge.to === campusNode.id);
  destination.campusAnchorId = incoming ? incoming.from : "prototype-upper-corridor";
  destination.campusCoverage = "inferred-campus-approach";
  if (campusNode.campusBuilding) destination.campusBuilding = campusNode.campusBuilding;
  destination.campusApproachLabel = campusNode.label;
}
fs.writeFileSync(file, `${JSON.stringify(original, null, 2)}\n`);
const svgEscape = value => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const planNodes = new Map(nodes.map(node => [node.id, node]));
const allX = [...limits.map(p => p[0]), ...nodes.map(n => n.position[0]), ...obstacles.flatMap(b => b.shape === "circle" ? [b.centerX - b.radius, b.centerX + b.radius] : [b.minX, b.maxX])];
const allZ = [...limits.map(p => p[1]), ...nodes.map(n => n.position[2]), ...obstacles.flatMap(b => b.shape === "circle" ? [b.centerZ - b.radius, b.centerZ + b.radius] : [b.minZ, b.maxZ])];
const worldBounds = { minX: Math.min(...allX), maxX: Math.max(...allX), minZ: Math.min(...allZ), maxZ: Math.max(...allZ) };
const mapBox = { x: 54, y: 112, width: 820, height: 710 };
const mapScale = Math.min(mapBox.width / (worldBounds.maxX - worldBounds.minX), mapBox.height / (worldBounds.maxZ - worldBounds.minZ));
const mapOffsetX = mapBox.x + (mapBox.width - (worldBounds.maxX - worldBounds.minX) * mapScale) / 2;
const mapOffsetY = mapBox.y + (mapBox.height - (worldBounds.maxZ - worldBounds.minZ) * mapScale) / 2;
const screenPoint = p => [mapOffsetX + (p[0] - worldBounds.minX) * mapScale, mapOffsetY + (worldBounds.maxZ - p[2]) * mapScale];
const pathSegments = edges.filter(edge => edge.kind === "imported-paved-path").map(edge => {
  const a = screenPoint(planNodes.get(edge.from).position), b = screenPoint(planNodes.get(edge.to).position);
  return `M${a[0].toFixed(1)},${a[1].toFixed(1)}L${b[0].toFixed(1)},${b[1].toFixed(1)}`;
}).join("");
const inferredSegments = edges.filter(edge => edge.kind.includes("inferred") && !edge.kind.includes("prototype")).map(edge => {
  const a = screenPoint(planNodes.get(edge.from).position), b = screenPoint(planNodes.get(edge.to).position);
  return `M${a[0].toFixed(1)},${a[1].toFixed(1)}L${b[0].toFixed(1)},${b[1].toFixed(1)}`;
}).join("");
const structureShapes = obstacles.map(b => {
  if (b.shape === "circle") {
    const center = screenPoint([b.centerX, y, b.centerZ]);
    return `<circle cx="${center[0].toFixed(1)}" cy="${center[1].toFixed(1)}" r="${(b.radius * mapScale).toFixed(1)}" class="building"/><text x="${(center[0] - 38).toFixed(1)}" y="${(center[1] + 4).toFixed(1)}" class="building-label">GlassHouseModel</text>`;
  }
  const [left, top] = screenPoint([b.minX, y, b.maxZ]), [right, bottom] = screenPoint([b.maxX, y, b.minZ]);
  return `<rect x="${left.toFixed(1)}" y="${top.toFixed(1)}" width="${(right - left).toFixed(1)}" height="${(bottom - top).toFixed(1)}" class="building"/><text x="${(left + 4).toFixed(1)}" y="${(top + 14).toFixed(1)}" class="building-label">${svgEscape(b.id)}</text>`;
}).join("");
const destinationById = new Map(original.destinations.map(destination => [destination.id, destination]));
const terminalRows = original.destinations.map((destination, index) => ({ index, destination, node: planNodes.get(`campus-destination-${destination.id}`) }));
const destinationMarks = terminalRows.map(item => {
  const { destination, node, index } = item, p = screenPoint(node.position), isPrototype = node.kind.startsWith("prototype-");
  const peers = terminalRows.filter(other => Math.hypot(other.node.position[0] - node.position[0], other.node.position[2] - node.position[2]) < 4.5);
  let labelX = p[0] + 8, labelY = p[1] - 7, leader = "";
  if (peers.length > 1) {
    const peerIndex = peers.findIndex(other => other.destination.id === destination.id);
    const angle = -Math.PI / 2 + 2 * Math.PI * peerIndex / peers.length;
    labelX = p[0] + Math.cos(angle) * 21;
    labelY = p[1] + Math.sin(angle) * 21;
    leader = `<path d="M${p[0].toFixed(1)},${p[1].toFixed(1)}L${labelX.toFixed(1)},${labelY.toFixed(1)}" class="leader"/>`;
  }
  return `${leader}<circle cx="${p[0].toFixed(1)}" cy="${p[1].toFixed(1)}" r="4.2" class="${isPrototype ? "lab" : "destination"}"><title>${svgEscape(destination.name)}: ${node.position.map(v => v.toFixed(1)).join(", ")}</title></circle><text x="${labelX.toFixed(1)}" y="${(labelY + 3).toFixed(1)}" class="marker-number">${String(index + 1).padStart(2, "0")}</text>`;
}).join("");
const startPoint = screenPoint(start);
const destinationList = original.destinations.map((destination, index) => {
  const column = index < 11 ? 0 : 1, row = index % 11;
  const x = 930 + column * 250, yy = 180 + row * 43;
  const node = planNodes.get(`campus-destination-${destination.id}`);
  const building = node?.campusBuilding || destination.campusBuilding || "Outdoor approach (inferred)";
  return `<text x="${x}" y="${yy}" class="list-number">${String(index + 1).padStart(2, "0")}</text><text x="${x + 30}" y="${yy}" class="list-name">${svgEscape(destination.name)}</text><text x="${x + 30}" y="${yy + 15}" class="list-building">${svgEscape(building)}</text>`;
}).join("");
const svg = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="1450" height="940" viewBox="0 0 1450 940" role="img" aria-labelledby="title desc">
<title id="title">Campus floor plan and shortest walking destination approaches</title>
<desc id="desc">Top-down campus plan derived from the imported Unity walk-surface triangles and eight building meshes. The route graph uses weighted Dijkstra distance. Facility assignments and unmarked door approaches are inferred.</desc>
<style>text{font-family:Arial,sans-serif;fill:#24374b}.title{font-size:26px;font-weight:700}.sub{font-size:13px;fill:#526477}.map-bg{fill:#edf4eb;stroke:#9aab97;stroke-width:1.5}.path{fill:none;stroke:#4b9b92;stroke-width:1.05;stroke-opacity:.42;stroke-linecap:round}.inferred{fill:none;stroke:#e39421;stroke-width:2.3;stroke-dasharray:6 4}.building{fill:#8d685e;fill-opacity:.58;stroke:#61443d;stroke-width:1.6}.building-label{font-size:10px;font-weight:700;fill:#fff}.destination{fill:#ea6457;stroke:#fff;stroke-width:1.5}.lab{fill:#7452c7;stroke:#fff;stroke-width:1.5}.start{fill:#1d8b57;stroke:#fff;stroke-width:2}.leader{stroke:#526477;stroke-width:.8;stroke-dasharray:2 1}.marker-number{font-size:9px;font-weight:700;fill:#24374b;stroke:#f7fafc;stroke-width:2px;paint-order:stroke;pointer-events:none}.panel{fill:#fff;stroke:#d8e0e7;stroke-width:1}.section{font-size:15px;font-weight:700}.list-number{font-size:12px;font-weight:700;fill:#14848b}.list-name{font-size:13px;font-weight:700}.list-building{font-size:10px;fill:#627386}.legend{font-size:12px;fill:#44576a}</style>
<rect width="100%" height="100%" fill="#f7fafc"/><text x="36" y="43" class="title">Campus floor plan · modeled routes and destination gates</text>
<text x="38" y="68" class="sub">${nodes.length.toLocaleString()} nodes · ${edges.length.toLocaleString()} edges · ${source.triangles.length.toLocaleString()} imported walk-surface triangles · shortest paths by weighted Dijkstra</text>
<rect x="${(mapBox.x - 12).toFixed(1)}" y="${(mapBox.y - 12).toFixed(1)}" width="${(mapBox.width + 24).toFixed(1)}" height="${(mapBox.height + 24).toFixed(1)}" rx="8" class="map-bg"/>
<path d="${pathSegments}" class="path"/><path d="${inferredSegments}" class="inferred"/>${structureShapes}${destinationMarks}
<path d="M${startPoint[0]},${(startPoint[1] - 8).toFixed(1)}L${(startPoint[0] - 7).toFixed(1)},${(startPoint[1] + 6).toFixed(1)}L${(startPoint[0] + 7).toFixed(1)},${(startPoint[1] + 6).toFixed(1)}Z" class="start"/>
<text x="${(mapBox.x - 2).toFixed(1)}" y="${(mapBox.y + mapBox.height + 42).toFixed(1)}" class="legend">Green triangle: campus entrance start · coral: destination approach · violet: prototype interior destination · orange dash: short inferred connector</text>
<rect x="910" y="112" width="510" height="722" rx="8" class="panel"/><text x="930" y="145" class="section">DESTINATIONS · UNIQUE ENDPOINTS</text>${destinationList}
<text x="930" y="872" class="sub">Facility names are inferred from model appearance; the asset has no semantic room tags.</text>
<text x="930" y="891" class="sub">Parking is an inferred paved-path arrival; no parking lot or bays are modeled.</text>
<text x="36" y="923" class="sub">Route geometry is mesh-derived; door access, facility names, and campus public access require on-site confirmation.</text>
</svg>`;
fs.writeFileSync(path.join(here, "..", "Documentation", "S014_Campus_Floor_Plan.svg"), svg);
console.log(`Generated ${nodes.length} nodes, ${edges.length} edges from ${source.triangles.length} imported walk-surface triangles.`);
console.log(`Terminals: ${nodes.filter(n => n.destinationId).map(n => `${n.destinationId}=${n.position.map(v => v.toFixed(1)).join(",")}`).join(" | ")}`);
