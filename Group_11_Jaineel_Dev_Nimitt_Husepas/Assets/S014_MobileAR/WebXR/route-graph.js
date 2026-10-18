const VALID_FLOORS = new Set(["Ground", "First"]);
const VALID_TRANSITION_KINDS = new Set(["stair", "ramp"]);
const DISTANCE_EPSILON = 1e-8;

function distanceBetween(left, right) {
  const x = left[0] - right[0];
  const y = left[1] - right[1];
  const z = left[2] - right[2];
  return Math.sqrt(x * x + y * y + z * z);
}

function compareText(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}

export function validateNavigationGraph(graph) {
  if (!graph || typeof graph !== "object" ||
      !Array.isArray(graph.nodes) || !Array.isArray(graph.edges) ||
      typeof graph.startNodeId !== "string") {
    throw new Error("The campus navigation graph is missing nodes, edges, or a start node.");
  }

  const nodes = new Map();
  for (const node of graph.nodes) {
    if (!node || typeof node.id !== "string" || !node.id ||
        !Array.isArray(node.position) || node.position.length !== 3 ||
        !node.position.every(Number.isFinite) || !VALID_FLOORS.has(node.floor) ||
        typeof node.label !== "string" || typeof node.kind !== "string") {
      throw new Error("The campus navigation graph contains an invalid node.");
    }
    if (nodes.has(node.id)) throw new Error("Duplicate navigation node ID: " + node.id);
    nodes.set(node.id, node);
  }
  if (!nodes.has(graph.startNodeId)) throw new Error("The navigation start node is missing.");

  const edges = new Set();
  for (const edge of graph.edges) {
    if (!edge || typeof edge.id !== "string" || !edge.id ||
        typeof edge.from !== "string" || typeof edge.to !== "string" ||
        typeof edge.walkable !== "boolean" || typeof edge.kind !== "string") {
      throw new Error("The campus navigation graph contains an invalid edge.");
    }
    if (edges.has(edge.id)) throw new Error("Duplicate navigation edge ID: " + edge.id);
    edges.add(edge.id);
    const from = nodes.get(edge.from);
    const to = nodes.get(edge.to);
    if (!from || !to) throw new Error("Navigation edge " + edge.id + " references an unknown node.");
    if (distanceBetween(from.position, to.position) <= DISTANCE_EPSILON) {
      throw new Error("Navigation edge " + edge.id + " has zero length.");
    }
    const changesFloor = from.floor !== to.floor;
    if (changesFloor !== (edge.floorTransition === true)) {
      throw new Error("Navigation edge " + edge.id + " has inconsistent floor-transition metadata.");
    }
    if (changesFloor &&
        (!VALID_TRANSITION_KINDS.has(edge.kind) || edge.targetFloor !== to.floor)) {
      throw new Error("Navigation edge " + edge.id + " changes floors without a valid stair or ramp.");
    }
    if (!changesFloor && edge.floorTransition === true) {
      throw new Error("Navigation edge " + edge.id + " marks a transition without changing floors.");
    }
  }
  return nodes;
}

export function findShortestPath(graph, startNodeId, destinationNodeId) {
  const nodes = validateNavigationGraph(graph);
  if (!nodes.has(startNodeId)) throw new Error("Unknown route start node: " + startNodeId);
  if (!nodes.has(destinationNodeId)) throw new Error("Unknown route destination node: " + destinationNodeId);

  const adjacent = new Map();
  for (const id of nodes.keys()) adjacent.set(id, []);
  for (const edge of graph.edges) {
    if (!edge.walkable) continue;
    const from = nodes.get(edge.from);
    const to = nodes.get(edge.to);
    const length = distanceBetween(from.position, to.position);
    adjacent.get(edge.from).push({ nodeId: edge.to, edge, length });
    adjacent.get(edge.to).push({ nodeId: edge.from, edge, length });
  }
  for (const neighbours of adjacent.values()) {
    neighbours.sort((left, right) =>
      compareText(left.nodeId, right.nodeId) || compareText(left.edge.id, right.edge.id));
  }

  const distances = new Map([[startNodeId, 0]]);
  const signatures = new Map([[startNodeId, startNodeId]]);
  const previous = new Map();
  const pending = new Set([startNodeId]);
  const visited = new Set();

  while (pending.size) {
    let currentId;
    for (const candidateId of pending) {
      if (currentId === undefined) {
        currentId = candidateId;
        continue;
      }
      const difference = distances.get(candidateId) - distances.get(currentId);
      if (difference < -DISTANCE_EPSILON ||
          (Math.abs(difference) <= DISTANCE_EPSILON &&
           compareText(signatures.get(candidateId), signatures.get(currentId)) < 0)) {
        currentId = candidateId;
      }
    }
    pending.delete(currentId);
    if (currentId === destinationNodeId) break;
    visited.add(currentId);

    for (const neighbour of adjacent.get(currentId)) {
      if (visited.has(neighbour.nodeId)) continue;
      const candidateDistance = distances.get(currentId) + neighbour.length;
      const candidateSignature = signatures.get(currentId) + "\u0000" + neighbour.nodeId;
      const knownDistance = distances.get(neighbour.nodeId);
      const isShorter = knownDistance === undefined ||
        candidateDistance < knownDistance - DISTANCE_EPSILON;
      const isDeterministicTie = knownDistance !== undefined &&
        Math.abs(candidateDistance - knownDistance) <= DISTANCE_EPSILON &&
        compareText(candidateSignature, signatures.get(neighbour.nodeId)) < 0;
      if (!isShorter && !isDeterministicTie) continue;
      distances.set(neighbour.nodeId, candidateDistance);
      signatures.set(neighbour.nodeId, candidateSignature);
      previous.set(neighbour.nodeId, {
        nodeId: currentId,
        edge: neighbour.edge
      });
      pending.add(neighbour.nodeId);
    }
  }

  if (!distances.has(destinationNodeId)) {
    throw new Error("No walkable route connects " + startNodeId + " to " + destinationNodeId + ".");
  }

  const reversed = [];
  let cursor = destinationNodeId;
  while (cursor !== startNodeId) {
    const step = previous.get(cursor);
    if (!step) throw new Error("Route reconstruction failed at node " + cursor + ".");
    reversed.push({
      node: nodes.get(cursor),
      edge: step.edge
    });
    cursor = step.nodeId;
  }
  reversed.push({ node: nodes.get(startNodeId), edge: null });
  reversed.reverse();

  const waypoints = reversed.map((step, index) => {
    const transition = index > 0 && step.edge.floorTransition === true;
    return {
      id: step.node.id,
      position: step.node.position.slice(),
      label: step.node.label,
      floor: step.node.floor,
      transitionTo: transition ? step.node.floor : ""
    };
  });
  const transitionCount = reversed.filter(step => step.edge && step.edge.floorTransition).length;
  const lengthMeters = distances.get(destinationNodeId);
  return {
    waypoints,
    nodeIds: waypoints.map(point => point.id),
    lengthMeters,
    estimatedWalkingSeconds: lengthMeters / 1.2,
    transitionCount,
    destinationId: nodes.get(destinationNodeId).destinationId || ""
  };
}

export function auditDestinationRoutes(data) {
  const useCampusGraph = Boolean(data.campusPrototypeGraph);
  const graph = useCampusGraph ? data.campusPrototypeGraph : data.navigationGraph;
  const nodes = validateNavigationGraph(graph);
  const destinationIds = new Set();
  const destinationNodes = new Set();
  return data.destinations.map(destination => {
    if (destinationIds.has(destination.id)) throw new Error("Duplicate destination ID: " + destination.id);
    destinationIds.add(destination.id);
    const nodeId = useCampusGraph ? destination.campusNodeId : destination.nodeId;
    if (destinationNodes.has(nodeId)) throw new Error("Duplicate destination node: " + nodeId);
    destinationNodes.add(nodeId);
    const node = nodes.get(nodeId);
    if (!node || node.destinationId !== destination.id) {
      throw new Error("Destination " + destination.id + " is not mapped to its own graph endpoint.");
    }
    const expectedFloor = useCampusGraph ? destination.campusFloor : destination.floor;
    if (node.floor !== expectedFloor ||
        distanceBetween(node.position, destination.endpoint) > DISTANCE_EPSILON) {
      throw new Error("Destination " + destination.id + " has inconsistent endpoint or floor metadata.");
    }
    const route = findShortestPath(graph, graph.startNodeId, nodeId);
    const expectedTransitions = expectedFloor === "First" ? 1 : 0;
    if (route.waypoints[route.waypoints.length - 1].id !== nodeId ||
        route.transitionCount !== expectedTransitions ||
        route.waypoints.some((point, index) => index > 0 &&
          distanceBetween(point.position, route.waypoints[index - 1].position) <= DISTANCE_EPSILON)) {
      throw new Error("Destination " + destination.id + " has an invalid reconstructed route.");
    }
    return {
      id: destination.id,
      floor: expectedFloor,
      nodeId,
      coverage: useCampusGraph ? destination.campusCoverage : destination.coverage,
      lengthMeters: route.lengthMeters,
      waypointCount: route.waypoints.length,
      transitionCount: route.transitionCount,
      nodeIds: route.nodeIds
    };
  });
}
