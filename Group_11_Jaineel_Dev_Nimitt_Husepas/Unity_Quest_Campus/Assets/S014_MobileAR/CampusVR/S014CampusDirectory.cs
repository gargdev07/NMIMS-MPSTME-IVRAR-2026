using System;
using System.Collections.Generic;
using UnityEngine;

namespace S014.CampusVR
{
    /// <summary>One navigable campus destination: a labelled gate plus the
    /// ground-to-gate guidance polyline the kiosk hands off to the navigator.</summary>
    [Serializable]
    public class CampusDestinationAnchor
    {
        public string id;
        public string displayName;
        public string category;
        public int floor;                                      // level index: 0=LG,1=G,2..9 = F1..F8
        public string floorLabel;                              // "LG","G","1".."8"
        public Transform anchor;                               // the labelled gate
        public List<Vector3> pathFromLobby = new List<Vector3>(); // STAIRS path: spawn -> gate
        public bool stairsWalkable = true;                     // false = this room cannot be reached on foot from the gate (use the lift)
        public Vector3 liftExit;                               // gallery point by the lift on this floor
        public List<Vector3> liftPath = new List<Vector3>();   // LIFT path: liftExit -> gate
    }

    /// <summary>Scene registry mapping destination ids to their world gate and
    /// route. Populated by the campus generator; read by the kiosk and guide.</summary>
    public class S014CampusDirectory : MonoBehaviour
    {
        public Transform lobbySpawn;
        public List<CampusDestinationAnchor> destinations = new List<CampusDestinationAnchor>();

        public CampusDestinationAnchor Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < destinations.Count; i++)
            {
                var a = destinations[i];
                if (a != null && string.Equals(a.id, id, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return null;
        }
    }
}
