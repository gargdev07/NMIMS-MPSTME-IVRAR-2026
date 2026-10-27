using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace S014.CampusVR
{
    /// <summary>
    /// Orchestrates the walkthrough HUD and arrival banner, keeps the VR camera
    /// opaque (no passthrough), and wires the shortcuts:
    ///   Left X / K  = bring the kiosk to me      Left Y / R = RESET to the main gate
    ///   Space       = scan the selected QR (editor)
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class S014CampusDemoFlow : MonoBehaviour
    {
        public S014CampusWayfindingGuide guide;
        public S014CampusKiosk kiosk;
        public TextMesh hud;
        public GameObject arrivalBanner;
        public TextMesh arrivalText;

        private bool prevSummon, prevReset;

        private void Awake()
        {
            // Keep rendering when the editor loses focus (Meta XR Simulator runs
            // in its own window); a no-op on the headset.
            Application.runInBackground = true;
        }

        private void OnEnable()
        {
            if (guide != null) guide.OnArrived += OnArrived;
            if (kiosk != null) kiosk.OnReset += HideBanner;
        }

        private void OnDisable()
        {
            if (guide != null) guide.OnArrived -= OnArrived;
            if (kiosk != null) kiosk.OnReset -= HideBanner;
        }

        private void Start()
        {
            HideBanner();
            StartCoroutine(KeepVrOpaque());
            StartCoroutine(EditorEyeHeight());
            StartCoroutine(WelcomeWhenWorn());
        }

        // The welcome must be heard by the visitor: wait until the headset is actually on
        // (or a few seconds in the editor), then let the guide introduce itself.
        private IEnumerator WelcomeWhenWorn()
        {
            float t = 0f;
            while (t < 60f)
            {
                bool worn = Application.isEditor || OVRManager.isHmdPresent && OVRPlugin.userPresent;
                if (worn && t > 2.5f) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (guide != null && guide.IsActive) yield break;
            var clip = welcomeClip;
            if (clip == null)
            {
                var agent = GameObject.Find("CampusVR_Agent");
                var a = agent != null ? agent.GetComponent<AudioSource>() : null;
                if (a != null) { clip = a.clip; a.playOnAwake = false; a.Stop(); }
            }
            if (clip != null) S014AgentVoice.Instance.Say(clip);
            else S014AgentVoice.Instance.Say("p_welcome_short");
        }

        public AudioClip welcomeClip;

        // The eyes must end up at standing height. A headset tracks from the floor, so nothing
        // is added there. Flat editor Play (no tracking) and runtimes that only provide an
        // eye-level ("Device") origin, such as the Meta XR Simulator, would otherwise put the
        // eyes on the floor, so the rig is raised by a standing eye height instead.
        private IEnumerator EditorEyeHeight()
        {
            yield return new WaitForSeconds(1.5f);
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig == null || rig.trackingSpace == null) yield break;

            bool needsHeight;
            if (!UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).isValid)
                needsHeight = Application.isEditor;
            else
            {
                var subsystems = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
                SubsystemManager.GetSubsystems(subsystems);
                foreach (var s in subsystems) s.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Floor);
                yield return new WaitForSeconds(0.5f);
                needsHeight = false;
                foreach (var s in subsystems)
                    if ((s.GetTrackingOriginMode() & UnityEngine.XR.TrackingOriginModeFlags.Floor) == 0) needsHeight = true;
                UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head)
                    .TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out var head);
                // A tracked head already above 1 m means the runtime supplied real height.
                if (head.y > 1f) needsHeight = false;
            }
            if (needsHeight) rig.trackingSpace.localPosition += Vector3.up * 1.65f;
        }

        // This is a fully virtual VR campus: passthrough must never show.
        // OVRManager can re-assert it for a moment after start, so re-apply briefly.
        private IEnumerator KeepVrOpaque()
        {
            for (int i = 0; i < 4; i++)
            {
                ConfigureVrRendering();
                yield return new WaitForSeconds(0.75f);
            }
        }

        private void ConfigureVrRendering()
        {
            var mgr = FindAnyObjectByType<OVRManager>();
            if (mgr != null) mgr.isInsightPassthroughEnabled = false;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            Camera cam = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.Skybox; cam.cullingMask = ~0; cam.enabled = true; }
        }

        private void Update()
        {
            if (hud != null)
            {
                if (guide != null && guide.IsActive && guide.Target != null)
                {
                    if (guide.GuidingToLift)
                        hud.text = "Go to the LIFT   " + Mathf.RoundToInt(guide.RemainingDistance) + " m\n(then " + guide.Target.displayName + ", floor " + guide.Target.floorLabel + ")";
                    else
                        hud.text = guide.Target.displayName + "   " + Mathf.RoundToInt(guide.RemainingDistance) + " m   (floor " + guide.Target.floorLabel + ")";
                }
                else
                    hud.text = "Use the kiosk: pick a room, scan the QR";
            }

            var kb = Keyboard.current;
            if (kiosk != null)
            {
                if (kb != null)
                {
                    if (kb.spaceKey.wasPressedThisFrame) kiosk.ConfirmScanAndStart();
                    if (kb.rKey.wasPressedThisFrame) kiosk.ResetSession();
                    if (kb.kKey.wasPressedThisFrame) kiosk.SummonByVisitor();
                }
                bool summon = S014VRInput.LeftPrimary();
                bool reset = S014VRInput.LeftSecondary();
                if (summon && !prevSummon) kiosk.SummonByVisitor();
                if (reset && !prevReset) kiosk.ResetSession();
                prevSummon = summon; prevReset = reset;
            }

            if (guide != null && guide.IsActive && arrivalBanner != null && arrivalBanner.activeSelf)
                arrivalBanner.SetActive(false);
        }

        private void OnArrived(CampusDestinationAnchor d)
        {
            if (arrivalBanner != null)
            {
                arrivalBanner.SetActive(true);
                if (arrivalText != null)
                    arrivalText.text = "You have arrived\n" + (d != null ? d.displayName : "") + "\nThe kiosk is beside you.\nPress X to open it anywhere.";
            }
            if (d != null) S014AgentVoice.Instance.Say("p_arrived", S014AgentVoice.NameKey(d.id), "p_new_dest");
            StartCoroutine(SummonKioskSoon());
            StartCoroutine(HideBannerLater(8f));
        }

        private IEnumerator HideBannerLater(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            HideBanner();
        }

        private IEnumerator SummonKioskSoon()
        {
            yield return new WaitForSeconds(1.5f);
            if (kiosk != null && guide != null && !guide.IsActive) kiosk.SummonNearPlayer();
        }

        private void HideBanner()
        {
            if (arrivalBanner != null) arrivalBanner.SetActive(false);
        }
    }
}
