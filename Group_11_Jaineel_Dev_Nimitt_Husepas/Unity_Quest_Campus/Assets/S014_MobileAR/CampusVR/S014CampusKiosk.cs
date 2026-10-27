using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace S014.CampusVR
{
    /// <summary>The situated kiosk: pick a floor, pick a room, choose Lift or
    /// Stairs, then scan the QR to load the session and start guided wayfinding.
    /// The kiosk stands by the main gate. After a destination is reached it can
    /// be summoned beside the player, and RESET returns everything to the gate.</summary>
    public class S014CampusKiosk : MonoBehaviour
    {
        public S014CampusDirectory directory;
        public S014CampusWayfindingGuide guide;
        public Transform playerRig;
        public Renderer qrQuad;
        public TextMesh selectedText;
        public GameObject qrPanel;
        public List<GameObject> floorPanels = new List<GameObject>(); // index = level 0..9
        public Renderer liftTile;
        public Renderer stairsTile;
        public GameObject liftConfirmPanel;   // "I've entered the lift" toggle

        public string SelectedId { get; private set; }
        public bool UseLift { get; private set; }
        public bool Busy { get { return scanning || riding; } }

        /// <summary>Raised after RESET so other UI (arrival banner) can hide.</summary>
        public event Action OnReset;

        private CampusDestinationAnchor pendingLiftDest;
        private Vector3 homePos;
        private Quaternion homeRot;
        private bool scanning, riding;
        private float shaftZ;

        private void Awake()
        {
            homePos = transform.position;
            homeRot = transform.rotation;
        }

        private void Start()
        {
            ShowFloor(1);
            SetModeHighlight();
            if (liftConfirmPanel != null) liftConfirmPanel.SetActive(false);
            if (qrPanel != null) qrPanel.SetActive(false);
            if (selectedText != null) selectedText.text = "Pick a destination";
            StartCoroutine(PlaceAtLobbyOnStart());
        }

        // The headset reports its first real pose a moment after start, so
        // align the rig to the gate lobby (facing the kiosk) twice.
        private IEnumerator PlaceAtLobbyOnStart()
        {
            yield return new WaitForSeconds(0.4f);
            TeleportToLobby();
            yield return new WaitForSeconds(1.2f);
            TeleportToLobby();
        }

        public void ShowFloor(int level)
        {
            for (int i = 0; i < floorPanels.Count; i++)
                if (floorPanels[i] != null) floorPanels[i].SetActive(i == level);
        }

        public void Select(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (id == "__reset__") { ResetSession(); return; }
            if (id == "__enterlift__") { EnterLiftConfirmed(); return; }
            if (Busy) return;   // ignore taps during scan / lift ride

            if (id.StartsWith("floor:")) { int lv; int.TryParse(id.Substring(6), out lv); ShowFloor(lv); return; }
            if (id == "mode:lift") { UseLift = true; SetModeHighlight(); UpdateSelText(); return; }
            if (id == "mode:stairs") { UseLift = false; SetModeHighlight(); UpdateSelText(); return; }
            if (id == "__start__") { ConfirmScanAndStart(); return; }

            if (directory != null && directory.Find(id) == null) return;
            SelectedId = id;
            pendingLiftDest = null;
            if (liftConfirmPanel != null) liftConfirmPanel.SetActive(false);
            if (qrPanel != null) qrPanel.SetActive(true);
            if (qrQuad != null) { qrQuad.material.color = Color.white; qrQuad.material.mainTexture = MakeQr(id); }
            UpdateSelText();
            var sel = directory != null ? directory.Find(id) : null;
            if (sel != null) S014AgentVoice.Instance.Say("p_selected", S014AgentVoice.NameKey(sel.id), "p_choose_mode");
        }

        private void UpdateSelText()
        {
            var d = directory != null ? directory.Find(SelectedId) : null;
            if (selectedText != null)
                selectedText.text = d != null
                    ? Wrap(d.displayName, 17) + "\nFloor " + d.floorLabel + "\nBy " + (UseLift ? "LIFT" : "STAIRS") + "\nPress SCAN"
                    : "Pick a destination";
        }

        // The kiosk text area is narrow: break long names into short lines.
        private static string Wrap(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            var words = text.Split(' '); var sb = new System.Text.StringBuilder(); int line = 0;
            foreach (var w in words)
            {
                if (line > 0 && line + 1 + w.Length > max) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(w); line += w.Length;
            }
            return sb.ToString();
        }

        private void SetModeHighlight()
        {
            var on = new Color(0.22f, 0.70f, 0.32f);
            var off = new Color(0.12f, 0.20f, 0.34f);
            if (liftTile != null) liftTile.material.color = UseLift ? on : off;
            if (stairsTile != null) stairsTile.material.color = !UseLift ? on : off;
        }

        // ---------------------------------------------------------------- scan

        public void ConfirmScanAndStart()
        {
            if (Busy) return;
            var d = directory != null ? directory.Find(SelectedId) : null;
            if (d == null)
            {
                if (selectedText != null) selectedText.text = "Pick a room first,\nthen press SCAN";
                return;
            }
            if (guide == null) return;
            StartCoroutine(ScanSequence(d));
        }

        // select -> QR shown -> QR scanned -> session loaded -> route starts
        private IEnumerator ScanSequence(CampusDestinationAnchor d)
        {
            scanning = true;
            if (selectedText != null) selectedText.text = "Scanning QR...\n" + Wrap(d.displayName, 17);

            float t = 0f;
            while (t < 1.1f)
            {
                t += Time.deltaTime;
                if (qrQuad != null) qrQuad.material.color = Color.Lerp(Color.white, new Color(0.55f, 1f, 0.6f), Mathf.PingPong(t * 4f, 1f));
                yield return null;
            }
            if (qrQuad != null) qrQuad.material.color = new Color(0.55f, 1f, 0.6f);
            if (selectedText != null)
                selectedText.text = "QR scanned - OK\nSession loaded:\n" + Wrap(d.displayName, 17) + "\nFloor " + d.floorLabel;
            S014AgentVoice.Instance.Say("p_qr_ok");
            yield return new WaitForSeconds(1.0f);

            if (qrPanel != null) qrPanel.SetActive(false);
            scanning = false;
            yield return StartCoroutine(StartRoute(d));
        }

        private IEnumerator StartRoute(CampusDestinationAnchor d)
        {
            // Routes start wherever the visitor stands now (any floor, even on a stair flight),
            // planned over the walkable grid; only if no route can be planned from here do we
            // fall back to the main-gate start.
            Vector3 here = playerRig != null ? playerRig.position : LobbyPos();
            float floorY = Mathf.Round((here.y + 0.1f) / 4f) * 4f;
            bool useLift = UseLift && playerRig != null && Mathf.Abs(floorY - d.liftExit.y) > 1.0f;

            List<Vector3> route = null;
            if (playerRig != null)
                route = useLift ? S014WalkGrid.FindPath(here, new Vector3(0f, floorY, 35f)) : S014WalkGrid.FindPath(here, d.anchor != null ? d.anchor.position : here);
            Debug.Log("[S014] route from " + here.ToString("F1") + " to " + d.id + " by " + (useLift ? "lift" : "stairs") + ": " + (route != null ? route.Count + " points" : "none"));

            if (route == null && playerRig != null && !IsNearLobby())
            {
                if (selectedText != null) selectedText.text = "No route from here.\nReturning to the main gate...";
                S014AgentVoice.Instance.Say("p_no_path");
                yield return new WaitForSeconds(2.5f);
                TeleportToLobby();
                ReturnHome();
                here = playerRig.position;
                floorY = Mathf.Round((here.y + 0.1f) / 4f) * 4f;
                useLift = UseLift && Mathf.Abs(floorY - d.liftExit.y) > 1.0f;
                route = useLift ? S014WalkGrid.FindPath(here, new Vector3(0f, floorY, 35f)) : null;
            }

            if (useLift)
            {
                pendingLiftDest = d;
                shaftZ = 35f;
                if (route != null) guide.GuideToLift(d, route);
                else guide.GuideToLift(d, new Vector3(0f, playerRig.position.y, shaftZ));
                if (selectedText != null)
                    selectedText.text = Wrap(d.displayName, 17) + "\nFloor " + d.floorLabel + "\nFollow the line\nto the LIFT and\nstep inside";
                liftPanelPlaced = false;
                S014AgentVoice.Instance.Say("p_to_lift");
            }
            else
            {
                if (route != null) guide.BeginExplicit(d, route); else guide.Begin(d);
                if (selectedText != null) selectedText.text = Wrap(d.displayName, 17) + "\nFloor " + d.floorLabel + "\nFollow the line";
                S014AgentVoice.Instance.Say("p_follow", S014AgentVoice.NameKey(d.id));
            }
        }

        private bool IsNearLobby()
        {
            if (playerRig == null) return true;
            Vector3 s = LobbyPos();
            Vector3 p = playerRig.position;
            float dh = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z));
            return dh < 9f && Mathf.Abs(p.y - s.y) < 1.2f;
        }

        // ---------------------------------------------------------------- lift

        private bool liftPanelPlaced;

        private void Update()
        {
            // The "I've entered the lift" toggle appears in front of the visitor
            // once they are close to the lift doors, and stays where it was placed.
            if (pendingLiftDest != null && liftConfirmPanel != null && playerRig != null && !riding)
            {
                Vector3 p = playerRig.position;
                float dh = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(0f, shaftZ));
                // stepping inside the lift starts the ride by itself; the toggle stays as a manual fallback
                if (dh < 1.4f && Mathf.Abs(p.y - Mathf.Round(p.y / 4f) * 4f) < 0.6f)
                {
                    Debug.Log("[S014] stepped into the lift - starting the ride");
                    EnterLiftConfirmed();
                    return;
                }
                if (dh < 7f)
                {
                    if (!liftPanelPlaced)
                    {
                        PlaceInFrontOfPlayer(liftConfirmPanel.transform, 1.7f, 1.45f);
                        liftPanelPlaced = true;
                    }
                    if (!liftConfirmPanel.activeSelf) liftConfirmPanel.SetActive(true);
                }
                else if (liftConfirmPanel.activeSelf) { liftConfirmPanel.SetActive(false); liftPanelPlaced = false; }
            }
        }

        public void EnterLiftConfirmed()
        {
            if (pendingLiftDest == null || playerRig == null || riding) return;
            if (liftConfirmPanel != null) liftConfirmPanel.SetActive(false);
            var d = pendingLiftDest; pendingLiftDest = null;
            StartCoroutine(LiftRide(d));
        }

        /// <summary>Animated lift ride: rise/descend to the destination floor,
        /// step out beside the lift, then start the floor's guided path.</summary>
        private IEnumerator LiftRide(CampusDestinationAnchor d)
        {
            riding = true;
            var loco = playerRig.GetComponent<S014CampusFreeLocomotion>();
            var cc = playerRig.GetComponent<CharacterController>();
            if (loco != null) loco.enabled = false;
            if (cc != null) cc.enabled = false;

            float z = d.liftExit.z < 50f ? 35f : 70f;
            Vector3 p = playerRig.position;
            Debug.Log("[S014] lift ride start " + p.y.ToString("F1") + " -> " + d.liftExit.y.ToString("F1") + " (" + d.id + ")");
            playerRig.position = new Vector3(0f, p.y, z);
            if (selectedText != null) selectedText.text = "Taking the lift to\nfloor " + d.floorLabel + "...";
            var voice = S014AgentVoice.Instance;
            voice.Say("p_lift_going", S014AgentVoice.FloorKey(d.floorLabel));
            voice.LiftHum(true);
            yield return new WaitForSeconds(0.8f);

            float y0 = playerRig.position.y, y1 = d.liftExit.y + 0.1f;
            float dur = Mathf.Clamp(Mathf.Abs(y1 - y0) / 3f, 2.5f, 8f), t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                playerRig.position = new Vector3(0f, Mathf.Lerp(y0, y1, k), z);
                yield return null;
            }

            playerRig.position = d.liftExit + Vector3.up * 0.1f;
            if (cc != null) cc.enabled = true;
            if (loco != null) loco.enabled = true;
            riding = false;
            voice.LiftHum(false);
            voice.LiftChime();
            Debug.Log("[S014] lift ride done, now at y=" + playerRig.position.y.ToString("F1"));
            if (selectedText != null) selectedText.text = Wrap(d.displayName, 17) + "\nFloor " + d.floorLabel + "\nFollow the line";
            guide.BeginExplicit(d, d.liftPath);
            voice.Say("p_lift_arrived", S014AgentVoice.FloorKey(d.floorLabel), "p_follow2", S014AgentVoice.NameKey(d.id));
        }

        // --------------------------------------------------- summon / reset

        /// <summary>Bring the kiosk to the visitor (used after reaching a
        /// destination, or on demand) so another destination can be chosen.</summary>
        public void SummonNearPlayer()
        {
            if (playerRig == null) return;
            var cam = Camera.main;
            Vector3 head = cam != null ? cam.transform.position : playerRig.position + Vector3.up * 1.6f;
            Vector3 fwd = cam != null ? cam.transform.forward : playerRig.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = playerRig.forward;
            fwd.Normalize();

            Vector3 pos = head + fwd * 2.0f;
            float gy = playerRig.position.y;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(pos.x, playerRig.position.y + 1.5f, pos.z), Vector3.down, out hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<S014CampusKiosk>() == null)
                gy = hit.point.y;
            else gy = playerRig.position.y - 0.1f;

            transform.SetPositionAndRotation(new Vector3(pos.x, gy, pos.z), Quaternion.LookRotation(fwd));
            ShowFloor(1);
            if (selectedText != null && SelectedId == null) selectedText.text = "Pick a destination";
        }

        /// <summary>Visitor pressed the summon button: open the kiosk right here.</summary>
        public void SummonByVisitor()
        {
            SummonNearPlayer();
            S014AgentVoice.Instance.Say("p_kiosk_here");
        }

        public void ReturnHome()
        {
            transform.SetPositionAndRotation(homePos, homeRot);
        }

        /// <summary>RESET: stop everything and go back to the main gate beside the kiosk.</summary>
        public void ResetSession()
        {
            StopAllCoroutines();
            scanning = false; riding = false;
            pendingLiftDest = null; liftPanelPlaced = false;

            var loco = playerRig != null ? playerRig.GetComponent<S014CampusFreeLocomotion>() : null;
            var cc = playerRig != null ? playerRig.GetComponent<CharacterController>() : null;
            if (guide != null) guide.Stop();
            S014AgentVoice.Instance.Silence();
            S014AgentVoice.Instance.LiftHum(false);
            TeleportToLobby();
            if (loco != null) loco.enabled = true;
            if (cc != null) cc.enabled = true;

            ReturnHome();
            SelectedId = null;
            UseLift = false;
            SetModeHighlight();
            if (qrPanel != null) qrPanel.SetActive(false);
            if (liftConfirmPanel != null) liftConfirmPanel.SetActive(false);
            ShowFloor(1);
            if (selectedText != null) selectedText.text = "Pick a destination";
            if (OnReset != null) OnReset();
        }

        private Vector3 LobbyPos()
        {
            return directory != null && directory.lobbySpawn != null ? directory.lobbySpawn.position : new Vector3(0f, 0.1f, 5f);
        }

        /// <summary>Place the rig at the main-gate lobby, with the visitor's head
        /// facing the kiosk (rig yaw is corrected for wherever the head points).</summary>
        public void TeleportToLobby()
        {
            if (playerRig == null) return;
            var cc = playerRig.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            Vector3 s = LobbyPos();
            float desiredYaw = directory != null && directory.lobbySpawn != null ? directory.lobbySpawn.eulerAngles.y : 0f;
            playerRig.position = s;
            var cam = Camera.main;
            if (cam != null)
            {
                float headYaw = cam.transform.eulerAngles.y;
                playerRig.RotateAround(cam.transform.position, Vector3.up, desiredYaw - headYaw);
                Vector3 off = cam.transform.position - playerRig.position; off.y = 0f;
                playerRig.position = s - off;
            }
            else playerRig.rotation = Quaternion.Euler(0f, desiredYaw, 0f);

            if (cc != null) cc.enabled = true;
        }

        private void PlaceInFrontOfPlayer(Transform t, float dist, float height)
        {
            var cam = Camera.main;
            Vector3 head = cam != null ? cam.transform.position : playerRig.position + Vector3.up * 1.6f;
            Vector3 fwd = cam != null ? cam.transform.forward : playerRig.forward;
            fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            t.position = new Vector3(head.x + fwd.x * dist, playerRig.position.y + height, head.z + fwd.z * dist);
            t.rotation = Quaternion.LookRotation(fwd);   // button faces the visitor (its front is -z)
        }

        // ------------------------------------------------------------ QR image

        private static Texture2D MakeQr(string payload)
        {
            const int n = 25;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var rnd = new System.Random(StableHash(payload));
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, rnd.Next(0, 2) == 0 ? Color.black : Color.white);
            DrawFinder(t, 0, 0); DrawFinder(t, 0, n - 7); DrawFinder(t, n - 7, n - 7);
            t.Apply();
            return t;
        }

        // FNV-1a: same QR on every device/run for the same destination.
        private static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                if (s != null) for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return (int)h;
            }
        }

        private static void DrawFinder(Texture2D t, int ox, int oy)
        {
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 7; x++)
                {
                    bool on = x == 0 || x == 6 || y == 0 || y == 6 || (x >= 2 && x <= 4 && y >= 2 && y <= 4);
                    t.SetPixel(ox + x, oy + y, on ? Color.black : Color.white);
                }
        }
    }
}
