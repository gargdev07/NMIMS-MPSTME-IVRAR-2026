using System.Collections.Generic;
using UnityEngine;

namespace S014.MobileAR
{
    /// <summary>Deterministic development fixture. Replace this provider with R057 route handoff integration.</summary>
    public class S014MockRouteProvider : MonoBehaviour, IS014RouteProvider
    {
        [SerializeField] private Vector3 start = new Vector3(0f, 1f, -8f);

        public IReadOnlyList<S014Waypoint> GetRoute()
        {
            return new List<S014Waypoint>
            {
                new S014Waypoint(0, start + new Vector3(0f, 0f, 3f), "Proceed toward the next waypoint", "Ground", false, "Prototype start marker"),
                new S014Waypoint(1, start + new Vector3(4f, 0f, 6f), "Turn right at the landmark", "Ground", false, "Prototype turn marker"),
                new S014Waypoint(2, start + new Vector3(4f, 0f, 9f), "Continue to the floor transition", "Ground", true, "Prototype stair marker", "First"),
                new S014Waypoint(3, start + new Vector3(8f, 3f, 9f), "Proceed on the next level", "First", false, "Prototype upper-level marker"),
                new S014Waypoint(4, start + new Vector3(8f, 3f, 13f), "Destination reached", "First", false, "Prototype destination")
            };
        }
    }
}



