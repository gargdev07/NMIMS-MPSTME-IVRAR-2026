using System.Collections.Generic;
using System;
using UnityEngine;
using R057.Kiosk;

namespace S014.MobileAR
{
    public sealed class S014RouteGraphNode
    {
        public string Id { get; }
        public Vector3 WorldPosition { get; }
        public string Floor { get; }
        public string Instruction { get; }
        public string Landmark { get; }
        public string DestinationId { get; }

        public S014RouteGraphNode(
            string id,
            Vector3 worldPosition,
            string floor,
            string instruction,
            string landmark,
            string destinationId = "")
        {
            Id = id;
            WorldPosition = worldPosition;
            Floor = floor;
            Instruction = instruction;
            Landmark = landmark;
            DestinationId = destinationId;
        }
    }

    public sealed class S014NavigationRouteResult
    {
        public string DestinationId { get; }
        public IReadOnlyList<S014Waypoint> Waypoints { get; }
        public float TotalLengthMeters { get; }
        public float EstimatedWalkingTimeSeconds { get; }
        public int FloorTransitionCount { get; }

        internal S014NavigationRouteResult(
            string destinationId,
            IReadOnlyList<S014Waypoint> waypoints,
            float totalLengthMeters,
            int floorTransitionCount)
        {
            DestinationId = destinationId;
            Waypoints = waypoints;
            TotalLengthMeters = totalLengthMeters;
            EstimatedWalkingTimeSeconds = totalLengthMeters / 1.3f;
            FloorTransitionCount = floorTransitionCount;
        }
    }

    public sealed class S014NavigationRouteGraph
    {
        private sealed class Edge
        {
            public readonly string id;
            public readonly string from;
            public readonly string to;
            public readonly string kind;
            public readonly bool floorTransition;
            public readonly string targetFloor;
            public readonly bool walkable;

            public Edge(string id, string from, string to, string kind, bool floorTransition, string targetFloor, bool walkable)
            {
                this.id = id;
                this.from = from;
                this.to = to;
                this.kind = kind;
                this.floorTransition = floorTransition;
                this.targetFloor = targetFloor;
                this.walkable = walkable;
            }
        }

        private readonly Dictionary<string, S014RouteGraphNode> nodes =
            new Dictionary<string, S014RouteGraphNode>(StringComparer.Ordinal);
        private readonly List<Edge> edges = new List<Edge>();
        private readonly HashSet<string> edgeIds = new HashSet<string>(StringComparer.Ordinal);

        public bool TryAddNode(S014RouteGraphNode node, out string error)
        {
            error = string.Empty;
            if (node == null || string.IsNullOrWhiteSpace(node.Id) ||
                (node.Floor != "Ground" && node.Floor != "First") ||
                !IsFinite(node.WorldPosition))
            {
                error = "Route graph node has invalid identity, position, or floor metadata.";
                return false;
            }
            if (nodes.ContainsKey(node.Id))
            {
                error = "Duplicate route graph node ID: " + node.Id;
                return false;
            }
            nodes.Add(node.Id, node);
            return true;
        }

        public bool TryConnect(
            string id,
            string fromId,
            string toId,
            string kind,
            bool walkable,
            bool floorTransition,
            string targetFloor,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(id) || !edgeIds.Add(id))
            {
                error = "Route graph edge ID is missing or duplicated: " + id;
                return false;
            }
            S014RouteGraphNode from;
            S014RouteGraphNode to;
            if (!nodes.TryGetValue(fromId, out from) || !nodes.TryGetValue(toId, out to))
            {
                edgeIds.Remove(id);
                error = "Route graph edge " + id + " references an unknown node.";
                return false;
            }
            if (Vector3.Distance(from.WorldPosition, to.WorldPosition) <= 0.01f)
            {
                edgeIds.Remove(id);
                error = "Route graph edge " + id + " has zero length.";
                return false;
            }
            var changesFloor = !string.Equals(from.Floor, to.Floor, StringComparison.Ordinal);
            if (changesFloor != floorTransition ||
                (changesFloor && ((kind != "stair" && kind != "ramp") || targetFloor != to.Floor)))
            {
                edgeIds.Remove(id);
                error = "Route graph edge " + id + " has invalid floor-transition metadata.";
                return false;
            }
            edges.Add(new Edge(id, fromId, toId, kind, floorTransition, targetFloor, walkable));
            return true;
        }

        public bool TryFindShortestPath(
            string startNodeId,
            string destinationNodeId,
            out S014NavigationRouteResult route,
            out string error)
        {
            route = null;
            error = string.Empty;
            if (!nodes.ContainsKey(startNodeId) || !nodes.ContainsKey(destinationNodeId))
            {
                error = "Route start or destination node is unavailable.";
                return false;
            }
            var destination = nodes[destinationNodeId];
            if (string.IsNullOrWhiteSpace(destination.DestinationId))
            {
                error = "Route endpoint is not a destination terminal: " + destinationNodeId;
                return false;
            }

            var adjacency = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
            foreach (var nodeId in nodes.Keys)
                adjacency.Add(nodeId, new List<Edge>());
            foreach (var edge in edges)
            {
                if (!edge.walkable) continue;
                adjacency[edge.from].Add(edge);
                adjacency[edge.to].Add(edge);
            }
            foreach (var adjacent in adjacency.Values)
                adjacent.Sort((left, right) => string.CompareOrdinal(left.id, right.id));

            var distances = new Dictionary<string, float>(StringComparer.Ordinal) { [startNodeId] = 0f };
            var signatures = new Dictionary<string, string>(StringComparer.Ordinal) { [startNodeId] = startNodeId };
            var previous = new Dictionary<string, Edge>(StringComparer.Ordinal);
            var pending = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (pending.Count > 0)
            {
                var currentId = string.Empty;
                var currentDistance = float.PositiveInfinity;
                var currentSignature = string.Empty;
                foreach (var candidateId in pending)
                {
                    var candidateDistance = distances[candidateId];
                    var candidateSignature = signatures[candidateId];
                    if (candidateDistance < currentDistance - 0.00001f ||
                        (Mathf.Abs(candidateDistance - currentDistance) <= 0.00001f &&
                         string.CompareOrdinal(candidateSignature, currentSignature) < 0))
                    {
                        currentId = candidateId;
                        currentDistance = candidateDistance;
                        currentSignature = candidateSignature;
                    }
                }
                pending.Remove(currentId);
                if (currentId == destinationNodeId) break;
                visited.Add(currentId);

                foreach (var edge in adjacency[currentId])
                {
                    var neighbourId = edge.from == currentId ? edge.to : edge.from;
                    if (visited.Contains(neighbourId)) continue;
                    var candidateDistance = currentDistance +
                        Vector3.Distance(nodes[currentId].WorldPosition, nodes[neighbourId].WorldPosition);
                    var candidateSignature = currentSignature + "\0" + neighbourId;
                    float knownDistance;
                    var hasKnownDistance = distances.TryGetValue(neighbourId, out knownDistance);
                    var isShorter = !hasKnownDistance || candidateDistance < knownDistance - 0.00001f;
                    var isDeterministicTie = hasKnownDistance &&
                        Mathf.Abs(candidateDistance - knownDistance) <= 0.00001f &&
                        string.CompareOrdinal(candidateSignature, signatures[neighbourId]) < 0;
                    if (!isShorter && !isDeterministicTie) continue;
                    distances[neighbourId] = candidateDistance;
                    signatures[neighbourId] = candidateSignature;
                    previous[neighbourId] = edge;
                    pending.Add(neighbourId);
                }
            }

            if (!distances.ContainsKey(destinationNodeId))
            {
                error = "No walkable route connects " + startNodeId + " to " + destinationNodeId + ".";
                return false;
            }

            var path = new List<string>();
            var incoming = new List<Edge>();
            var cursor = destinationNodeId;
            path.Add(cursor);
            while (cursor != startNodeId)
            {
                Edge edge;
                if (!previous.TryGetValue(cursor, out edge))
                {
                    error = "Route reconstruction failed at node " + cursor + ".";
                    return false;
                }
                incoming.Add(edge);
                cursor = edge.from == cursor ? edge.to : edge.from;
                path.Add(cursor);
            }
            path.Reverse();
            incoming.Reverse();

            var waypoints = new List<S014Waypoint>(path.Count);
            var transitions = 0;
            for (var i = 0; i < path.Count; i++)
            {
                var node = nodes[path[i]];
                var transition = i > 0 && incoming[i - 1].floorTransition;
                var targetFloor = transition ? node.Floor : string.Empty;
                if (transition) transitions++;
                waypoints.Add(new S014Waypoint(
                    i,
                    node.WorldPosition,
                    node.Instruction,
                    node.Floor,
                    transition,
                    node.Landmark,
                    targetFloor));
            }

            route = new S014NavigationRouteResult(
                destination.DestinationId,
                waypoints,
                distances[destinationNodeId],
                transitions);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    public sealed class S014RepresentativeInteriorIntegration : MonoBehaviour
    {
        [SerializeField] private Transform exteriorApproach;
        [SerializeField] private Transform entrance;
        [SerializeField] private Transform groundCorridor;
        [SerializeField] private Transform stairBottom;
        [SerializeField] private Transform stairMidpoint;
        [SerializeField] private Transform stairTop;
        [SerializeField] private Transform upperCorridor;
        [SerializeField] private Transform lab101Door;
        [SerializeField] private Transform lab104Door;
        [SerializeField] private Transform deansOfficeDoor;
        [SerializeField] private Transform parkingApproach;
        [SerializeField] private Transform groundDoorLeaf;
        [SerializeField] private Transform upperStairLanding;
        private Quaternion closedDoorRotation;
        private bool hasClosedDoorRotation;

        public S014NavigationRouteResult LastRouteResult { get; private set; }

        public bool TryBuildRoute(R057DestinationDefinition destination, out IReadOnlyList<S014Waypoint> waypoints)
        {
            waypoints = new List<S014Waypoint>();
            LastRouteResult = null;
            if (destination == null || !destination.hasPrototypeEndpoint ||
                (destination.targetFloor != "Ground" && destination.targetFloor != "First"))
                return false;
            ResolveRouteGeometry();

            var graph = new S014NavigationRouteGraph();
            var error = string.Empty;
            if (exteriorApproach == null || entrance == null ||
                !AddNode(graph, "exteriorApproach", exteriorApproach.position, "Ground",
                    "Approach the representative campus route", "Exterior approach", string.Empty, out error) ||
                !AddNode(graph, "entrance", entrance.position, "Ground",
                    "Arrive at the representative entrance", "Representative entrance",
                    "main-entrance", out error))
            {
                Debug.LogError(string.IsNullOrEmpty(error) ? "Representative route start or entrance is not assigned." : error);
                return false;
            }

            if (!Connect(graph, "approach-to-entrance", "exteriorApproach", "entrance", "prototype-outdoor", false, string.Empty, out error))
            {
                Debug.LogError(error);
                return false;
            }

            if (parkingApproach != null)
            {
                if (!AddNode(graph, "parkingApproach", parkingApproach.position, "Ground",
                        "Continue toward the representative parking endpoint", "Parking prototype endpoint",
                        "parking", out error) ||
                    !Connect(graph, "approach-to-parking", "exteriorApproach", "parkingApproach",
                        "prototype-outdoor", false, string.Empty, out error))
                {
                    Debug.LogError(error);
                    return false;
                }
            }

            var isParking = string.Equals(destination.id, "parking", StringComparison.Ordinal);
            var isMainEntrance = string.Equals(destination.id, "main-entrance", StringComparison.Ordinal);
            if (isParking || isMainEntrance)
            {
                var endpointTransform = isParking ? parkingApproach : entrance;
                if (endpointTransform == null ||
                    Vector3.Distance(endpointTransform.position, transform.TransformPoint(destination.prototypeEndpointLocal)) > 0.1f)
                {
                    Debug.LogError("The prototype route anchor does not match destination " + destination.id + "'s catalogue endpoint.");
                    return false;
                }
            }
            else if (!BuildRepresentativeInteriorGraph(graph, destination, out error))
            {
                Debug.LogError(error);
                return false;
            }

            var destinationNodeId = isParking ? "parkingApproach" :
                isMainEntrance ? "entrance" :
                string.Equals(destination.id, "reception", StringComparison.Ordinal) ? "doorwayThreshold" :
                "destination-" + destination.id;
            if (!graph.TryFindShortestPath("exteriorApproach", destinationNodeId, out var result, out error))
            {
                Debug.LogError(error);
                return false;
            }
            LastRouteResult = result;
            waypoints = result.Waypoints;
            return true;
        }

        public bool SetGroundPassageOpen(bool open)
        {
            ResolveRouteGeometry();
            if (groundDoorLeaf == null)
            {
                Debug.LogError("The representative ground-floor door leaf is not assigned.");
                return false;
            }
            if (!hasClosedDoorRotation)
            {
                closedDoorRotation = groundDoorLeaf.localRotation;
                hasClosedDoorRotation = true;
            }
            groundDoorLeaf.localRotation = open
                ? closedDoorRotation * Quaternion.Euler(0f, -90f, 0f)
                : closedDoorRotation;
            return true;
        }

        private void ResolveRouteGeometry()
        {
            if (upperStairLanding == null)
                upperStairLanding = transform.Find("InteriorGeometry_RepresentativeOnly/UpperFloor_12x12m/Floor_U_StairTopLanding");
            if (groundDoorLeaf == null)
                groundDoorLeaf = transform.Find("InteriorGeometry_RepresentativeOnly/DoorAssemblies_NativeScale/Door_GroundEntrance_Floor0/Door_A_Frame/Door_A_Door");
        }

        private bool BuildRepresentativeInteriorGraph(
            S014NavigationRouteGraph graph,
            R057DestinationDefinition destination,
            out string error)
        {
            error = string.Empty;
            if (groundCorridor == null || groundDoorLeaf == null)
            {
                error = "The representative interior corridor or doorway is not assigned.";
                return false;
            }

            var doorwayApproachPosition = transform.TransformPoint(new Vector3(-4.5f, 0f, -3.6f));
            var doorwayThresholdPosition = transform.TransformPoint(new Vector3(-4.5f, 0f, -2.4f));
            var receptionId = string.Equals(destination.id, "reception", StringComparison.Ordinal) ? "reception" : string.Empty;
            if (!AddNode(graph, "groundCorridor", groundCorridor.position, "Ground",
                    "Follow the ground-floor corridor", "Ground corridor", string.Empty, out error) ||
                !AddNode(graph, "doorwayApproach", doorwayApproachPosition, "Ground",
                    "Approach the ground-floor doorway", "Doorway approach", string.Empty, out error) ||
                !AddNode(graph, "doorwayThreshold", doorwayThresholdPosition, "Ground",
                    "Pass through the representative ground-floor doorway", "Doorway threshold", receptionId, out error) ||
                !Connect(graph, "entrance-to-ground-corridor", "entrance", "groundCorridor",
                    "representative-interior", false, string.Empty, out error) ||
                !Connect(graph, "ground-corridor-to-doorway", "groundCorridor", "doorwayApproach",
                    "representative-interior", false, string.Empty, out error) ||
                !Connect(graph, "doorway-to-threshold", "doorwayApproach", "doorwayThreshold",
                    "representative-interior", false, string.Empty, out error))
                return false;

            if (destination.targetFloor == "First")
            {
                if (stairBottom == null || stairMidpoint == null || stairTop == null ||
                    upperStairLanding == null || upperCorridor == null)
                {
                    error = "The representative stairway or upper corridor is not assigned.";
                    return false;
                }
                var stairApproachPosition = transform.TransformPoint(new Vector3(-4.5f, 0f, 4f));
                var upperBypassPosition = transform.TransformPoint(new Vector3(4.5f, 3f, 3.5f));
                if (!AddNode(graph, "stairApproach", stairApproachPosition, "Ground",
                        "Follow the side approach to the staircase", "Staircase approach", string.Empty, out error) ||
                    !AddNode(graph, "stairBottom", stairBottom.position, "Ground",
                        "Proceed to the staircase", "Staircase", string.Empty, out error) ||
                    !AddNode(graph, "stairMidpoint", stairMidpoint.position, "Ground",
                        "Reach the intermediate stair landing", "Stair landing", string.Empty, out error) ||
                    !AddNode(graph, "stairTop", stairTop.position, "First",
                        "Reach the upper-floor landing", "Upper stair landing", string.Empty, out error) ||
                    !AddNode(graph, "upperLanding", upperStairLanding.position, "First",
                        "Step onto the upper-floor landing", "Upper stair landing", string.Empty, out error) ||
                    !AddNode(graph, "upperStairBypass", upperBypassPosition, "First",
                        "Follow the route around the representative stairwell opening", "Upper stairwell bypass", string.Empty, out error) ||
                    !AddNode(graph, "upperCorridor", upperCorridor.position, "First",
                        "Follow the first-floor corridor", "Upper corridor", string.Empty, out error) ||
                    !Connect(graph, "threshold-to-stair-approach", "doorwayThreshold", "stairApproach",
                        "representative-interior", false, string.Empty, out error) ||
                    !Connect(graph, "stair-approach-to-bottom", "stairApproach", "stairBottom",
                        "stair", false, string.Empty, out error) ||
                    !Connect(graph, "stair-bottom-to-midpoint", "stairBottom", "stairMidpoint",
                        "stair", false, string.Empty, out error) ||
                    !Connect(graph, "stair-midpoint-to-top", "stairMidpoint", "stairTop",
                        "stair", true, "First", out error) ||
                    !Connect(graph, "stair-top-to-landing", "stairTop", "upperLanding",
                        "representative-interior", false, string.Empty, out error) ||
                    !Connect(graph, "landing-to-bypass", "upperLanding", "upperStairBypass",
                        "representative-interior", false, string.Empty, out error) ||
                    !Connect(graph, "bypass-to-upper-corridor", "upperStairBypass", "upperCorridor",
                        "representative-interior", false, string.Empty, out error))
                    return false;
            }

            if (string.Equals(destination.id, "reception", StringComparison.Ordinal))
                return true;

            var endpoint = transform.TransformPoint(destination.prototypeEndpointLocal);
            if (string.Equals(destination.id, "lab-101", StringComparison.Ordinal) && lab101Door != null)
                endpoint = lab101Door.position;
            else if (string.Equals(destination.id, "lab-104", StringComparison.Ordinal) && lab104Door != null)
                endpoint = lab104Door.position;
            else if (string.Equals(destination.id, "deans-office", StringComparison.Ordinal) && deansOfficeDoor != null)
                endpoint = deansOfficeDoor.position;
            var nodeId = "destination-" + destination.id;
            var connectorId = destination.targetFloor == "Ground" ? "doorwayThreshold" : "upperCorridor";
            if (!AddNode(graph, nodeId, endpoint, destination.targetFloor,
                    "Arrive at the representative " + destination.displayName + " endpoint",
                    destination.displayName, destination.id, out error))
                return false;
            var edgeId = (destination.targetFloor == "Ground" ? "threshold-to-" : "corridor-to-") + destination.id;
            return Connect(graph, edgeId, connectorId, nodeId,
                "representative-destination", false, string.Empty, out error);
        }

        private static bool AddNode(
            S014NavigationRouteGraph graph,
            string id,
            Vector3 position,
            string floor,
            string instruction,
            string landmark,
            string destinationId,
            out string error)
        {
            return graph.TryAddNode(
                new S014RouteGraphNode(id, position, floor, instruction, landmark, destinationId),
                out error);
        }

        private static bool Connect(
            S014NavigationRouteGraph graph,
            string id,
            string from,
            string to,
            string kind,
            bool transition,
            string targetFloor,
            out string error)
        {
            return graph.TryConnect(id, from, to, kind, true, transition, targetFloor, out error);
        }

    }
}
