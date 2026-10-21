using System.Collections.Generic;
using UnityEngine;
using S014.MobileAR;
using S021.Analytics;
using R057.Kiosk;

namespace S014.IntegratedDemo
{
    [DefaultExecutionOrder(-50)]
    public sealed class S014MasterDemoController : MonoBehaviour
    {
        [SerializeField] private S014NavigationController navigator;
        [SerializeField] private S014CampusRouteProvider routeProvider;
        [SerializeField] private S014TransformPoseProvider poseProvider;
        [SerializeField] private S021NavigationAnalytics analytics;
        [SerializeField] private J032.Telemetry.S021SpatialTelemetry telemetry;
        [SerializeField] private R057DestinationCatalog destinationCatalog;
        [SerializeField] private R057NavigationSession session;
        [SerializeField] private R057QRCodeHandoff qrHandoff;
        [SerializeField] private Camera demoCamera;
        [SerializeField] private S014InteriorRevealSwitch interiorRevealSwitch;
        [SerializeField] private Renderer representativeInteriorDisclaimer;
        [SerializeField] private bool demoMode = true;
        [SerializeField] private float demoSpeedMetersPerSecond = 3.5f;
        [SerializeField] private float cameraHeight = 1.6f;
        [SerializeField] private float cameraSmoothTime = 0.18f;
        [SerializeField] private float rotationSmoothSpeed = 8f;
        private readonly List<GameObject> markers = new List<GameObject>();
        private readonly List<Renderer> markerRenderers = new List<Renderer>();
        private readonly List<GameObject> routeLines = new List<GameObject>();
        private GameObject campusRouteLine;
        [SerializeField] private bool showRouteMarkers;
        private MaterialPropertyBlock markerPropertyBlock;
        private Vector3 cameraVelocity;
        private Vector3 initialPosePosition = new Vector3(0f, 1f, -6.5f);
        private Quaternion initialPoseRotation = Quaternion.identity;
        private CharacterController demoCharacterController;
        private float demoVerticalVelocity;
        private float blockedMovementSeconds;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle buttonStyle;
        private GUIStyle selectedButtonStyle;
        private GUIStyle destinationButtonStyle;
        private GUIStyle categoryFilterStyle;
        private GUIStyle summaryStyle;
        private GUIStyle statusStyle;
        private string statusMessage = "Select a destination to generate a route.";
        private bool showKiosk = true;
        private bool showArrival;
        private bool showAnalyticsPanel;
        private string searchText = string.Empty;
        private string categoryFilter = "All";
        private string csvExportPath = string.Empty;
        private int destinationPage;
        private const int DestinationsPerPage = 6;
        private const float GroundSupportProbeDistance = 1.25f;
        private const float MaximumWalkableSlopeDegrees = 50f;
        private float panelContentAvailableHeight;
        private Vector2 kioskScrollPosition;
        private bool showFloorPlan;
        private string[] categories = { "All", "Academic", "Student Services", "Amenities", "Access" };
        public string SelectedDestination => routeProvider == null ? string.Empty : routeProvider.SelectedDestination;

        private void Awake()
        {
            markerPropertyBlock = new MaterialPropertyBlock();
            if (representativeInteriorDisclaimer != null)
                representativeInteriorDisclaimer.enabled = false;
            if (demoCamera == null) demoCamera = Camera.main;
            if (destinationCatalog != null) destinationCatalog.EnsureDefaults();
            EnsureDemoCharacterController();
            var initialDestination = destinationCatalog == null || routeProvider == null
                ? null
                : destinationCatalog.Find(routeProvider.SelectedDestinationId) ??
                  destinationCatalog.Find(routeProvider.SelectedDestination);
            if (initialDestination != null && routeProvider.SetDestination(initialDestination) &&
                navigator != null && routeProvider != null && navigator.Route.Count == 0)
                navigator.LoadRoute(routeProvider.GetRoute());
            else if (initialDestination == null)
                Debug.LogError("The saved route destination is missing from the destination catalogue.");
            if (initialDestination != null) PreparePhoneHandoff(initialDestination);
            RebuildRouteVisuals();
            AlignCameraToPose(true);
        }

        private void OnEnable()
        {
            if (navigator == null || routeProvider == null || navigator.Route.Count > 0) return;
            var restoredRoute = routeProvider.GetRoute();
            if (restoredRoute == null || restoredRoute.Count == 0) return;
            navigator.LoadRoute(restoredRoute);
            RebuildRouteVisuals();
        }

        private void Start()
        {
            if (poseProvider == null || poseProvider.PoseTransform == null) return;

            if (demoMode && navigator != null && navigator.Route.Count > 0)
            {
                var pose = poseProvider.PoseTransform;
                initialPosePosition = navigator.Route[0].worldPosition;
                initialPoseRotation = Quaternion.identity;
                if (demoCharacterController != null) demoCharacterController.enabled = false;
                pose.SetPositionAndRotation(initialPosePosition, initialPoseRotation);
                if (demoCharacterController != null) demoCharacterController.enabled = true;
                AlignCameraToPose(true);
                return;
            }

            if (poseProvider.PoseTransform.position == Vector3.zero) return;
            initialPosePosition = poseProvider.PoseTransform.position;
            initialPoseRotation = poseProvider.PoseTransform.rotation;
        }

        private void EnsureDemoCharacterController()
        {
            if (!demoMode || poseProvider == null || poseProvider.PoseTransform == null) return;
            var pose = poseProvider.PoseTransform;
            demoCharacterController = pose.GetComponent<CharacterController>();
            if (demoCharacterController == null)
                demoCharacterController = pose.gameObject.AddComponent<CharacterController>();
            demoCharacterController.height = 1.75f;
            demoCharacterController.radius = 0.2f;
            demoCharacterController.center = new Vector3(0f, 0.875f, 0f);
            demoCharacterController.skinWidth = 0.025f;
            demoCharacterController.stepOffset = 0.32f;
            demoCharacterController.slopeLimit = 50f;
            demoCharacterController.minMoveDistance = 0f;
            demoCharacterController.detectCollisions = true;
        }

        private void UpdateDemoPose()
        {
            if (!demoMode || navigator == null || poseProvider == null || navigator.State != S014NavigationState.Active) return;
            var target = navigator.CurrentWaypoint;
            if (target == null) return;

            var poseTransform = poseProvider.PoseTransform;
            if (poseTransform == null) return;

            var movement = target.worldPosition - poseTransform.position;
            var horizontalMovement = Vector3.ProjectOnPlane(movement, Vector3.up);
            if (horizontalMovement.sqrMagnitude > 0.0001f)
            {
                var desiredRotation = Quaternion.LookRotation(horizontalMovement.normalized, Vector3.up);
                poseTransform.rotation = Quaternion.Slerp(
                    poseTransform.rotation,
                    desiredRotation,
                    1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
            }

            // Campus connectors are an explicitly inferred overlay, not a Unity NavMesh.
            // Move the demo camera along that graph so missing interior colliders do not
            // silently strand the route at the single representative building.
            if (routeProvider != null && routeProvider.UsesCampusRoute)
            {
                var remainingDistance = Vector3.Distance(poseTransform.position, target.worldPosition);
                poseTransform.position = remainingDistance <= navigator.WaypointArrivalRadius
                    ? target.worldPosition
                    : Vector3.MoveTowards(
                        poseTransform.position, target.worldPosition, demoSpeedMetersPerSecond * Time.deltaTime);
                return;
            }

            if (demoCharacterController == null)
            {
                statusMessage = "Navigation is blocked because collision-aware movement is unavailable.";
                navigator.PauseNavigation();
                return;
            }
            var beforeMove = poseTransform.position;
            if (!demoCharacterController.isGrounded && !HasGroundSupport(beforeMove))
            {
                demoVerticalVelocity = 0f;
                blockedMovementSeconds = 0f;
                statusMessage = "Navigation paused because there is no collidable walkable surface under the current position.";
                navigator.PauseNavigation();
                return;
            }
            var horizontalOffset = Vector3.ProjectOnPlane(target.worldPosition - beforeMove, Vector3.up);
            var horizontalMove = Vector3.MoveTowards(Vector3.zero, horizontalOffset, demoSpeedMetersPerSecond * Time.deltaTime);
            if (demoCharacterController.isGrounded && demoVerticalVelocity < 0f)
                demoVerticalVelocity = -2f;
            else
                demoVerticalVelocity += Physics.gravity.y * Time.deltaTime;
            var desiredMove = horizontalMove + Vector3.up * (demoVerticalVelocity * Time.deltaTime);
            var collisionFlags = demoCharacterController.Move(desiredMove);
            if (!demoCharacterController.isGrounded && !HasGroundSupport(poseTransform.position))
            {
                var displacement = beforeMove - poseTransform.position;
                if (displacement.sqrMagnitude > 0.000001f)
                    demoCharacterController.Move(displacement);
                demoVerticalVelocity = 0f;
                blockedMovementSeconds = 0f;
                statusMessage = "Navigation paused because the next movement step has no collidable walkable surface.";
                navigator.PauseNavigation();
                return;
            }
            if ((collisionFlags & CollisionFlags.Below) != 0)
                demoVerticalVelocity = -2f;
            else if ((collisionFlags & CollisionFlags.Above) != 0 && demoVerticalVelocity > 0f)
                demoVerticalVelocity = 0f;
            var movedDistance = Vector3.Distance(beforeMove, poseTransform.position);
            if (movedDistance < Mathf.Min(0.01f, desiredMove.magnitude * 0.1f) &&
                desiredMove.sqrMagnitude > 0.0025f && collisionFlags != CollisionFlags.None)
            {
                blockedMovementSeconds += Time.deltaTime;
                if (blockedMovementSeconds >= 0.5f)
                {
                    statusMessage = "Movement blocked by scene collision geometry at the current waypoint.";
                    navigator.PauseNavigation();
                }
            }
            else
            {
                blockedMovementSeconds = 0f;
            }
        }

        private static bool HasGroundSupport(Vector3 position)
        {
            var origin = position + Vector3.up * 0.15f;
            RaycastHit hit;
            return Physics.Raycast(
                origin,
                Vector3.down,
                out hit,
                GroundSupportProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) &&
                   Vector3.Angle(hit.normal, Vector3.up) <= MaximumWalkableSlopeDegrees;
        }

        public void SelectDestination(string destinationId)
        {
            if (routeProvider == null || navigator == null)
            {
                statusMessage = "Navigation is unavailable because required scene components are missing.";
                return;
            }
            var definition = destinationCatalog == null ? null : destinationCatalog.Find(destinationId);
            if (definition == null)
            {
                statusMessage = "Destination is unavailable in the catalogue.";
                return;
            }
            if (!routeProvider.SetDestination(definition))
            {
                statusMessage = "Destination could not be loaded from the campus route graph.";
                return;
            }
            var route = routeProvider.GetRoute();
            if (route.Count == 0)
            {
                routeProvider.SetGroundPassageOpen(false);
                navigator.ReplaceRoute(route);
                if (session != null) session.Clear();
                if (qrHandoff != null) qrHandoff.Clear();
                showKiosk = true;
                showArrival = false;
                RebuildRouteVisuals();
                statusMessage = "A route could not be built for this destination.";
                return;
            }
            if (interiorRevealSwitch != null)
            {
                var showPrototypeInterior = definition.id == "lab-101" || definition.id == "lab-102" ||
                                            definition.id == "lab-103" || definition.id == "student-help";
                interiorRevealSwitch.SetRevealMode(showPrototypeInterior);
            }
            routeProvider.SetGroundPassageOpen(false);
            navigator.ReplaceRoute(route);
            PlacePoseAtRouteStart(route);
            if (session != null) session.Clear();
            if (qrHandoff != null) qrHandoff.Clear();
            PreparePhoneHandoff(definition);
            if (analytics != null)
                analytics.ConfigureSession("DEMO", definition.displayName, definition.targetFloor);
            showKiosk = true;
            showArrival = false;
            statusMessage = definition.displayName + " route ready.";
            RebuildRouteVisuals();
        }

        public void StartNavigation()
        {
            var definition = destinationCatalog == null || routeProvider == null
                ? null
                : destinationCatalog.FindById(routeProvider.SelectedDestinationId);
            if (definition == null)
            {
                statusMessage = "Select a valid destination before starting.";
                return;
            }
            if (navigator == null || navigator.Route.Count == 0)
            {
                statusMessage = "Navigation cannot start because the route is empty.";
                return;
            }
            if (session == null)
            {
                statusMessage = "Navigation cannot start because the session service is missing.";
                return;
            }
            var pose = poseProvider == null ? null : poseProvider.PoseTransform;
            if (pose == null)
            {
                statusMessage = "Navigation cannot start because the current pose is unavailable.";
                return;
            }
            var routeStart = navigator.Route[0].worldPosition;
            if (routeProvider == null || !routeProvider.UsesCampusRoute)
            {
                if (Vector3.Distance(pose.position, routeStart) > 3f)
                {
                    statusMessage = "Navigation cannot start: the current demo pose is away from the selected route origin.";
                    return;
                }
                if (!HasGroundSupport(pose.position))
                {
                    statusMessage = "Navigation cannot start because no collidable walkable surface supports the current position.";
                    return;
                }
            }
            PlacePoseAtRouteStart(navigator.Route);
            var payload = PreparePhoneHandoff(definition);
            if (payload == null)
            {
                statusMessage = "Navigation cannot start because the phone handoff session is unavailable.";
                return;
            }
            if (analytics != null)
            {
                analytics.ConfigureSession(payload.sessionId, definition.displayName, definition.targetFloor);
                analytics.ReloadRoute();
            }
            navigator.StartNavigation();
            if (navigator.State != S014NavigationState.Active)
            {
                statusMessage = "Navigation did not start. Check the route and pose provider.";
                return;
            }
            if (telemetry != null) telemetry.SetSessionId(payload.sessionId);
            showKiosk = false;
            statusMessage = qrHandoff != null && qrHandoff.QrTexture != null
                ? "Navigation active. Phone handoff ready for " + definition.displayName + "."
                : "Navigation active, but the phone handoff is unavailable: " +
                  (qrHandoff == null ? "QR service is not assigned." : qrHandoff.ErrorMessage);
        }

        public void ResetDemo()
        {
            if (navigator != null) navigator.ResetNavigation();
            if (routeProvider != null) routeProvider.SetGroundPassageOpen(false);
            if (interiorRevealSwitch != null) interiorRevealSwitch.SetRevealMode(false);
            if (session != null) session.Clear();
            if (qrHandoff != null) qrHandoff.Clear();
            if (poseProvider != null && poseProvider.PoseTransform != null)
            {
                if (demoCharacterController != null) demoCharacterController.enabled = false;
                poseProvider.PoseTransform.SetPositionAndRotation(initialPosePosition, initialPoseRotation);
                if (demoCharacterController != null) demoCharacterController.enabled = true;
                demoVerticalVelocity = 0f;
                AlignCameraToPose(true);
            }
            blockedMovementSeconds = 0f;
            if (analytics != null && routeProvider != null)
            {
                var definition = destinationCatalog == null ? null : destinationCatalog.FindById(routeProvider.SelectedDestinationId);
                if (definition != null) analytics.ConfigureSession("DEMO", definition.displayName, definition.targetFloor);
            }
            showKiosk = true;
            showArrival = false;
            showAnalyticsPanel = false;
            csvExportPath = string.Empty;
            var selected = destinationCatalog == null || routeProvider == null
                ? null
                : destinationCatalog.FindById(routeProvider.SelectedDestinationId);
            if (selected != null) PreparePhoneHandoff(selected);
            statusMessage = routeProvider != null && !string.IsNullOrEmpty(routeProvider.SelectedDestinationId)
                ? "Session reset. Select a destination or restart navigation."
                : "Session reset. Select a destination.";
            RebuildRouteVisuals();
        }

        public void RestartCurrentRoute()
        {
            if (navigator == null || navigator.Route.Count == 0 || routeProvider == null)
            {
                statusMessage = "The current route cannot be restarted because no route is loaded.";
                return;
            }
            navigator.ResetNavigation();
            routeProvider.SetGroundPassageOpen(false);
            if (interiorRevealSwitch != null) interiorRevealSwitch.SetRevealMode(false);
            if (session != null) session.Clear();
            if (qrHandoff != null) qrHandoff.Clear();
            if (poseProvider == null || poseProvider.PoseTransform == null)
            {
                statusMessage = "The current route cannot restart because the demo pose is unavailable.";
                showKiosk = true;
                showArrival = false;
                return;
            }
            SetPoseAtRouteStart();
            showKiosk = false;
            showArrival = false;
            showAnalyticsPanel = false;
            StartNavigation();
            if (navigator.State != S014NavigationState.Active) showKiosk = true;
            RebuildRouteVisuals();
        }

        public void ReturnToDestinationSelection()
        {
            if (navigator != null) navigator.ResetNavigation();
            if (routeProvider != null) routeProvider.SetGroundPassageOpen(false);
            if (interiorRevealSwitch != null) interiorRevealSwitch.SetRevealMode(false);
            if (session != null) session.Clear();
            if (qrHandoff != null) qrHandoff.Clear();
            if (poseProvider != null && poseProvider.PoseTransform != null && navigator != null &&
                navigator.Route.Count > 0)
                SetPoseAtRouteStart();
            showKiosk = true;
            showArrival = false;
            showAnalyticsPanel = false;
            statusMessage = "Select a destination. The previous route and phone handoff were cleared.";
            RebuildRouteVisuals();
        }

        private void SetPoseAtRouteStart()
        {
            if (navigator == null || navigator.Route.Count == 0 ||
                poseProvider == null || poseProvider.PoseTransform == null)
                return;
            var start = navigator.Route[0].worldPosition;
            if (demoCharacterController != null) demoCharacterController.enabled = false;
            poseProvider.PoseTransform.SetPositionAndRotation(start, initialPoseRotation);
            if (demoCharacterController != null) demoCharacterController.enabled = true;
            initialPosePosition = start;
            demoVerticalVelocity = 0f;
            blockedMovementSeconds = 0f;
            AlignCameraToPose(true);
        }

        private R057NavigationPayload PreparePhoneHandoff(R057DestinationDefinition definition)
        {
            if (session == null || routeProvider == null || definition == null)
            {
                if (qrHandoff != null) qrHandoff.Clear();
                return null;
            }

            var payload = session.CurrentPayload;
            if (payload == null ||
                !string.Equals(payload.destinationId, definition.id, System.StringComparison.Ordinal) ||
                !string.Equals(payload.routeId, routeProvider.RouteId, System.StringComparison.Ordinal))
                payload = session.Begin(routeProvider.RouteId, definition);

            if (qrHandoff != null) qrHandoff.Generate(payload);
            return payload;
        }

        public void ToggleInteriorReveal()
        {
            if (interiorRevealSwitch == null) return;
            interiorRevealSwitch.SetRevealMode(!interiorRevealSwitch.RevealMode);
        }

        public void ExportCsv()
        {
            if (analytics == null)
            {
                statusMessage = "CSV export is unavailable because S021 analytics is not assigned.";
                Debug.LogError(statusMessage, this);
                return;
            }

            try
            {
                csvExportPath = analytics.ExportCompletedCsv();
                statusMessage = analytics.HasCompletedSession
                    ? "Completed session appended to CSV: " + csvExportPath
                    : "CSV is ready with headers; no completed session data was added: " + csvExportPath;
            }
            catch (System.InvalidOperationException exception)
            {
                statusMessage = "CSV export skipped: " + exception.Message;
                Debug.LogWarning(statusMessage, this);
            }
            catch (System.IO.IOException exception)
            {
                statusMessage = "CSV export failed: " + exception.Message;
                Debug.LogError(statusMessage, this);
            }
            catch (System.UnauthorizedAccessException exception)
            {
                statusMessage = "CSV export failed: " + exception.Message;
                Debug.LogError(statusMessage, this);
            }
        }

        private void LateUpdate()
        {
            UpdateDemoPose();
            AlignCameraToPose(false);
            UpdateMarkerPresentation();
            if (navigator != null && navigator.State == S014NavigationState.Complete && !showArrival)
            {
                showArrival = true;
                statusMessage = "Destination reached. Navigation complete.";
            }
        }

        private void OnGUI()
        {
            if (navigator == null || routeProvider == null) return;
            if (panelStyle == null) BuildStyles();
            var compactLayout = Screen.width < 760;
            var margin = compactLayout ? 8f : 18f;
            var width = compactLayout ? Mathf.Max(160f, Screen.width - margin * 2f) :
                Mathf.Clamp(Screen.width * 0.31f, 320f, 430f);
            var height = compactLayout ? Mathf.Clamp(Screen.height * 0.52f, 280f, 480f) : Screen.height - 36f;
            var panelY = compactLayout ? Screen.height - height - margin : margin;
            var panel = new Rect(margin, panelY, width, height);
            panelContentAvailableHeight = panel.height - 36f;
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(panel.x + 16f, panel.y + 12f, panel.width - 32f, panel.height - 24f));
            if (showKiosk)
            {
                DrawKiosk();
            }
            else
            {
                DrawNavigationStatus();
            }
            GUILayout.EndArea();
        }

        private void DrawKiosk()
        {
            if (showAnalyticsPanel)
            {
                DrawAnalyticsPanel();
                return;
            }

            var footerHeight = Screen.width < 760 ? 112f : Mathf.Clamp(Screen.height * 0.22f, 128f, 156f);
            var scrollHeight = Mathf.Max(100f, panelContentAvailableHeight - footerHeight);
            kioskScrollPosition = GUILayout.BeginScrollView(kioskScrollPosition, GUILayout.Height(scrollHeight));
            GUILayout.Label("SMART CAMPUS", titleStyle);
            GUILayout.Label("AR WAYFINDING SYSTEM", bodyStyle);
            GUILayout.Label("PROTOTYPE ROUTES — representative interior and inferred outdoor links.", statusStyle);
            if (GUILayout.Button(showFloorPlan ? "HIDE PROTOTYPE FLOOR PLAN" : "VIEW PROTOTYPE FLOOR PLAN", buttonStyle, GUILayout.Height(32f)))
                showFloorPlan = !showFloorPlan;
            if (showFloorPlan) DrawPrototypeFloorPlan();
            if (interiorRevealSwitch != null)
            {
                GUILayout.Label("REPRESENTATIVE CUTAWAY — NOT A VERIFIED CAMPUS FLOOR PLAN", statusStyle);
                if (GUILayout.Button(interiorRevealSwitch.RevealMode ? "HIDE CUTAWAY / RESTORE BUILDING" : "REVEAL INTERIOR CUTAWAY", buttonStyle, GUILayout.Height(32f)))
                    ToggleInteriorReveal();
            }
            GUILayout.Space(4f);
            GUILayout.Label("WHERE WOULD YOU LIKE TO GO?", bodyStyle);
            var previousSearch = searchText;
            searchText = GUILayout.TextField(searchText, GUILayout.Height(30f));
            if (searchText != previousSearch) destinationPage = 0;
            DrawDestinationPage();
            DrawPhoneHandoff();
            if (qrHandoff != null && !string.IsNullOrEmpty(qrHandoff.ErrorMessage))
                GUILayout.Label(qrHandoff.ErrorMessage, statusStyle);
            if (GUILayout.Button("ADMIN ANALYTICS / CSV EXPORT", buttonStyle, GUILayout.Height(30f)))
                showAnalyticsPanel = true;
            GUILayout.EndScrollView();

            GUILayout.BeginVertical(GUILayout.Height(footerHeight));
            GUILayout.Label("ROUTE SUMMARY", statusStyle);
            GUILayout.Label(routeProvider.RouteId + "  ·  " + routeProvider.SelectedDestination + "  ·  " +
                navigator.Route.Count + " waypoints  ·  " + CalculateRouteDistance().ToString("0.0") + " m",
                summaryStyle, GUILayout.Height(32f));
            var routeResult = routeProvider.CurrentRouteResult;
            if (routeResult != null)
                GUILayout.Label(routeResult.EstimatedWalkingTimeSeconds.ToString("0") + " s estimated  ·  " +
                    routeResult.FloorTransitionCount + " floor transitions  ·  graph route",
                    statusStyle, GUILayout.Height(22f));
            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = previousGuiEnabled && CanStartNavigation();
            var startClicked = GUILayout.Button("START NAVIGATION", selectedButtonStyle, GUILayout.Height(46f));
            GUI.enabled = previousGuiEnabled;
            if (startClicked)
                StartNavigation();
            GUILayout.Label(statusMessage, statusStyle, GUILayout.Height(24f));
            GUILayout.EndVertical();
        }

        private bool CanStartNavigation()
        {
            if (navigator == null || routeProvider == null || session == null ||
                poseProvider == null || poseProvider.PoseTransform == null ||
                navigator.Route.Count == 0)
                return false;
            var routeResult = routeProvider.CurrentRouteResult;
            return routeResult != null &&
                   System.String.Equals(
                       routeResult.DestinationId,
                       routeProvider.SelectedDestinationId,
                       System.StringComparison.Ordinal);
        }

        private void DrawAnalyticsPanel()
        {
            GUILayout.BeginScrollView(kioskScrollPosition, GUILayout.Height(Mathf.Max(220f, Screen.height - 60f)));
            GUILayout.Label("S021 ADMIN ANALYTICS", titleStyle);
            GUILayout.Label("Demo movement and participant ID are simulated. Survey scores remain blank until explicitly collected.", bodyStyle);
            var metrics = analytics == null ? null : analytics.Metrics;
            if (metrics == null)
            {
                GUILayout.Label("No analytics session is currently available.", statusStyle);
            }
            else
            {
                GUILayout.Label("SESSION  " + metrics.sessionId, bodyStyle);
                GUILayout.Label("DESTINATION  " + (string.IsNullOrEmpty(metrics.destination) ? "Not selected" : metrics.destination), bodyStyle);
                GUILayout.Label("STATUS  " + metrics.completionStatus, bodyStyle);
                GUILayout.Label("DURATION  " + metrics.transitDurationSeconds.ToString("0.0") + " s", bodyStyle);
                GUILayout.Label("DISTANCE  " + metrics.totalDistanceMeters.ToString("0.0") + " m actual / " +
                    metrics.expectedDistanceMeters.ToString("0.0") + " m expected", bodyStyle);
                GUILayout.Label("DEVIATIONS  " + metrics.deviationCount + "  ·  WRONG TURNS  " + metrics.wrongTurnCount +
                    "  ·  FLOOR TRANSITIONS  " + metrics.floorTransitions, bodyStyle);
                GUILayout.Label("SURVEY SCORES  " + (metrics.hasSurveyScores
                    ? metrics.susScore.ToString("0.0") + " SUS / " + metrics.nasaTlxScore.ToString("0.0") + " NASA-TLX"
                    : "not collected"), bodyStyle);
            }

            if (GUILayout.Button("EXPORT COMPLETED SESSION CSV", selectedButtonStyle, GUILayout.Height(42f)))
                ExportCsv();
            if (!string.IsNullOrEmpty(csvExportPath))
                GUILayout.TextArea(csvExportPath, GUILayout.Height(44f));
            GUILayout.Label(statusMessage, statusStyle);
            if (GUILayout.Button("BACK TO DESTINATIONS", buttonStyle, GUILayout.Height(36f)))
                showAnalyticsPanel = false;
            GUILayout.EndScrollView();
        }

        private void DrawNavigationStatus()
        {
            var footerHeight = Screen.width < 760 ? 96f : 94f;
            var contentHeight = Mathf.Max(90f, panelContentAvailableHeight - footerHeight);
            kioskScrollPosition = GUILayout.BeginScrollView(kioskScrollPosition, GUILayout.Height(contentHeight));
            GUILayout.Label("SMART CAMPUS", titleStyle);
            GUILayout.Label("LIVE NAVIGATION", bodyStyle);
            var waypoint = navigator.CurrentWaypoint;
            var stateLabel = navigator.State == S014NavigationState.Complete ? "ARRIVED" :
                navigator.State == S014NavigationState.Paused ? "PAUSED" :
                navigator.State == S014NavigationState.Active ? "NAVIGATING" : "READY";
            GUILayout.Label(stateLabel, titleStyle);
            GUILayout.Label("DESTINATION  " + routeProvider.SelectedDestination, bodyStyle);
            GUILayout.Label("NEXT  " + (waypoint == null ? "Complete" : waypoint.instruction), bodyStyle);
            GUILayout.Label("DISTANCE  " + (waypoint == null ? "0.0" : navigator.CurrentDistanceMeters.ToString("0.0") + " m"), bodyStyle);
            GUILayout.Label("FLOOR  " + (waypoint == null ? "-" : waypoint.floor.ToUpperInvariant()), bodyStyle);
            GUILayout.Label("PROGRESS  " + Mathf.Min(navigator.CurrentWaypointIndex + 1, navigator.Route.Count) + " / " + navigator.Route.Count, bodyStyle);
            if (GUILayout.Button(showFloorPlan ? "HIDE CAMPUS ROUTE PLAN" : "VIEW CAMPUS ROUTE PLAN", buttonStyle, GUILayout.Height(36f)))
                showFloorPlan = !showFloorPlan;
            if (showFloorPlan) DrawPrototypeFloorPlan();
            DrawPhoneHandoff();
            GUILayout.Space(8f);
            if (navigator.State == S014NavigationState.Active)
            {
                if (GUILayout.Button("PAUSE NAVIGATION", buttonStyle, GUILayout.Height(36f)))
                {
                    navigator.PauseNavigation();
                    statusMessage = "Navigation paused.";
                }
            }
            else if (navigator.State == S014NavigationState.Paused)
            {
                if (GUILayout.Button("RESUME NAVIGATION", selectedButtonStyle, GUILayout.Height(36f)))
                {
                    navigator.ResumeNavigation();
                    statusMessage = "Navigation resumed.";
                }
            }
            if (navigator.Route.Count > 0 &&
                GUILayout.Button("RESTART CURRENT ROUTE", selectedButtonStyle, GUILayout.Height(38f)))
                RestartCurrentRoute();
            if (GUILayout.Button("CHANGE DESTINATION", buttonStyle, GUILayout.Height(38f)))
                ReturnToDestinationSelection();
            GUILayout.Label(statusMessage, statusStyle);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal(GUILayout.Height(48f));
            if (GUILayout.Button("RESET SESSION", buttonStyle, GUILayout.Height(44f))) ResetDemo();
            if (GUILayout.Button("EXPORT CSV", selectedButtonStyle, GUILayout.Height(44f))) ExportCsv();
            GUILayout.EndHorizontal();
        }

        private void DrawPrototypeFloorPlan()
        {
            var route = navigator == null ? null : navigator.Route;
            GUILayout.Label("FULL CAMPUS ROUTE · " + (routeProvider == null ? "" : routeProvider.SelectedDestination), statusStyle);
            GUILayout.Label("Starts at the inferred campus entrance approach and follows the selected route across the imported site layout. Connectors and destination approaches are inferred.", statusStyle);
            if (route == null || route.Count == 0) return;
            var mapRect = GUILayoutUtility.GetRect(280f, 210f, GUILayout.ExpandWidth(true));
            GUI.Box(mapRect, GUIContent.none, panelStyle);
            var plan = new Rect(mapRect.x + 18f, mapRect.y + 16f, mapRect.width - 36f, mapRect.height - 32f);
            GUI.color = new Color(0.08f, 0.17f, 0.2f, 1f);
            GUI.DrawTexture(plan, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Box(plan, "IMPORTED CAMPUS · ROUTE OVERLAY", statusStyle);
            var minX = float.PositiveInfinity; var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity; var maxZ = float.NegativeInfinity;
            for (var i = 0; i < route.Count; i++)
            {
                var p = route[i].worldPosition;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            var width = Mathf.Max(1f, maxX - minX); var depth = Mathf.Max(1f, maxZ - minZ);
            Vector2 MapPoint(Vector3 p) => new Vector2(
                plan.x + 14f + (p.x - minX) / width * (plan.width - 28f),
                plan.y + plan.height - 14f - (p.z - minZ) / depth * (plan.height - 28f));
            for (var i = 1; i < route.Count; i++)
                DrawRoutePlanSegment(MapPoint(route[i - 1].worldPosition), MapPoint(route[i].worldPosition),
                    new Color(0.2f, 0.9f, 1f, 1f), 4f);
            var start = MapPoint(route[0].worldPosition);
            var end = MapPoint(route[route.Count - 1].worldPosition);
            GUI.color = new Color(1f, 0.68f, 0.1f, 1f);
            GUI.DrawTexture(new Rect(start.x - 6f, start.y - 6f, 12f, 12f), Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 1f, 0.56f, 1f);
            GUI.DrawTexture(new Rect(end.x - 6f, end.y - 6f, 12f, 12f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(plan.x + 5f, plan.y + 20f, 110f, 18f), "START · CAMPUS ENTRY", statusStyle);
            GUI.Label(new Rect(plan.x + plan.width - 132f, plan.y + plan.height - 22f, 130f, 18f), "DESTINATION APPROACH", statusStyle);
            GUILayout.Label("" + route.Count + " route points · " + CalculateRouteDistance().ToString("0") + " m · inferred campus links", statusStyle);
        }

        private static void DrawRoutePlanSegment(Vector2 from, Vector2 to, Color color, float thickness)
        {
            var delta = to - from;
            if (delta.sqrMagnitude < 0.01f) return;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.color = color;
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(-angle, from);
            GUI.color = Color.white;
        }

        private void DrawPhoneHandoff()
        {
            if (qrHandoff == null || qrHandoff.QrTexture == null) return;
            GUILayout.Label("CONTINUE ON YOUR PHONE", titleStyle);
            GUILayout.Label("Scan this locally generated QR for the selected destination.", bodyStyle);
            GUI.DrawTexture(GUILayoutUtility.GetRect(224f, 224f), qrHandoff.QrTexture, ScaleMode.ScaleToFit);
            GUILayout.TextArea(qrHandoff.PayloadUrl, GUILayout.Height(42f));
            if (GUILayout.Button("COPY PHONE LINK", buttonStyle, GUILayout.Height(32f)))
            {
                GUIUtility.systemCopyBuffer = qrHandoff.PayloadUrl;
                statusMessage = "Phone handoff URL copied.";
            }
        }

        private int CountFloorTransitions()
        {
            var count = 0;
            foreach (var waypoint in routeProvider.GetRoute()) if (waypoint.isFloorTransition) count++;
            return count;
        }

        private float CalculateRouteDistance()
        {
            var total = 0f;
            var route = routeProvider.GetRoute();
            for (var i = 1; i < route.Count; i++) total += Vector3.Distance(route[i - 1].worldPosition, route[i].worldPosition);
            return total;
        }

        private void BuildStyles()
        {
            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = MakeTexture(new Color(0.025f, 0.06f, 0.11f, 0.94f));
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.35f, 0.9f, 1f) } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = new Color(0.86f, 0.93f, 0.97f) } };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 6, 6) };
            selectedButtonStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            destinationButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5),
                wordWrap = false
            };
            destinationButtonStyle.normal.textColor = new Color(0.96f, 0.99f, 1f);
            destinationButtonStyle.hover.textColor = Color.white;
            destinationButtonStyle.active.textColor = Color.white;
            categoryFilterStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, wordWrap = false };
            summaryStyle = new GUIStyle(bodyStyle) { fontSize = 12, wordWrap = false };
            statusStyle = new GUIStyle(bodyStyle) { fontSize = 11 };
        }

        private void DrawDestinationPage()
        {
            var selectedCategory = System.Array.IndexOf(categories, categoryFilter);
            var nextCategory = GUILayout.SelectionGrid(selectedCategory, categories, 3, categoryFilterStyle, GUILayout.Height(54f));
            if (nextCategory != selectedCategory)
            {
                categoryFilter = categories[nextCategory];
                destinationPage = 0;
            }
            DrawFilteredDestinationPage();
        }

        private void DrawFilteredDestinationPage()
        {
            var filtered = new List<R057DestinationDefinition>();
            if (destinationCatalog != null)
            {
                foreach (var definition in destinationCatalog.Destinations)
                {
                    if (categoryFilter != "All" && definition.category != categoryFilter) continue;
                    if (!string.IsNullOrWhiteSpace(searchText) &&
                        definition.displayName.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    filtered.Add(definition);
                }
            }

            var pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)DestinationsPerPage));
            destinationPage = Mathf.Clamp(destinationPage, 0, pageCount - 1);
            var start = destinationPage * DestinationsPerPage;
            var end = Mathf.Min(start + DestinationsPerPage, filtered.Count);

            for (var i = start; i < end; i++)
            {
                var definition = filtered[i];
                var label = definition.displayName + "  [" + definition.category + "]";
                if (GUILayout.Button(label, destinationButtonStyle, GUILayout.Height(32f)))
                    SelectDestination(definition.id);
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = destinationPage > 0;
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(42f), GUILayout.Height(28f))) destinationPage--;
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.Label("PAGE " + (destinationPage + 1) + " / " + pageCount, statusStyle, GUILayout.Width(100f));
            GUILayout.FlexibleSpace();
            GUI.enabled = destinationPage < pageCount - 1;
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(42f), GUILayout.Height(28f))) destinationPage++;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void RebuildRouteVisuals()
        {
            CollectPersistedRouteVisuals();
            if (markerPropertyBlock == null) markerPropertyBlock = new MaterialPropertyBlock();
            foreach (var marker in markers) if (marker != null) DestroyGeneratedObject(marker);
            markers.Clear();
            markerRenderers.Clear();
            foreach (var lineObject in routeLines) if (lineObject != null) DestroyGeneratedObject(lineObject);
            routeLines.Clear();
            campusRouteLine = null;
            if (!showRouteMarkers) return;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.1f, 0.8f, 1f, 0.9f) };
            var route = routeProvider.GetRoute();
            if (route.Count > 1)
            {
                campusRouteLine = new GameObject("CampusWideRouteLine");
                routeLines.Add(campusRouteLine);
                var line = campusRouteLine.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = route.Count;
                // Keep the route narrow and close to each walking surface so
                // stair risers remain visible and the ribbon does not float
                // across the stairwell opening.
                line.startWidth = 0.09f;
                line.endWidth = 0.09f;
                line.startColor = new Color(1f, 0.72f, 0.12f, 1f);
                line.endColor = new Color(0.1f, 0.85f, 1f, 1f);
                line.material = material;
                line.numCapVertices = 3;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                for (var i = 0; i < route.Count; i++)
                    line.SetPosition(i, route[i].worldPosition + Vector3.up * 0.12f);
            }
            for (var routeIndex = 0; routeIndex < route.Count; routeIndex++)
            {
                var waypoint = route[routeIndex];
                if (routeIndex != 0 && routeIndex != route.Count - 1 && !waypoint.isFloorTransition)
                    continue;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "RouteWaypoint_" + waypoint.index;
                marker.transform.position = waypoint.worldPosition;
                marker.transform.localScale = Vector3.one * 0.28f;
                var renderer = marker.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                var collider = marker.GetComponent<Collider>();
                if (collider != null) DestroyGeneratedObject(collider);
                markers.Add(marker);
                markerRenderers.Add(renderer);
            }
            UpdateMarkerPresentation();
        }

        private void CollectPersistedRouteVisuals()
        {
            // Older scene saves may contain route markers from a previous
            // destination. Track them before rebuilding so the old line cannot
            // remain visible over the freshly computed campus route.
            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || !candidate.scene.IsValid() || candidate.scene != gameObject.scene)
                    continue;
                if (candidate.name == "CampusWideRouteLine")
                {
                    if (!routeLines.Contains(candidate)) routeLines.Add(candidate);
                    if (campusRouteLine == null) campusRouteLine = candidate;
                    continue;
                }
                if (!candidate.name.StartsWith("RouteWaypoint_", System.StringComparison.Ordinal))
                    continue;
                if (markers.Contains(candidate)) continue;
                markers.Add(candidate);
                var renderer = candidate.GetComponent<Renderer>();
                if (renderer != null) markerRenderers.Add(renderer);
            }
        }

        private static void DestroyGeneratedObject(UnityEngine.Object generatedObject)
        {
            if (generatedObject == null) return;
            if (Application.isPlaying) Destroy(generatedObject);
            else DestroyImmediate(generatedObject);
        }

        private void PlacePoseAtRouteStart(IReadOnlyList<S014Waypoint> route)
        {
            if (route == null || route.Count == 0 || poseProvider == null || poseProvider.PoseTransform == null) return;
            var pose = poseProvider.PoseTransform;
            var direction = route.Count > 1
                ? Vector3.ProjectOnPlane(route[1].worldPosition - route[0].worldPosition, Vector3.up)
                : Vector3.zero;
            var rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : pose.rotation;
            if (demoCharacterController != null) demoCharacterController.enabled = false;
            pose.SetPositionAndRotation(route[0].worldPosition, rotation);
            if (demoCharacterController != null) demoCharacterController.enabled = true;
            initialPosePosition = pose.position;
            initialPoseRotation = pose.rotation;
            demoVerticalVelocity = 0f;
            AlignCameraToPose(true);
        }

        private void AlignCameraToPose(bool immediate)
        {
            if (demoCamera == null || poseProvider == null || poseProvider.PoseTransform == null) return;

            var pose = poseProvider.PoseTransform;
            var desiredPosition = pose.position + Vector3.up * cameraHeight;
            var target = navigator == null ? null : navigator.CurrentWaypoint;
            if (target != null)
            {
                var lookDirection = Vector3.ProjectOnPlane(target.worldPosition - pose.position, Vector3.up);
                if (lookDirection.sqrMagnitude > 0.0001f)
                    pose.rotation = Quaternion.Slerp(pose.rotation, Quaternion.LookRotation(lookDirection, Vector3.up), 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
            }

            if (immediate)
                demoCamera.transform.position = desiredPosition;
            else
                demoCamera.transform.position = Vector3.SmoothDamp(demoCamera.transform.position, desiredPosition, ref cameraVelocity, cameraSmoothTime);

            var cameraForward = pose.forward;
            if (target != null)
            {
                var targetDirection = Vector3.ProjectOnPlane(target.worldPosition - demoCamera.transform.position, Vector3.up);
                if (targetDirection.sqrMagnitude > 0.0001f)
                    cameraForward = Vector3.Slerp(cameraForward, targetDirection.normalized, 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
            }
            if (cameraForward.sqrMagnitude > 0.0001f)
                demoCamera.transform.rotation = Quaternion.LookRotation(cameraForward, Vector3.up);
        }

        private void UpdateMarkerPresentation()
        {
            var currentIndex = navigator == null ? -1 : navigator.CurrentWaypointIndex;
            for (var i = 0; i < markerRenderers.Count; i++)
            {
                var renderer = markerRenderers[i];
                if (renderer == null) continue;
                var current = i == currentIndex;
                markerPropertyBlock.SetColor("_BaseColor", current ? new Color(1f, 0.65f, 0.05f, 1f) : new Color(0.1f, 0.8f, 1f, 1f));
                renderer.SetPropertyBlock(markerPropertyBlock);
                var marker = markers[i];
                if (marker != null)
                    marker.transform.localScale = Vector3.one * (current ? 0.48f : 0.28f);
            }
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
