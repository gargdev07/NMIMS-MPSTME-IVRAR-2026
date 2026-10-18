using System;
using System.Collections.Generic;
using UnityEngine;

namespace R057.Kiosk
{
    [Serializable]
    public sealed class R057DestinationDefinition
    {
        public string id;
        public string displayName;
        public string category;
        public string targetFloor;
        public string prototypeNote;
        public Vector3 prototypeEndpointLocal;
        public bool hasPrototypeEndpoint;
    }

    [DefaultExecutionOrder(-200)]
    public sealed class R057DestinationCatalog : MonoBehaviour
    {
        [SerializeField] private List<R057DestinationDefinition> destinations = new List<R057DestinationDefinition>();

        public IReadOnlyList<R057DestinationDefinition> Destinations => destinations;

        private void Awake()
        {
            EnsureDefaults();
        }

        public void EnsureDefaults()
        {
            if (destinations.Count == 0) PopulateDefaults();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var endpoints = new HashSet<Vector3>();
            foreach (var destination in destinations)
            {
                if (destination == null || string.IsNullOrWhiteSpace(destination.id))
                    continue;
                if (!ids.Add(destination.id))
                {
                    destination.hasPrototypeEndpoint = false;
                    Debug.LogError("Duplicate destination ID in catalogue: " + destination.id);
                    continue;
                }
                // Only the three prototype labs and its sample help desk have
                // interior waypoints. Every other campus destination is an
                // exterior approach on the ground floor.
                if (destination.id != "lab-101" && destination.id != "lab-102" &&
                    destination.id != "lab-103" && destination.id != "student-help")
                    destination.targetFloor = "Ground";
                destination.hasPrototypeEndpoint = TryGetPrototypeEndpoint(destination.id, out destination.prototypeEndpointLocal);
                if (destination.hasPrototypeEndpoint)
                {
                    var expectedY = destination.targetFloor == "Ground" ? 0f :
                        destination.targetFloor == "First" ? 3f : float.NaN;
                    if (float.IsNaN(expectedY) || !Mathf.Approximately(destination.prototypeEndpointLocal.y, expectedY))
                    {
                        destination.hasPrototypeEndpoint = false;
                        Debug.LogError("Destination floor does not match its prototype endpoint: " + destination.id);
                    }
                    else if (!endpoints.Add(destination.prototypeEndpointLocal))
                    {
                        destination.hasPrototypeEndpoint = false;
                        Debug.LogError("Duplicate representative endpoint in catalogue: " + destination.id);
                    }
                }
                if (!destination.hasPrototypeEndpoint)
                {
                    destination.prototypeNote = "No representative endpoint is configured.";
                }
                else if (destination.id == "main-entrance" || destination.id == "parking")
                {
                    destination.prototypeNote = "Prototype exterior endpoint; campus-wide walkable geometry is not mapped.";
                }
                else
                {
                    destination.prototypeNote = "Representative/demo endpoint; not verified against a campus room or floor plan.";
                }
            }
        }

        public R057DestinationDefinition Find(string displayName)
        {
            for (var i = 0; i < destinations.Count; i++)
                if (destinations[i] != null &&
                    (string.Equals(destinations[i].displayName, displayName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(destinations[i].id, displayName, StringComparison.OrdinalIgnoreCase)))
                    return destinations[i];
            return null;
        }

        public R057DestinationDefinition FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            for (var i = 0; i < destinations.Count; i++)
                if (destinations[i] != null &&
                    string.Equals(destinations[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return destinations[i];
            return null;
        }

        public static bool TryGetPrototypeEndpoint(string id, out Vector3 endpoint)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                endpoint = Vector3.zero;
                return false;
            }
            switch (id.ToLowerInvariant())
            {
                case "lab-101": endpoint = new Vector3(-1.5f, 3f, -2.6f); return true;
                case "lab-102": endpoint = new Vector3(1.5f, 3f, -2.6f); return true;
                case "lab-103": endpoint = new Vector3(4.5f, 3f, -2.6f); return true;
                case "student-help": endpoint = new Vector3(-3.5f, 0f, -2.4f); return true;
                default: endpoint = Vector3.zero; return false;
            }
        }

        private void PopulateDefaults()
        {
            Add("auditorium", "Auditorium", "Academic", "Ground");
            Add("lab-101", "Lab 101", "Academic", "First");
            Add("lab-102", "Lab 102", "Academic", "First");
            Add("lab-103", "Lab 103", "Academic", "First");
            Add("lab-104", "Lab 104", "Academic", "Ground");
            Add("classrooms", "Classrooms", "Academic", "Ground");
            Add("seminar-hall", "Seminar Hall", "Academic", "Ground");
            Add("deans-office", "Dean's Office", "Academic", "Ground");
            Add("faculty-offices", "Faculty Offices", "Academic", "Ground");
            Add("library", "Library", "Student Services", "Ground");
            Add("reception", "Reception", "Student Services", "Ground");
            Add("student-help", "Student Help Desk", "Student Services", "Ground");
            Add("administration", "Administration", "Student Services", "Ground");
            Add("student-activity", "Student Activity Area", "Student Services", "Ground");
            Add("cafeteria", "Cafeteria", "Amenities", "Ground");
            Add("water", "Drinking Water", "Amenities", "Ground");
            Add("washrooms", "Washrooms", "Amenities", "Ground");
            Add("first-aid", "Medical / First Aid", "Amenities", "Ground");
            Add("main-entrance", "Main Entrance", "Access", "Ground");
            Add("visitor-reception", "Visitor Reception", "Access", "Ground");
            Add("parking", "Parking", "Access", "Ground");
            Add("security", "Security Desk", "Access", "Ground");
        }

        private void Add(string id, string name, string category, string floor)
        {
            destinations.Add(new R057DestinationDefinition
            {
                id = id,
                displayName = name,
                category = category,
                targetFloor = floor,
                prototypeNote = "Mapped to the nearest validated prototype waypoint."
            });
        }
    }
}
