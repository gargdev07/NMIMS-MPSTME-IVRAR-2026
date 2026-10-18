using System;
using System.Collections.Generic;
using UnityEngine;
using R057.Kiosk;

namespace S014.MobileAR
{
    [DefaultExecutionOrder(-100)]
    public sealed class S014CampusRouteProvider : MonoBehaviour, IS014RouteProvider
    {
        [SerializeField] private string selectedDestination = "Auditorium";
        [SerializeField] private string selectedDestinationId = "auditorium";
        [SerializeField] private S014RepresentativeInteriorIntegration representativeInterior;
        [SerializeField] private R057DestinationCatalog destinationCatalog;
        [SerializeField] private TextAsset campusRouteData;
        private R057DestinationDefinition currentDestination;
        private S014NavigationRouteResult lastRouteResult;

        [Serializable] private sealed class CampusRouteData { public CampusGraph campusPrototypeGraph = new CampusGraph(); }
        [Serializable] private sealed class CampusGraph
        {
            public string startNodeId = string.Empty;
            public CampusNode[] nodes = new CampusNode[0];
            public CampusEdge[] edges = new CampusEdge[0];
        }
        [Serializable] private sealed class CampusNode
        {
            public string id = string.Empty;
            public float[] position = new float[0];
            public string floor = string.Empty;
            public string label = string.Empty;
            public string kind = string.Empty;
            public string destinationId = string.Empty;
        }
        [Serializable] private sealed class CampusEdge
        {
            public string id = string.Empty;
            public string from = string.Empty;
            public string to = string.Empty;
            public bool walkable = true;
            public string kind = string.Empty;
            public bool floorTransition;
            public string targetFloor = string.Empty;
        }

        private void Awake()
        {
            if (destinationCatalog == null) destinationCatalog = FindAnyObjectByType<R057DestinationCatalog>();
            if (destinationCatalog == null) return;
            destinationCatalog.EnsureDefaults();
            currentDestination = destinationCatalog.FindById(selectedDestinationId) ??
                                 destinationCatalog.Find(selectedDestination);
            if (currentDestination != null)
            {
                selectedDestination = currentDestination.displayName;
                selectedDestinationId = currentDestination.id;
            }
        }

        public string SelectedDestination => currentDestination == null ? selectedDestination : currentDestination.displayName;
        public string SelectedDestinationId => currentDestination == null ? selectedDestinationId : currentDestination.id;
        public string RouteId => string.IsNullOrWhiteSpace(SelectedDestinationId) ? string.Empty : "CampusRoute_" + SelectedDestinationId;
        public S014NavigationRouteResult CurrentRouteResult => lastRouteResult;
        public bool UsesCampusRoute => true;

        public bool SetDestination(R057DestinationDefinition destination)
        {
            if (destination == null)
            {
                currentDestination = null;
                Debug.LogError("Cannot select an unknown campus destination.");
                return false;
            }

            currentDestination = destination;
            selectedDestination = destination.displayName;
            selectedDestinationId = destination.id;
            return true;
        }

        public bool SetGroundPassageOpen(bool open)
        {
            if (representativeInterior == null)
            {
                Debug.LogError("Cannot change the ground passage: representative interior is missing.");
                return false;
            }
            return representativeInterior.SetGroundPassageOpen(open);
        }

        public IReadOnlyList<S014Waypoint> GetRoute()
        {
            if (currentDestination == null)
            {
                Debug.LogError("No catalogue destination has been selected for the campus route.");
                return new List<S014Waypoint>();
            }

            if (campusRouteData == null)
            {
                Debug.LogError("Campus route provider has no assigned campus route-data asset.");
                return new List<S014Waypoint>();
            }

            var data = JsonUtility.FromJson<CampusRouteData>(campusRouteData.text);
            if (data == null || data.campusPrototypeGraph == null ||
                data.campusPrototypeGraph.nodes == null || data.campusPrototypeGraph.edges == null)
            {
                Debug.LogError("Campus route-data asset is invalid or missing its campus graph.");
                return new List<S014Waypoint>();
            }

            var graph = new S014NavigationRouteGraph();
            var error = string.Empty;
            foreach (var node in data.campusPrototypeGraph.nodes)
            {
                if (node.position == null || node.position.Length != 3)
                {
                    Debug.LogError("Campus route node has no XYZ position: " + node.id);
                    return new List<S014Waypoint>();
                }
                var destinationId = string.Equals(node.destinationId, currentDestination.id, StringComparison.Ordinal)
                    ? node.destinationId : string.Empty;
                var instruction = !string.IsNullOrEmpty(destinationId)
                    ? node.label ?? ("Arrive at the inferred campus approach for " + currentDestination.displayName)
                    : node.kind != null && node.kind.IndexOf("entrance", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Follow the campus path past " + node.label
                        : "Continue along the " + (node.label ?? "inferred campus route");
                if (!graph.TryAddNode(new S014RouteGraphNode(
                        node.id,
                        new Vector3(node.position[0], node.position[1], node.position[2]),
                        node.floor,
                        instruction,
                        node.label ?? node.id,
                        destinationId), out error))
                {
                    Debug.LogError("Could not load campus route node: " + error);
                    return new List<S014Waypoint>();
                }
            }
            foreach (var edge in data.campusPrototypeGraph.edges)
            {
                if (!graph.TryConnect(edge.id, edge.from, edge.to, edge.kind, edge.walkable,
                        edge.floorTransition, edge.targetFloor, out error))
                {
                    Debug.LogError("Could not load campus route connector: " + error);
                    return new List<S014Waypoint>();
                }
            }

            var destinationNodeId = "campus-destination-" + currentDestination.id;
            if (!graph.TryFindShortestPath(data.campusPrototypeGraph.startNodeId, destinationNodeId,
                    out lastRouteResult, out error))
            {
                Debug.LogError("Could not build a campus-wide route for " + currentDestination.id + ": " + error);
                lastRouteResult = null;
                return new List<S014Waypoint>();
            }
            return lastRouteResult.Waypoints;
        }
    }
}
