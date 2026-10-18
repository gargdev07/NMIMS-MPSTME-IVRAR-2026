using System;
using UnityEngine;

namespace R057.Kiosk
{
    [Serializable]
    public sealed class R057NavigationPayload
    {
        public string sessionId;
        public string routeId;
        public string destinationId;
        public string destinationName;
        public string targetFloor;
        public string routeVersion;
        public string createdUtc;
    }

    public sealed class R057NavigationSession : MonoBehaviour
    {
        public R057NavigationPayload CurrentPayload { get; private set; }
        public bool HasActiveSession => CurrentPayload != null;

        public R057NavigationPayload Begin(string routeId, R057DestinationDefinition destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (!string.Equals(routeId, "CampusRoute_" + destination.id, StringComparison.Ordinal))
                throw new ArgumentException("The route ID does not match the selected destination ID.", nameof(routeId));
            CurrentPayload = new R057NavigationPayload
            {
                sessionId = Guid.NewGuid().ToString("N"),
                routeId = routeId,
                destinationId = destination.id,
                destinationName = destination.displayName,
                targetFloor = destination.targetFloor,
                routeVersion = "2.0",
                createdUtc = DateTime.UtcNow.ToString("o")
            };
            return CurrentPayload;
        }

        public void Clear()
        {
            CurrentPayload = null;
        }

        public string Serialize()
        {
            if (CurrentPayload == null) return string.Empty;
            return JsonUtility.ToJson(CurrentPayload);
        }
    }
}
