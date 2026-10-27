using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SmartKioskHandoffManager manages the public campus kiosk interface,
/// resolves multi-floor destination queries, and generates serialized WebXR QR session
/// tokens for seamless handoff to visitors' mobile smartphones.
/// 
/// Course: IVRAR (Immersive Virtual, Real & Augmented Reality) - Group 11
/// Governing Standard: ISO/IEC 18004 (QR Code Barcode Symbology) & W3C WebXR Device API
/// </summary>
public class SmartKioskHandoffManager : MonoBehaviour
{
    [System.Serializable]
    public struct CampusDestination
    {
        public string destinationId;
        public string destinationName;
        public int floorLevel;
        public Vector3 worldBIMCoordinate;
        public List<Vector3> routeWaypoints;
    }

    [System.Serializable]
    public enum HandoffState
    {
        IdleOverview,           // Kiosk displaying global campus 3D map
        DestinationSelected,    // Route calculated on kiosk screen
        QRCodeAwaitingScan,     // Active QR session token presented to visitor
        HandoffCompleted        // Visitor mobile device confirmed active WebXR navigation
    }

    [Header("Kiosk Configuration")]
    [Tooltip("Unique kiosk identifier located at building entrances (e.g. KIOSK_MAIN_LOBBY).")]
    public string kioskIdentifier = "KIOSK_MAIN_LOBBY";

    [Tooltip("Timeout in seconds before QR handoff session expires.")]
    public float qrSessionTimeoutSeconds = 45.0f;

    [Tooltip("Demonstration shared secret used to encrypt and sign the QR token (see HandoffPayloadCodec).")]
    public string handoffSecret = "IVRAR-G11-DEMO-KEY";

    [Tooltip("Base address of the zero-install WebXR client that the QR code opens.")]
    public string webXrBaseUrl = "https://campus-ar.internal/nav";

    [Tooltip("World position of this kiosk, the origin of every route.")]
    public Vector3 kioskWorldPosition = Vector3.zero;

    [Tooltip("Height of one storey in metres; used to infer the floor of a route waypoint.")]
    public float floorHeightMetres = 4.0f;

    [Header("Campus Facility Directory")]
    public List<CampusDestination> destinationDirectory = new List<CampusDestination>();

    [Header("Live Handoff Telemetry")]
    public HandoffState currentHandoffState = HandoffState.IdleOverview;
    public string activeSessionToken = "";
    public float sessionTimer = 0f;
    public int totalHandoffSessionsGenerated = 0;

    // Student Technical Boundaries:
    // [R057 - Jaineel Shah]  DONE here: dynamic QR payload (HandoffPayloadCodec), encryption, 45 s lease, A* route (CampusGraph).
    //                        The destination UI and 3D route preview run in the Quest build (Unity_Quest_Campus/.../S014CampusKiosk.cs).
    // [S014 - Dev Garg]      DONE here: TryOpenHandoff() is the acknowledgement listener the mobile client calls with the scanned token.

    private CampusGraph graph;
    private const string KioskNodeId = "KIOSK";

    void Start()
    {
        InitializeDestinationDirectory();
    }

    void Update()
    {
        if (currentHandoffState == HandoffState.QRCodeAwaitingScan)
        {
            sessionTimer += Time.deltaTime;
            if (sessionTimer >= qrSessionTimeoutSeconds)
            {
                CancelExpiredSession();
            }
        }
    }

    /// <summary>
    /// Loads ground-truth campus destination coordinates into directory.
    /// </summary>
    private void InitializeDestinationDirectory()
    {
        // Ingest the destination records (BIM coordinates + stored waypoints) into a topological graph.
        // Waypoints are snapped to a 0.5 m lattice so corridors shared by several routes merge into one set of vertices.
        graph = new CampusGraph();
        graph.AddNode(KioskNodeId, kioskWorldPosition.x, kioskWorldPosition.y, kioskWorldPosition.z, FloorOf(kioskWorldPosition.y));
        foreach (var d in destinationDirectory)
        {
            string prevId = KioskNodeId;
            if (d.routeWaypoints != null)
            {
                foreach (var w in d.routeWaypoints)
                {
                    string id = NodeKey(w);
                    graph.AddNode(id, w.x, w.y, w.z, FloorOf(w.y));
                    if (id != prevId) graph.AddEdge(prevId, id);
                    prevId = id;
                }
            }
            string destNode = "DEST:" + d.destinationId;
            graph.AddNode(destNode, d.worldBIMCoordinate.x, d.worldBIMCoordinate.y, d.worldBIMCoordinate.z, d.floorLevel);
            graph.AddEdge(prevId, destNode);
        }
        Debug.Log($"[SmartKioskHandoffManager] Kiosk '{kioskIdentifier}' online. {destinationDirectory.Count} destinations registered.");
    }

    /// <summary>
    /// Computes route to destination and presents dynamic WebXR handoff QR code.
    /// </summary>
    public string GenerateHandoffQR(string destinationId)
    {
        CampusDestination dest = default(CampusDestination);
        bool found = false;
        foreach (var d in destinationDirectory)
            if (string.Equals(d.destinationId, destinationId, StringComparison.OrdinalIgnoreCase)) { dest = d; found = true; break; }
        if (!found)
        {
            Debug.LogWarning($"[SmartKioskHandoffManager] Unknown destination '{destinationId}'.");
            return "";
        }

        // Multi-floor shortest path (A*) from the kiosk; fall back to the stored waypoints if the graph has no route.
        var flat = new List<float>();
        float cost;
        List<string> ids = graph != null ? graph.ShortestPath(KioskNodeId, "DEST:" + dest.destinationId, out cost) : null;
        if (ids != null)
        {
            foreach (var id in ids) { var n = graph.Get(id); flat.Add(n.x); flat.Add(n.y); flat.Add(n.z); }
        }
        else if (dest.routeWaypoints != null)
        {
            foreach (var w in dest.routeWaypoints) { flat.Add(w.x); flat.Add(w.y); flat.Add(w.z); }
        }

        var payload = new HandoffPayloadCodec.Payload
        {
            kioskId = kioskIdentifier,
            destinationId = dest.destinationId,
            floorLevel = dest.floorLevel,
            issuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            waypointsXYZ = flat.ToArray()
        };
        activeSessionToken = HandoffPayloadCodec.Encode(payload, handoffSecret);
        sessionTimer = 0f;
        currentHandoffState = HandoffState.QRCodeAwaitingScan;
        totalHandoffSessionsGenerated++;

        string webXrPayloadUrl = $"{webXrBaseUrl}?kiosk={kioskIdentifier}&t={activeSessionToken}";
        Debug.Log($"[SmartKioskHandoffManager] Handoff generated for '{destinationId}' ({flat.Count / 3} waypoints, {webXrPayloadUrl.Length} chars).");
        return webXrPayloadUrl;
    }

    /// <summary>
    /// Mobile-side acknowledgement listener: decrypts and verifies a scanned token (signature, lease time) and,
    /// if valid and current, completes the handoff.
    /// </summary>
    public bool TryOpenHandoff(string token, out HandoffPayloadCodec.Payload payload, out string error)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool ok = HandoffPayloadCodec.TryDecode(token, handoffSecret, now, Mathf.CeilToInt(qrSessionTimeoutSeconds), out payload, out error);
        if (ok) ConfirmMobileHandoffReceived(token);
        return ok;
    }

    private int FloorOf(float y) { return Mathf.RoundToInt(y / Mathf.Max(0.1f, floorHeightMetres)); }

    private static string NodeKey(Vector3 w)
    {
        return Mathf.RoundToInt(w.x * 2f) + ":" + Mathf.RoundToInt(w.y * 2f) + ":" + Mathf.RoundToInt(w.z * 2f);
    }

    /// <summary>
    /// Called when visitor's smartphone scans QR code and opens mobile WebXR route.
    /// </summary>
    public void ConfirmMobileHandoffReceived(string token)
    {
        if (token == activeSessionToken && currentHandoffState == HandoffState.QRCodeAwaitingScan)
        {
            currentHandoffState = HandoffState.HandoffCompleted;
            Debug.Log($"[SmartKioskHandoffManager] Session '{token}' successfully transferred to mobile WebXR.");
            StartCoroutine(ResetToIdleAfterDelay(3.0f));
        }
    }

    private void CancelExpiredSession()
    {
        currentHandoffState = HandoffState.IdleOverview;
        activeSessionToken = "";
        sessionTimer = 0f;
        Debug.Log("[SmartKioskHandoffManager] Handoff session timed out. Reverting to idle overview.");
    }

    private IEnumerator ResetToIdleAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        currentHandoffState = HandoffState.IdleOverview;
        activeSessionToken = "";
    }
}
