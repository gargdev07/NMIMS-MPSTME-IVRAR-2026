import * as THREE from "three";
import { GLTFLoader } from "./vendor/examples/jsm/loaders/GLTFLoader.js";
import { createRepresentativeInterior } from "./representative-interior.js";
import { auditDestinationRoutes, findShortestPath, validateNavigationGraph } from "./route-graph.js";
import { checkImmersiveArSupport, requestImmersiveArSession } from "./xr-support.js?v=4.6-campus-routing-ready";

const canvas = document.getElementById("campusView");
const destinationElement = document.getElementById("destination");
const floorElement = document.getElementById("floor");
const modelStatusElement = document.getElementById("modelStatus");
const progressElement = document.getElementById("progress");
const payloadStatusElement = document.getElementById("payloadStatus");
const coverageStatusElement = document.getElementById("coverageStatus");
const instructionElement = document.getElementById("instruction");
const nextWaypointElement = document.getElementById("nextWaypoint");
const distanceElement = document.getElementById("distance");
const routeProgressElement = document.getElementById("routeProgress");
const currentFloorElement = document.getElementById("currentFloor");
const noticeElement = document.getElementById("notice");
const arStatusElement = document.getElementById("arStatus");
const toastElement = document.getElementById("toast");
const startButton = document.getElementById("startRoute");
const xrPreviewButton = document.getElementById("xrPreview");
const routeModeButton = document.getElementById("routeMode");
const pauseButton = document.getElementById("pauseRoute");
const restartButton = document.getElementById("restartRoute");
const resetButton = document.getElementById("resetRoute");
const overviewButton = document.getElementById("overview");
const destinationSelect = document.getElementById("destinationSelect");
const changeDestinationButton = document.getElementById("changeDestination");

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x081722);
const camera = new THREE.PerspectiveCamera(58, 1, 0.1, 3000);
const renderer = new THREE.WebGLRenderer({
  canvas,
  antialias: false,
  alpha: true,
  powerPreference: "low-power"
});
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.1;
scene.add(new THREE.HemisphereLight(0xc6e9ff, 0x26303b, 2.1));
const sun = new THREE.DirectionalLight(0xffedcf, 2.1);
sun.position.set(-80, 120, -40);
scene.add(sun);

const campusRoot = new THREE.Group();
const routeRoot = new THREE.Group();
const xrPreviewRoot = new THREE.Group();
scene.add(xrPreviewRoot);
xrPreviewRoot.add(campusRoot, routeRoot);
const routeRaycaster = new THREE.Raycaster();
const clock = new THREE.Clock();
const eyeHeight = 1.6;
const travelSpeed = 1.8;
const collisionRadius = 0.24;
const PROTOTYPE_INTERIOR_DESTINATIONS = new Set(["lab-101", "lab-102", "lab-103", "student-help"]);
const routeColor = 0x36e3ff;
let routeData;
let selectedDestination;
let activeSessionId = "";
let route = [];
let routeIndex = 0;
let currentPosition;
let running = false;
let paused = false;
let hasStarted = false;
let handoffPayload;
let campusBounds;
let campusCollisionObjects = [];
let interiorCollisionObjects = [];
let interiorBounds;
let modelReady = false;
let handoffReady = false;
let toastTimeout = 0;
let cameraTarget = new THREE.Vector3();
let lastBlock = null;
let xrSession = null;
let xrStartPending = false;
// Campus-scale routing is the real phone destination flow. Keep the small
// representative cutaway as an explicit optional preview only.
let routeMode = "campus";
let immersiveArCapability = { supported: false, reason: "not-checked" };
let pendingStart = false;
let campusModelFailed = false;
let routeDetourCount = 0;
let nextDetourId = 1;

function setNotice(message, kind) {
  noticeElement.textContent = message;
  noticeElement.className = "notice" + (kind ? " " + kind : "");
}

function setToast(message) {
  toastElement.textContent = message;
  toastElement.classList.add("visible");
  window.clearTimeout(toastTimeout);
  toastTimeout = window.setTimeout(() => toastElement.classList.remove("visible"), 2600);
}

function vectorFromArray(values) {
  return new THREE.Vector3(values[0], values[1], values[2]);
}

function validateRouteData(data) {
  if (data.version !== "2.0" || data.destinations.length !== 22) {
    throw new Error("The browser route catalogue is incomplete or incompatible.");
  }
  let graphNodes;
  try {
    graphNodes = validateNavigationGraph(data.navigationGraph);
  } catch (error) {
    throw new Error("Representative route graph is invalid: " + error.message);
  }
  for (const [id, position] of Object.entries(data.nodes)) {
    const node = graphNodes.get(id);
    if (!node || node.position.some((value, axis) => value !== position[axis])) {
      throw new Error("Representative route node " + id + " does not match its graph position.");
    }
  }
  if (!data.representativeInterior ||
      JSON.stringify(data.representativeInterior.origin) !== JSON.stringify(data.integrationOrigin)) {
    throw new Error("The representative interior layout does not match the route coordinate origin.");
  }

  let campusNodes;
  try {
    campusNodes = validateNavigationGraph(data.campusPrototypeGraph);
  } catch (error) {
    throw new Error("Campus prototype graph is invalid: " + error.message);
  }
  const campusIds = new Set();
  const campusPositions = new Set();
  for (const destination of data.destinations) {
    const node = campusNodes.get(destination.campusNodeId);
    if (!node || node.destinationId !== destination.id ||
        node.floor !== destination.campusFloor || destination.campusCoverage !== "inferred-campus-approach" ||
        campusIds.has(destination.campusNodeId)) {
      throw new Error("Campus prototype endpoint is missing or mislabeled for " + destination.id + ".");
    }
    campusIds.add(destination.campusNodeId);
    const positionKey = node.position.join(",");
    if (campusPositions.has(positionKey)) {
      throw new Error("Campus prototype destinations must use unique spatial endpoints.");
    }
    campusPositions.add(positionKey);
    findShortestPath(data.campusPrototypeGraph, data.campusPrototypeGraph.startNodeId, destination.campusNodeId);
  }

  const ids = new Set();
  const endpoints = new Set();
  for (const destination of data.destinations) {
    if (ids.has(destination.id)) throw new Error("Duplicate destination ID: " + destination.id);
    ids.add(destination.id);
    if (!data.floorNames[destination.floor]) throw new Error("Unknown destination floor: " + destination.id);
    const key = destination.endpoint.join(",");
    if (endpoints.has(key)) throw new Error("Duplicate destination endpoint: " + destination.id);
    endpoints.add(key);
  }
  auditDestinationRoutes(data);
}

function waypoint(id, point, label, floor, transitionTo) {
  return {
    id,
    position: vectorFromArray(point),
    label,
    floor,
    transitionTo: transitionTo || ""
  };
}

function buildRoute(destination, mode = routeMode) {
  if (mode === "representative" && !PROTOTYPE_INTERIOR_DESTINATIONS.has(destination.id)) {
    throw new Error("Only Labs 101–103 and the representative help desk have prototype interior routes.");
  }
  const graph = mode === "campus" ? routeData.campusPrototypeGraph : routeData.navigationGraph;
  const endpointId = mode === "campus" ? destination.campusNodeId : destination.nodeId;
  const graphRoute = findShortestPath(
    graph,
    graph.startNodeId,
    endpointId
  );
  if (graphRoute.destinationId !== destination.id) {
    throw new Error("The selected destination does not match its route graph endpoint.");
  }
  const points = graphRoute.waypoints.map(point =>
    waypoint(point.id, point.position, point.label, point.floor, point.transitionTo));
  if (points.length < 2) throw new Error("The selected destination route is incomplete.");
  for (let index = 1; index < points.length; index++) {
    if (points[index - 1].position.distanceTo(points[index].position) <= 0.01) {
      throw new Error("The route contains a zero-length segment at " + points[index].label + ".");
    }
  }
  const expectedFloor = mode === "campus" ? destination.campusFloor : destination.floor;
  if (points[points.length - 1].floor !== expectedFloor) {
    throw new Error("The route's final floor does not match its selected coverage mode.");
  }
  return points;
}

function auditRoutes() {
  return auditDestinationRoutes(routeData);
}

function auditCampusRoutes() {
  return routeData.destinations.map(destination => {
    const result = findShortestPath(
      routeData.campusPrototypeGraph,
      routeData.campusPrototypeGraph.startNodeId,
      destination.campusNodeId
    );
    if (result.destinationId !== destination.id ||
        result.waypoints.at(-1).floor !== destination.campusFloor) {
      throw new Error("Invalid inferred campus approach route for " + destination.id + ".");
    }
    return {
      id: destination.id,
      nodeId: destination.campusNodeId,
      anchorId: destination.campusAnchorId,
      coverage: destination.campusCoverage,
      lengthMeters: result.lengthMeters,
      waypointCount: result.waypoints.length,
      nodeIds: result.nodeIds
    };
  });
}

function updateRouteGraphics() {
  routeRoot.clear();
  if (route.length < 2) return;

  const linePoints = route.map(point => point.position.clone().add(new THREE.Vector3(0, 0.08, 0)));
  const lineGeometry = new THREE.BufferGeometry().setFromPoints(linePoints);
  const line = new THREE.Line(lineGeometry, new THREE.LineBasicMaterial({
    color: routeColor,
    transparent: true,
    opacity: 0.9,
    depthTest: false
  }));
  line.renderOrder = 10;
  routeRoot.add(line);

  route.forEach((point, index) => {
    if (index !== 0 && index !== route.length - 1 && !point.transitionTo) return;
    const marker = new THREE.Mesh(
      new THREE.SphereGeometry(index === route.length - 1 ? 0.28 : 0.17, 10, 8),
      new THREE.MeshBasicMaterial({
        color: index === route.length - 1 ? 0xffd479 : routeColor,
        depthTest: false
      })
    );
    marker.position.copy(point.position).add(new THREE.Vector3(0, 0.16, 0));
    marker.renderOrder = 11;
    routeRoot.add(marker);
  });
}

function setCameraForRoute(position, targetPosition, immediate) {
  const eye = position.clone().add(new THREE.Vector3(0, eyeHeight, 0));
  const target = targetPosition.clone().add(new THREE.Vector3(0, eyeHeight * 0.78, 0));
  cameraTarget.copy(target);
  if (immediate) camera.position.copy(eye);
  else camera.position.lerp(eye, 0.18);
  camera.lookAt(cameraTarget);
}

function setRouteStatus() {
  const target = route[routeIndex];
  if (!target) {
    nextWaypointElement.textContent = "Arrived";
    distanceElement.textContent = "0 m";
    routeProgressElement.textContent = route.length + " / " + route.length;
    return;
  }

  const from = currentPosition || route[0].position;
  nextWaypointElement.textContent = target.label;
  distanceElement.textContent = from.distanceTo(target.position).toFixed(1) + " m";
  routeProgressElement.textContent = Math.min(routeIndex, route.length - 1) + " / " + (route.length - 1);
  currentFloorElement.textContent = routeData.floorNames[target.floor];
  floorElement.textContent = routeMode === "campus"
    ? "Campus approach level: " + routeData.floorNames[target.floor] + " · interior floor not mapped"
    : "Destination floor: " + routeData.floorNames[selectedDestination.floor];
}

function refreshControls() {
  const routeAvailable = handoffReady && route.length > 1;
  const modelRouteAvailable = routeAvailable && modelReady && !campusModelFailed;
  startButton.disabled = !routeAvailable || campusModelFailed || hasStarted || pendingStart || routeIndex >= route.length;
  pauseButton.disabled = !routeAvailable || !hasStarted || routeIndex >= route.length;
  resetButton.disabled = !modelRouteAvailable;
  restartButton.disabled = !modelRouteAvailable;
  destinationSelect.disabled = !modelRouteAvailable;
  changeDestinationButton.disabled = !modelRouteAvailable;
  routeModeButton.disabled = !modelRouteAvailable ||
    (routeMode === "campus" && !PROTOTYPE_INTERIOR_DESTINATIONS.has(selectedDestination.id));
}

function createSessionId() {
  if (!globalThis.crypto || typeof globalThis.crypto.getRandomValues !== "function") {
    throw new Error("Secure random session IDs are unavailable in this browser.");
  }
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  return Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
}

function startRoute() {
  if (!handoffReady || !route.length || hasStarted || routeIndex >= route.length) return;
  if (campusModelFailed) {
    setNotice("The campus model failed to load, so virtual movement cannot start.", "warning");
    return;
  }
  if (!modelReady) {
    pendingStart = true;
    startButton.textContent = "STARTING WHEN CAMPUS LOADS…";
    instructionElement.textContent = "Start accepted. Movement will begin at the entrance when the campus model finishes loading.";
    setNotice("Start accepted. Waiting for campus geometry so route movement can check for visible obstacles.", "");
    refreshControls();
    return;
  }
  if (!currentPosition) currentPosition = route[0].position.clone();
  hasStarted = true;
  pendingStart = false;
  routeDetourCount = 0;
  nextDetourId = 1;
  startButton.textContent = "ROUTE IN PROGRESS";
  paused = false;
  running = true;
  pauseButton.textContent = "PAUSE";
  instructionElement.textContent = "Following the connected virtual waypoints.";
  setNotice("Virtual route active. The phone camera is simulating movement; Unity pose is not streamed.", "success");
  refreshControls();
}

function resetRoute(startImmediately, renewSession = false) {
  if (!route.length) return;
  pendingStart = false;
  startButton.textContent = "START VIRTUAL ROUTE";
  running = false;
  paused = false;
  hasStarted = false;
  lastBlock = null;
  routeDetourCount = 0;
  nextDetourId = 1;
  if (renewSession) {
    try {
      activeSessionId = createSessionId();
    } catch (error) {
      refreshControls();
      setNotice("Could not create a new virtual session: " + error.message, "warning");
      return;
    }
  }
  currentPosition = route[0].position.clone();
  routeIndex = 1;
  hasStarted = !!startImmediately;
  running = hasStarted;
  pauseButton.textContent = running ? "PAUSE" : "RESUME";
  instructionElement.textContent = running
    ? "Following the connected virtual waypoints."
    : "Ready at the exterior route start.";
  payloadStatusElement.textContent = "Virtual session " + activeSessionId + " · " +
    route.length + " connected waypoints · " +
    (routeMode === "campus" ? "inferred campus approach" : routeData.floorNames[selectedDestination.floor]);
  payloadStatusElement.className = "success";
  coverageStatusElement.textContent = getCoverageMessage(selectedDestination);
  setRouteStatus();
  setCameraForRoute(currentPosition, route[routeIndex].position, true);
  setNotice(running
    ? "Virtual route active. The phone camera is simulating movement; Unity pose is not streamed."
    : "Route reset to its start. The QR handoff and Unity session are unchanged.", running ? "success" : "");
  refreshControls();
}

function changeDestination() {
  const destination = routeData.destinations.find(item => item.id === destinationSelect.value);
  if (!destination) {
    setNotice("The selected virtual destination is not in the route catalogue.", "warning");
    return;
  }
  if (routeMode === "representative" && !PROTOTYPE_INTERIOR_DESTINATIONS.has(destination.id)) {
    routeMode = "campus";
    routeModeButton.textContent = "USE REPRESENTATIVE CUTAWAY ROUTES";
  }
  running = false;
  pendingStart = false;
  startButton.textContent = "START VIRTUAL ROUTE";
  paused = false;
  hasStarted = false;
  route = [];
  routeIndex = 0;
  currentPosition = null;
  lastBlock = null;
  routeDetourCount = 0;
  nextDetourId = 1;
  routeRoot.clear();
  refreshControls();
  try {
    const nextRoute = buildRoute(destination);
    const nextSessionId = createSessionId();
    selectedDestination = destination;
    route = nextRoute;
    activeSessionId = nextSessionId;
    routeIndex = 1;
    currentPosition = route[0].position.clone();
    destinationElement.textContent = destination.name;
    floorElement.textContent = routeMode === "campus"
      ? "Campus approach level: Ground floor · interior floor not mapped"
      : "Destination floor: " + routeData.floorNames[destination.floor];
    payloadStatusElement.textContent = "Virtual session " + activeSessionId + " · " +
      route.length + " connected waypoints · " + routeData.floorNames[destination.floor];
    payloadStatusElement.className = "success";
    coverageStatusElement.textContent = getCoverageMessage(destination);
    instructionElement.textContent = "A new virtual route is ready. Start to follow the selected destination.";
    updateRouteGraphics();
    setRouteStatus();
    setCameraForRoute(currentPosition, route[routeIndex].position, true);
    setNotice("Virtual destination changed. This does not change the original QR handoff or Unity kiosk session.", "");
    refreshControls();
  } catch (error) {
    selectedDestination = destination;
    destinationElement.textContent = destination.name;
    floorElement.textContent = "No route loaded";
    payloadStatusElement.textContent = error.message;
    payloadStatusElement.className = "warning";
    coverageStatusElement.textContent = "No route coverage is available.";
    instructionElement.textContent = "The selected destination route is unavailable.";
    setNotice(error.message, "warning");
    refreshControls();
  }
}

function populateDestinationSelect() {
  destinationSelect.replaceChildren();
  for (const destination of routeData.destinations) {
    const option = document.createElement("option");
    option.value = destination.id;
    option.textContent = destination.name + " — " + routeData.floorNames[destination.floor];
    destinationSelect.append(option);
  }
  destinationSelect.value = selectedDestination.id;
}

function getCoverageMessage(destination) {
  if (routeMode === "campus") {
    const startNode = routeData.campusPrototypeGraph.nodes.find(node =>
      node.id === routeData.campusPrototypeGraph.startNodeId);
    const startLabel = startNode ? startNode.label : "the inferred campus starting point";
    return "Starts at " + startLabel + ". Generic entrance assignment and outdoor links are inferred; the named room and its indoor floor are not mapped.";
  }
  return destination.coverage === "prototype-outdoor"
    ? "Prototype exterior route — not a verified campus-wide path."
    : "Representative interior route — room layout and physical walkability are not verified.";
}

function switchRouteMode() {
  const nextMode = routeMode === "representative" ? "campus" : "representative";
  try {
    const nextRoute = buildRoute(selectedDestination, nextMode);
    const nextSessionId = createSessionId();
    running = false;
    pendingStart = false;
    startButton.textContent = "START VIRTUAL ROUTE";
    paused = false;
    hasStarted = false;
    routeMode = nextMode;
    route = nextRoute;
    activeSessionId = nextSessionId;
    routeIndex = 1;
    currentPosition = route[0].position.clone();
    routeModeButton.textContent = routeMode === "campus"
      ? "USE REPRESENTATIVE CUTAWAY ROUTES"
      : "USE CAMPUS PROTOTYPE ROUTES";
    pauseButton.textContent = "PAUSE";
    instructionElement.textContent = routeMode === "campus"
      ? "A campus prototype approach route is ready from " +
        routeData.campusPrototypeGraph.nodes.find(node => node.id === routeData.campusPrototypeGraph.startNodeId).label +
        ". Outdoor connectors and named facility assignments are inferred."
      : "A representative cutaway route is ready. Room placement is illustrative.";
    payloadStatusElement.textContent = "Virtual session " + activeSessionId + " · " +
      route.length + " connected waypoints · " +
      (routeMode === "campus" ? "inferred campus approach" : routeData.floorNames[selectedDestination.floor]);
    payloadStatusElement.className = "success";
    coverageStatusElement.textContent = getCoverageMessage(selectedDestination);
    updateRouteGraphics();
    setRouteStatus();
    setCameraForRoute(currentPosition, route[routeIndex].position, true);
    setNotice(routeMode === "campus"
      ? "Campus prototype routing selected. Routes end at inferred building approaches and do not claim verified outdoor walkability or room-level access."
      : "Representative cutaway routing selected. This local route is not a surveyed campus route.", "warning");
    refreshControls();
  } catch (error) {
    setNotice("Could not switch route coverage: " + error.message, "warning");
  }
}

function focusCampusOverview() {
  if (!campusBounds) return;
  if (running) {
    running = false;
    paused = true;
    pauseButton.textContent = "RESUME";
  }
  refreshControls();
  const center = campusBounds.getCenter(new THREE.Vector3());
  const size = campusBounds.getSize(new THREE.Vector3());
  const radius = Math.max(size.x, size.y, size.z) * 0.62;
  camera.position.set(center.x + radius * 0.62, center.y + radius * 0.52, center.z + radius * 0.82);
  camera.lookAt(center);
  cameraTarget.copy(center);
  instructionElement.textContent = "Campus overview. Select Start Virtual Route to follow the QR destination.";
}

function isBlockedByCampus(from, to) {
  if (routeMode === "campus") return isBlockedByNamedBuildingBounds(from, to);
  const delta = to.clone().sub(from);
  const length = delta.length();
  if (length < 0.01 || (campusCollisionObjects.length === 0 && interiorCollisionObjects.length === 0)) return false;

  const origin = from.clone().add(new THREE.Vector3(0, 0.88, 0));
  const target = to.clone().add(new THREE.Vector3(0, 0.88, 0));
  const direction = target.sub(origin).normalize();
  routeRaycaster.set(origin, direction);
  routeRaycaster.near = collisionRadius;
  routeRaycaster.far = length + collisionRadius;
  const campusHit = routeRaycaster.intersectObjects(campusCollisionObjects, false).find(candidate =>
    candidate.point.y >= from.y + 0.15 &&
    candidate.point.y <= from.y + 1.45 &&
    !isInsideRepresentativeInterior(candidate.point)
  );
  const interiorHit = routeRaycaster.intersectObjects(interiorCollisionObjects, false).find(candidate =>
    candidate.point.y >= from.y + 0.15 &&
    candidate.point.y <= from.y + 1.45
  );
  const hit = !interiorHit || (campusHit && campusHit.distance < interiorHit.distance)
    ? campusHit
    : interiorHit;
  lastBlock = hit
    ? { object: hit.object.name || hit.object.type, point: hit.point.toArray(), distance: hit.distance }
    : null;
  return !!hit;
}

function isBlockedByNamedBuildingBounds(from, to) {
  const bounds = routeData && routeData.campusGeometry && routeData.campusGeometry.buildingBoundsXZ;
  if (!Array.isArray(bounds) || bounds.length === 0) return false;
  const delta = to.clone().sub(from);
  const steps = Math.max(1, Math.ceil(delta.length() / 0.5));
  const margin = 1.0;
  for (let step = 1; step <= steps; step++) {
    const t = step / steps;
    const x = from.x + delta.x * t;
    const z = from.z + delta.z * t;
    const building = bounds.find(item =>
      x >= item.min[0] - margin && x <= item.max[0] + margin &&
      z >= item.min[1] - margin && z <= item.max[1] + margin
    );
    if (!building) continue;
    lastBlock = {
      object: "known building footprint " + building.id,
      point: [x, from.y + 0.88, z],
      distance: delta.length() * t
    };
    return true;
  }
  lastBlock = null;
  return false;
}

function isInsideRepresentativeInterior(point) {
  return interiorBounds &&
    point.x > interiorBounds.minX && point.x < interiorBounds.maxX &&
    point.y >= interiorBounds.minY - 0.05 && point.y <= interiorBounds.maxY &&
    point.z > interiorBounds.minZ && point.z < interiorBounds.maxZ;
}

function findCampusObstacleDetour(from, target) {
  if (!lastBlock || !Array.isArray(lastBlock.point) || routeDetourCount >= 12) return null;
  const hit = vectorFromArray(lastBlock.point);
  const direction = target.clone().sub(from);
  direction.y = 0;
  if (direction.lengthSq() < 0.001) return null;
  direction.normalize();
  const sides = [
    new THREE.Vector3(-direction.z, 0, direction.x),
    new THREE.Vector3(direction.z, 0, -direction.x)
  ];
  const candidates = [];
  for (const radius of [2, 3.5, 5, 7, 10]) {
    for (const side of sides) {
      for (const along of [-0.8, 0.8]) {
        const point = hit.clone()
          .addScaledVector(side, radius)
          .addScaledVector(direction, along);
        point.y = from.y;
        if (point.distanceTo(from) < 0.75) continue;
        if (campusBounds && !campusBounds.clone().expandByScalar(-0.5).containsPoint(point)) continue;
        candidates.push({ point, score: point.distanceTo(from) + point.distanceTo(target) * 0.35 });
      }
    }
  }
  candidates.sort((left, right) => left.score - right.score);
  for (const candidate of candidates) {
    if (!isBlockedByCampus(from, candidate.point)) return candidate.point;
  }
  return null;
}

function advanceNavigation(deltaTime) {
  if (!running || !route.length || routeIndex >= route.length) return;

  const target = route[routeIndex];
  const remaining = target.position.clone().sub(currentPosition);
  const distance = remaining.length();
  const step = Math.min(distance, travelSpeed * Math.min(deltaTime, 0.05));
  if (distance > 0.001) {
    const nextPosition = currentPosition.clone().addScaledVector(remaining.normalize(), step);
    if (isBlockedByCampus(currentPosition, nextPosition)) {
      const blockedBy = lastBlock && lastBlock.object ? lastBlock.object : "campus geometry";
      const detour = findCampusObstacleDetour(currentPosition, target.position);
      if (detour) {
        const id = "geometry-detour-" + nextDetourId++;
        route.splice(routeIndex, 0, waypoint(
          id,
          detour.toArray(),
          "Clear side step around visible campus geometry",
          target.floor,
          ""
        ));
        routeDetourCount++;
        updateRouteGraphics();
        instructionElement.textContent = "Obstacle ahead. Following a short geometry-checked detour around " + blockedBy + ".";
        setNotice("Route adjusted around visible campus geometry. The detour is inferred from the imported model.", "warning");
        setToast("Adjusting around a visible obstacle");
        setRouteStatus();
        refreshControls();
        return;
      }
      running = false;
      paused = true;
      pauseButton.textContent = "RESUME";
      instructionElement.textContent = "Movement stopped at campus geometry; this path needs review.";
      const obstruction = lastBlock && lastBlock.object
        ? " at " + lastBlock.object + " (" + lastBlock.point.map(value => value.toFixed(1)).join(", ") + ")"
        : "";
      setNotice("Route blocked by visible campus geometry" + obstruction + ". The virtual camera did not pass through it.", "warning");
      refreshControls();
      return;
    }
    currentPosition.copy(nextPosition);
  }

  if (currentPosition.distanceTo(target.position) <= 0.04) {
    currentPosition.copy(target.position);
    if (target.transitionTo) {
      currentFloorElement.textContent = routeData.floorNames[target.transitionTo];
      setToast("Floor transition · " + routeData.floorNames[target.transitionTo]);
    }
    routeIndex++;
    if (routeIndex >= route.length) {
      running = false;
      paused = false;
      startButton.textContent = "DESTINATION REACHED";
      instructionElement.textContent = "Destination reached.";
      setNotice("Arrived at " + selectedDestination.name + ".", "success");
      setToast("Destination reached");
      refreshControls();
    }
  }

  setRouteStatus();
  const next = route[Math.min(routeIndex, route.length - 1)];
  if (routeIndex >= route.length) {
    const previous = route[route.length - 2].position;
    const forward = route[route.length - 1].position.clone().sub(previous);
    forward.y = 0;
    if (forward.lengthSq() > 0.001) forward.normalize();
    setCameraForRoute(currentPosition, currentPosition.clone().add(forward.multiplyScalar(2)), false);
  } else if (next) {
    setCameraForRoute(currentPosition, next.position, false);
  }
}

function frame() {
  const deltaTime = clock.getDelta();
  advanceNavigation(deltaTime);
  renderer.render(scene, camera);
}

function resize() {
  const width = window.innerWidth;
  const height = window.innerHeight;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

async function loadCampusModel() {
  modelStatusElement.textContent = "Loading optimized campus model…";
  const loader = new GLTFLoader();
  const gltf = await new Promise((resolve, reject) => {
    loader.load(
      "./campus/Unity2Skfb-mobile.glb",
      resolve,
      event => {
        if (event.lengthComputable) {
          progressElement.textContent = Math.round(event.loaded / event.total * 100) + "%";
        }
      },
      reject
    );
  });

  campusRoot.add(gltf.scene);
  const unityTransform = routeData.campusGeometry.unitySceneTransform;
  gltf.scene.scale.set(-1, 1, 1);
  gltf.scene.position.set(...unityTransform.position);
  gltf.scene.updateMatrixWorld(true);
  campusBounds = new THREE.Box3().setFromObject(gltf.scene);
  if (campusBounds.isEmpty()) throw new Error("The loaded campus model has no renderable geometry.");

  gltf.scene.traverse(object => {
    if (!object.isMesh || !object.geometry) return;
    object.frustumCulled = true;
    object.material = Array.isArray(object.material)
      ? object.material.map(material => material.clone())
      : object.material.clone();
    const materials = Array.isArray(object.material) ? object.material : [object.material];
    materials.forEach(material => { material.side = THREE.DoubleSide; });
    campusCollisionObjects.push(object);
  });

  const interior = createRepresentativeInterior(THREE, routeData.representativeInterior);
  campusRoot.add(interior.group);
  interior.group.updateMatrixWorld(true);
  interiorBounds = interior.bounds;
  interiorCollisionObjects = interior.collisionObjects;
  const fullBounds = campusBounds.clone().union(new THREE.Box3().setFromObject(interior.group));
  campusBounds = fullBounds;

  modelReady = true;
  modelStatusElement.textContent = "Campus model and representative interior loaded";
  progressElement.textContent = "100%";
  refreshControls();
  return campusBounds;
}

function readHandoff(data) {
  const url = new URL(window.location.href);
  const query = new URLSearchParams(url.search);
  const fragment = new URLSearchParams(url.hash.replace(/^#\??/, ""));
  const parameters = query.has("s") || query.has("d") || query.has("payload") || query.has("r") || query.has("f") || query.has("v")
    ? query
    : fragment;
  const encoded = parameters.get("payload");
  let payload;

  if (parameters.has("payload")) {
    try {
      if (!encoded) throw new Error();
      const normalized = encoded.replace(/-/g, "+").replace(/_/g, "/");
      if (!/^[A-Za-z0-9+/]*={0,2}$/.test(normalized)) throw new Error();
      const unpadded = normalized.replace(/=+$/, "");
      const padding = normalized.length - unpadded.length;
      if (unpadded.length % 4 === 1 ||
          (padding > 0 && (normalized.length % 4 !== 0 || padding !== (4 - unpadded.length % 4) % 4))) {
        throw new Error();
      }
      const padded = unpadded + "=".repeat((4 - unpadded.length % 4) % 4);
      const bytes = Uint8Array.from(atob(padded), character => character.charCodeAt(0));
      payload = JSON.parse(new TextDecoder().decode(bytes));
    } catch (error) {
      throw new Error("The QR payload is malformed; scan a current destination QR code.");
    }
  } else {
    const destination = data.destinations.find(candidate => candidate.id === parameters.get("d"));
    if (!parameters.get("s") || !parameters.get("r") || !parameters.get("d") ||
        !parameters.get("f") || !parameters.get("v")) {
      throw new Error("The QR handoff is missing required session or route parameters.");
    }
    if (!destination) throw new Error("The QR handoff contains an unsupported destination ID.");
    payload = {
      sessionId: parameters.get("s"),
      routeId: parameters.get("r"),
      destinationId: destination.id,
      destinationName: destination.name,
      targetFloor: parameters.get("f"),
      routeVersion: parameters.get("v")
    };
  }

  if (!payload || typeof payload !== "object" || Array.isArray(payload) ||
      typeof payload.sessionId !== "string" || !/^[A-Za-z0-9-]{8,128}$/.test(payload.sessionId) ||
      typeof payload.routeId !== "string" || typeof payload.destinationId !== "string" ||
      typeof payload.destinationName !== "string" || typeof payload.targetFloor !== "string" ||
      typeof payload.routeVersion !== "string") {
    throw new Error("The QR handoff is missing required fields or contains invalid values.");
  }
  if (payload.routeVersion !== data.version) {
    throw new Error("The QR handoff uses unsupported version " + payload.routeVersion + ".");
  }

  const destination = data.destinations.find(candidate => candidate.id === payload.destinationId);
  if (!destination) {
    throw new Error("The QR handoff contains an unsupported destination ID.");
  }
  if (payload.routeId !== "CampusRoute_" + destination.id ||
      payload.destinationName !== destination.name || payload.targetFloor !== destination.floor) {
    throw new Error("Destination, floor, and route ID do not match the campus route catalogue.");
  }
  return { payload, destination };
}

async function startXrPreview() {
  if (xrSession) {
    await xrSession.end();
    return;
  }
  if (xrStartPending) return;
  if (!immersiveArCapability.supported) {
    const reason = immersiveArCapability.reason === "secure-context-required"
      ? "Immersive AR requires a secure HTTPS context."
      : "Immersive AR is unavailable in this browser or device.";
    arStatusElement.textContent = reason + " Virtual 3D navigation remains available without AR.";
    setNotice(reason + " Virtual 3D navigation remains available without AR.", "warning");
    return;
  }
  xrStartPending = true;
  xrPreviewButton.disabled = true;
  xrPreviewButton.textContent = "STARTING REAL IMMERSIVE AR…";
  arStatusElement.textContent = "Tap received. Requesting the immersive AR camera session…";
  setNotice("Starting the immersive AR preview…", "success");
  try {
    // requestSession must be invoked before the first await in this tap path;
    // mobile browsers can otherwise discard the transient user activation.
    const sessionRequest = requestImmersiveArSession(window, {
      requiredFeatures: ["local"],
      optionalFeatures: ["local-floor", "dom-overlay"],
      domOverlay: { root: document.body }
    });
    const session = await sessionRequest;
    xrSession = session;
    renderer.xr.enabled = true;
    renderer.xr.setReferenceSpaceType("local");
    await renderer.xr.setSession(session);
    scene.background = null;
    xrPreviewRoot.scale.setScalar(0.015);
    const xrCamera = renderer.xr.getCamera(camera);
    xrCamera.updateMatrixWorld(true);
    const viewer = new THREE.Vector3();
    const forward = new THREE.Vector3();
    xrCamera.getWorldPosition(viewer);
    xrCamera.getWorldDirection(forward);
    const target = viewer.add(forward.multiplyScalar(2.2)).add(new THREE.Vector3(0, -0.35, 0));
    const center = campusBounds ? campusBounds.getCenter(new THREE.Vector3()) : new THREE.Vector3();
    xrPreviewRoot.position.copy(target).addScaledVector(center, -0.015);
    xrPreviewButton.textContent = "EXIT IMMERSIVE AR";
    xrPreviewButton.disabled = false;
    arStatusElement.textContent = "Immersive AR session is active. Model scale and placement are illustrative and not campus-aligned.";
    setNotice("Immersive AR preview opened. Model scale and placement are illustrative; no surveyed campus origin is available.", "warning");
    session.addEventListener("end", () => {
      xrSession = null;
      renderer.xr.enabled = false;
      xrPreviewRoot.position.set(0, 0, 0);
      xrPreviewRoot.rotation.set(0, 0, 0);
      xrPreviewRoot.scale.setScalar(1);
      scene.background = new THREE.Color(0x081722);
      xrPreviewButton.textContent = "OPEN IMMERSIVE AR PREVIEW";
      xrPreviewButton.disabled = !immersiveArCapability.supported;
      arStatusElement.textContent = "Immersive AR preview ended. Virtual 3D navigation remains available without AR.";
      setNotice("Immersive AR preview ended. Virtual 3D navigation remains available without AR.", "");
    }, { once: true });
  } catch (error) {
    if (xrSession) {
      try { await xrSession.end(); } catch (_) { /* Session teardown may already be in progress. */ }
    }
    xrSession = null;
    renderer.xr.enabled = false;
    scene.background = new THREE.Color(0x081722);
    xrPreviewButton.textContent = "OPEN IMMERSIVE AR PREVIEW";
    xrPreviewButton.disabled = !immersiveArCapability.supported;
    arStatusElement.textContent = "Immersive AR session could not start: " + error.message + ". Virtual 3D navigation remains available without AR.";
    setNotice("Could not start immersive AR: " + error.message + ". Virtual 3D navigation remains available without AR.", "warning");
  } finally {
    xrStartPending = false;
  }
}

async function checkOptionalAr() {
  const capability = await checkImmersiveArSupport(window);
  immersiveArCapability = capability;
  if (!capability.supported) {
    xrPreviewButton.disabled = true;
    const messages = {
      "secure-context-required": "Immersive AR requires HTTPS or another secure browser context.",
      "webxr-unavailable": "This browser does not expose WebXR immersive AR.",
      "device-or-browser-unsupported": "This browser or device does not support immersive AR.",
      "capability-check-failed": "Immersive AR capability could not be checked: " +
        (capability.error ? capability.error.message : "unknown error") + "."
    };
    arStatusElement.textContent = (messages[capability.reason] || "Immersive AR is unavailable.") +
      " Virtual 3D navigation remains available without AR.";
    return;
  }
  xrPreviewButton.disabled = false;
  arStatusElement.textContent = "Immersive AR preview is available. Model placement is illustrative and not campus-aligned.";
}

async function initialize() {
  resize();
  window.addEventListener("resize", resize);
  startButton.addEventListener("click", startRoute);
  pauseButton.addEventListener("click", () => {
    if (running && hasStarted) {
      running = false;
      paused = true;
      pauseButton.textContent = "RESUME";
      setNotice("Virtual navigation paused.", "");
    } else if (paused && hasStarted && routeIndex < route.length) {
      running = true;
      paused = false;
      pauseButton.textContent = "PAUSE";
      setNotice("Virtual route active. The phone camera is simulating movement; Unity pose is not streamed.", "success");
    }
    refreshControls();
  });
  resetButton.addEventListener("click", () => resetRoute(false, true));
  restartButton.addEventListener("click", () => resetRoute(true, true));
  changeDestinationButton.addEventListener("click", changeDestination);
  overviewButton.addEventListener("click", focusCampusOverview);
  routeModeButton.addEventListener("click", switchRouteMode);
  renderer.setAnimationLoop(frame);
  xrPreviewButton.addEventListener("click", startXrPreview);
  checkOptionalAr();

  try {
  const [routeResponse] = await Promise.all([fetch("./route-data.json?v=4.6-campus-routing-ready")]);
    if (!routeResponse.ok) throw new Error("Could not load the campus route catalogue (" + routeResponse.status + ").");
    routeData = await routeResponse.json();
    validateRouteData(routeData);
    const handoff = readHandoff(routeData);
    handoffPayload = handoff.payload;
    activeSessionId = handoff.payload.sessionId;
    selectedDestination = handoff.destination;
    route = buildRoute(selectedDestination);
    populateDestinationSelect();
    routeModeButton.textContent = "USE REPRESENTATIVE CUTAWAY ROUTES (OPTIONAL)";
    payloadStatusElement.textContent = "QR handoff session " + handoff.payload.sessionId + " · " +
      route.length + " connected waypoints · " + routeData.floorNames[selectedDestination.floor];
    payloadStatusElement.className = "success";
    coverageStatusElement.textContent = getCoverageMessage(selectedDestination);
    destinationElement.textContent = selectedDestination.name;
    floorElement.textContent = "Campus route · room floor is not mapped";
    currentFloorElement.textContent = routeData.floorNames[route[0].floor];
    updateRouteGraphics();
    handoffReady = true;
    instructionElement.textContent = "Campus route ready from " +
      routeData.campusPrototypeGraph.nodes.find(node => node.id === routeData.campusPrototypeGraph.startNodeId).label +
      ". Start to follow the inferred outdoor path to " + selectedDestination.name + ".";
    startButton.disabled = !handoffReady;
    pauseButton.disabled = !modelReady;
    restartButton.disabled = !modelReady;
    window.S014VirtualCampus = {
      payload: handoff.payload,
      getHandoffPayload: () => handoffPayload,
      get route() { return route; },
      getRoute: () => route,
      auditRoutes,
      auditCampusRoutes,
      getActiveSessionId: () => activeSessionId,
      getSelectedDestinationId: () => selectedDestination.id,
      getModelBounds: () => campusBounds
        ? { min: campusBounds.min.toArray(), max: campusBounds.max.toArray() }
        : null,
      getNavigationState: () => ({
        running,
        routeIndex,
        waypointCount: route.length,
        currentPosition: currentPosition ? currentPosition.toArray() : null,
        lastBlock
      })
    };
  } catch (error) {
    payloadStatusElement.textContent = error.message;
    payloadStatusElement.className = "warning";
    coverageStatusElement.textContent = "No route coverage is available.";
    destinationElement.textContent = "Invalid QR handoff";
    floorElement.textContent = "No route loaded";
    instructionElement.textContent = "Scan a current destination QR code to load virtual navigation.";
    startButton.disabled = true;
    pauseButton.disabled = true;
    restartButton.disabled = true;
    setNotice(error.message, "warning");
  }

  try {
    if (!routeData) {
      const routeResponse = await fetch("./route-data.json?v=4.6-campus-routing-ready");
      if (!routeResponse.ok) throw new Error("Could not load the campus route catalogue (" + routeResponse.status + ").");
      routeData = await routeResponse.json();
      validateRouteData(routeData);
    }
    await loadCampusModel();
    if (handoffReady) {
      currentPosition = route[0].position.clone();
      routeIndex = 1;
      setRouteStatus();
      setCameraForRoute(currentPosition, route[routeIndex].position, true);
      if (pendingStart) startRoute();
      refreshControls();
    } else {
      focusCampusOverview();
    }
  } catch (error) {
    campusModelFailed = true;
    pendingStart = false;
    startButton.textContent = "START VIRTUAL ROUTE";
    modelStatusElement.textContent = "Campus model could not be loaded.";
    progressElement.textContent = "Error";
    setNotice("3D campus load failed: " + error.message, "warning");
    refreshControls();
  }
}

initialize();
