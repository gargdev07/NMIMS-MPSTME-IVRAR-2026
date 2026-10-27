using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace S014.CampusVR.EditorTools
{
    /// <summary>
    /// End-to-end self test, run in Play mode (S014/Self test). For every destination it plays the real visitor
    /// flow through the kiosk - pick destination, QR shows, scan, session loads, guide line + arrow appear, then a
    /// simulated visitor walks the DISPLAYED line with the real CharacterController (lift mode also rides the lift)
    /// until the arrival event fires and the kiosk comes back. Results go to Logs/S014SelfTest.txt.
    /// </summary>
    public class S014FlowTester : MonoBehaviour
    {
        public static string ReportPath = "Logs/S014SelfTest.txt";
        public bool doStairs = true, doLift = true;
        public float timeScale = 10f;
        public string onlyId;
        public bool fromAnywhere;   // start every run from a random walkable spot on any floor instead of the gate
        List<Vector3> starts; System.Random rnd = new System.Random(12345);

        S014CampusKiosk k; S014CampusDirectory dir; S014CampusWayfindingGuide g; Transform rig; CharacterController cc; S014CampusFreeLocomotion loco;
        bool arrived; float vy;
        readonly StringBuilder log = new StringBuilder();
        int ok, fail;

        void OnArrived(CampusDestinationAnchor a) { arrived = true; }

        IEnumerator Start()
        {
            k = FindAnyObjectByType<S014CampusKiosk>(); dir = FindAnyObjectByType<S014CampusDirectory>(); g = FindAnyObjectByType<S014CampusWayfindingGuide>();
            rig = k.playerRig; cc = rig.GetComponent<CharacterController>(); loco = rig.GetComponent<S014CampusFreeLocomotion>();
            g.OnArrived += OnArrived;
            Time.timeScale = timeScale; Time.maximumDeltaTime = 0.25f;
            yield return new WaitForSecondsRealtime(2f);
            var list = new List<CampusDestinationAnchor>(dir.destinations);
            if (!string.IsNullOrEmpty(onlyId)) list = list.FindAll(d => d.id == onlyId || (onlyId.EndsWith("*") && d.id.StartsWith(onlyId.TrimEnd('*'))));   // "fg-*" selects a whole prefix
            if (fromAnywhere) starts = S014RouteTool.ReachableSamples(2.0f);
            int idx = 0;
            foreach (var d in list)
            {
                idx++;
                if (doStairs) yield return Run(d, false);
                if (doLift) yield return Run(d, true);
                Flush("progress " + idx + "/" + list.Count);
            }
            Time.timeScale = 1f;
            log.Insert(0, "DONE  ok=" + ok + "  fail=" + fail + "  (destinations=" + list.Count + ", modes: " + (doStairs ? "stairs " : "") + (doLift ? "lift" : "") + ")\n");
            Flush(null);
            g.OnArrived -= OnArrived; loco.enabled = true; k.ResetSession(); Destroy(gameObject);
        }

        void Flush(string head)
        {
            string text = (head != null ? head + "\n" : "") + log;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, text);
        }

        IEnumerator Wait(float s) { float t = 0; while (t < s) { loco.enabled = false; t += Time.deltaTime; yield return null; } }

        IEnumerator Run(CampusDestinationAnchor d, bool lift)
        {
            string tag = (lift ? "LIFT   " : "STAIRS ") + d.floorLabel.PadRight(2) + " " + d.id;
            string err = null; float t0 = Time.time;
            arrived = false; bool expectLift = lift && Mathf.Abs(d.liftExit.y) > 1f;   // the gate is on the ground floor: a ground-floor room needs no lift
            k.ResetSession(); loco.enabled = false;
            yield return Wait(1.0f);
            if (fromAnywhere)
            {
                var sp = starts[rnd.Next(starts.Count)];
                cc.enabled = false; rig.position = sp + Vector3.up * 0.05f; cc.enabled = true; Physics.SyncTransforms();
                yield return Wait(0.5f);
                k.SummonNearPlayer();
                float fy = Mathf.Round((sp.y + 0.1f) / 4f) * 4f;
                expectLift = lift && Mathf.Abs(fy - d.liftExit.y) > 1f;
                tag += "  from " + sp.ToString("F0");
            }
            else if (Vector2.Distance(new Vector2(rig.position.x, rig.position.z), new Vector2(0f, 5f)) > 2.5f || Mathf.Abs(rig.position.y - 0.1f) > 0.6f) err = "reset did not return to the main gate: " + rig.position.ToString("F1");

            if (err == null)
            {
                k.Select(lift ? "mode:lift" : "mode:stairs"); k.Select(d.id);
                yield return Wait(0.3f);
                if (k.SelectedId != d.id) err = "kiosk did not select the destination";
                else if (k.qrPanel == null || !k.qrPanel.activeInHierarchy) err = "QR panel did not appear after selecting";
            }
            if (err == null)
            {
                k.ConfirmScanAndStart();
                float w = 0; while (!g.IsActive && w < 10f) { w += Time.deltaTime; loco.enabled = false; yield return null; }
                if (!g.IsActive) err = "session did not start after QR scan";
                else if (g.Target == null || g.Target.id != d.id) err = "session loaded the wrong destination";
                else if (k.qrPanel != null && k.qrPanel.activeInHierarchy) err = "QR panel stayed visible after the scan";
                else if (expectLift && !g.GuidingToLift) err = "lift mode did not guide to the lift";
                else if (!expectLift && g.GuidingToLift) err = "stairs mode guided to the lift";
            }
            if (err == null) err = CheckGuideVisuals();

            if (err == null && expectLift)
            {
                // walking into the lift now starts the ride by itself (the kiosk toggle is only a fallback)
                yield return Follow(x => { err = x; }, true);
                if (err == null)
                {
                    yield return Wait(0.3f);
                    {
                        float w = 0; while ((g.GuidingToLift || !g.IsActive || Mathf.Abs(rig.position.y - (d.liftExit.y + 0.1f)) > 0.8f) && w < 15f) { w += Time.deltaTime; yield return null; }
                        if (w >= 15f && !g.IsActive) err = "the lift did not start by itself when stepping inside";
                        else if (Mathf.Abs(rig.position.y - (d.liftExit.y + 0.1f)) > 0.8f) err = "lift ride did not end on the destination floor (y=" + rig.position.y.ToString("F1") + ")";
                        else if (!g.IsActive) err = "no guide after the lift ride";
                        else err = CheckGuideVisuals();
                    }
                }
            }

            if (err == null) yield return Follow(x => { err = x; }, false);

            if (err == null)
            {
                float w = 0; while (!arrived && w < 3f) { w += Time.deltaTime; yield return null; }
                if (!arrived) err = "walked the whole line but arrival never triggered";
            }
            if (err == null)
            {
                float w = 0; float kd = 99f; while (w < 8f) { w += Time.deltaTime; kd = Vector3.Distance(rig.position, k.transform.position); if (kd < 3.5f && k.gameObject.activeInHierarchy && !g.IsActive) break; loco.enabled = false; yield return null; }
                if (kd >= 3.5f) err = "kiosk did not come back after arrival (distance " + kd.ToString("F1") + " m)";
                else if (Mathf.Abs(k.transform.position.y - rig.position.y) > 1.0f) err = "kiosk reappeared on the wrong floor";
            }
            if (err == null)
            {
                float dd = Vector2.Distance(new Vector2(rig.position.x, rig.position.z), new Vector2(d.anchor.position.x, d.anchor.position.z));
                if (dd > 3.0f || Mathf.Abs(rig.position.y - d.anchor.position.y) > 1.5f) err = "arrived but not at the room: " + dd.ToString("F1") + " m away";
            }

            if (err == null) { ok++; log.AppendLine("OK    " + tag + "   (" + (Time.time - t0).ToString("F0") + " game-s)"); }
            else { fail++; log.AppendLine("FAIL  " + tag + "   " + err + "   player=" + rig.position.ToString("F1")); }
        }

        string CheckGuideVisuals()
        {
            if (g.line == null || g.line.positionCount < 2) return "guide line missing (points=" + (g.line != null ? g.line.positionCount : 0) + ")";
            if (!g.line.enabled || !g.line.gameObject.activeInHierarchy) return "guide line not visible";
            if (g.arrow == null || !g.arrow.gameObject.activeInHierarchy) return "guide arrow not visible";
            var pts = new Vector3[g.line.positionCount]; g.line.GetPositions(pts);
            if (!g.line.useWorldSpace) for (int i = 0; i < pts.Length; i++) pts[i] = g.line.transform.TransformPoint(pts[i]);
            float best = 1e9f; foreach (var p in pts) best = Mathf.Min(best, Vector3.Distance(p, rig.position));
            if (best > 4f) return "guide line does not start near the visitor (" + best.ToString("F1") + " m)";
            return null;
        }

        // walks the displayed line with the real CharacterController
        IEnumerator Follow(System.Action<string> result, bool toLift)
        {
            float elapsed = 0, lastProgressT = 0, bestRem = 1e9f; vy = 0;
            string e = null; int frames = 0; Vector3[] snap = null; int cur = 1; Vector3 lastCheckPos = rig.position;
            while (true)
            {
                loco.enabled = false;
                if (!toLift && arrived) break;
                if (!g.IsActive) break;
                if (toLift && (!g.GuidingToLift || k.Busy)) break;
                if (toLift)
                {
                    float dh = Vector2.Distance(new Vector2(rig.position.x, rig.position.z), new Vector2(0f, g.line.GetPosition(g.line.positionCount - 1).z));
                    if (dh < 0.9f) break;
                }
                // The displayed line is re-drawn from the visitor every frame, so take the line as first shown and
                // follow its corners in order (cursor only moves forward).
                if (snap == null)
                {
                    snap = new Vector3[g.line.positionCount]; g.line.GetPositions(snap);
                    if (!g.line.useWorldSpace) for (int i = 0; i < snap.Length; i++) snap[i] = g.line.transform.TransformPoint(snap[i]);
                    cur = Mathf.Min(1, snap.Length - 1);
                }
                while (cur < snap.Length - 1 && Vector2.Distance(new Vector2(snap[cur].x, snap[cur].z), new Vector2(rig.position.x, rig.position.z)) < 0.6f && Mathf.Abs(snap[cur].y - rig.position.y) < 1.2f) { cur++; lastProgressT = elapsed; }
                Vector3 to = snap[cur] - rig.position; to.y = 0;
                float rem = g.RemainingDistance;
                // real progress = the visitor actually moved on; a wall/rail stops them dead
                if (Vector3.Distance(rig.position, lastCheckPos) > 0.6f) { lastCheckPos = rig.position; lastProgressT = elapsed; }
                if (elapsed - lastProgressT > 8f)
                {
                    e = "stuck while following the line at " + rig.position.ToString("F1") + " (" + rem.ToString("F1") + " m left) heading to corner " + cur + "/" + (snap.Length - 1) + " " + snap[cur].ToString("F1");
                    break;
                }
                if (elapsed > 240f) { e = "timed out following the line"; break; }
                if (rig.position.y < -9f || (bestRem < 1e8f && false)) { e = "fell off"; break; }
                float dt = Time.deltaTime; int sub = Mathf.Max(1, Mathf.CeilToInt(dt / 0.02f)); float h = dt / sub;
                for (int s = 0; s < sub; s++)
                {
                    if (cc.isGrounded && vy < 0) vy = -2f; vy -= 9.81f * h;
                    Vector3 mv = to.sqrMagnitude > 0.0004f ? to.normalized * 2.6f : Vector3.zero;
                    cc.Move((mv + Vector3.up * vy) * h);
                }
                elapsed += dt; frames++;
                yield return null;
            }
            result(e);
        }
    }

    /// <summary>Drops a CharacterController on every reachable floor point and walks 1 m in four directions;
    /// any spot where it ends more than 1.1 m lower is an unprotected edge (Logs/S014FallTest.txt).</summary>
    public class S014FallBot : MonoBehaviour
    {
        IEnumerator Start()
        {
            var k = FindAnyObjectByType<S014CampusKiosk>(); var rig = k.playerRig; var cc = rig.GetComponent<CharacterController>();
            rig.GetComponent<S014CampusFreeLocomotion>().enabled = false;
            yield return new WaitForSecondsRealtime(2f);
            var samples = S014RouteTool.ReachableSamples(1.0f);
            var groups = new Dictionary<string, int>(); var first = new Dictionary<string, string>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }; string[] dn = { "+x", "-x", "+z", "-z" };
            int n = 0, falls = 0;
            foreach (var s in samples)
            {
                for (int di = 0; di < 4; di++)
                {
                    cc.enabled = false; rig.position = s + Vector3.up * 0.05f; cc.enabled = true; Physics.SyncTransforms();
                    float vy = 0;
                    for (int i = 0; i < 3; i++) { vy -= 9.81f * 0.02f; cc.Move(Vector3.up * vy * 0.02f); if (cc.isGrounded) vy = -1f; }
                    float y0 = rig.position.y;
                    for (int i = 0; i < 10; i++) { vy -= 9.81f * 0.02f; if (cc.isGrounded && vy < 0) vy = -2f; cc.Move(dirs[di] * 0.1f + Vector3.up * vy * 0.02f); }
                    for (int i = 0; i < 30; i++) { vy -= 9.81f * 0.02f; if (cc.isGrounded && vy < 0) vy = -2f; cc.Move(Vector3.up * vy * 0.02f); }
                    float drop = y0 - rig.position.y;
                    if (drop > 1.1f)
                    {
                        falls++;
                        string key = "y=" + s.y.ToString("F0") + " x[" + (Mathf.Floor(s.x / 2f) * 2f) + "," + (Mathf.Floor(s.x / 2f) * 2f + 2f) + "] z[" + (Mathf.Floor(s.z / 2f) * 2f) + "," + (Mathf.Floor(s.z / 2f) * 2f + 2f) + "] " + dn[di];
                        groups[key] = (groups.ContainsKey(key) ? groups[key] : 0) + 1;
                        if (!first.ContainsKey(key)) first[key] = "start " + s.ToString("F1") + " -> y=" + rig.position.y.ToString("F1");
                    }
                }
                if (++n % 1500 == 0) yield return null;
            }
            var sb = new StringBuilder("DONE samples=" + samples.Count + " trajectories=" + samples.Count * 4 + " falls=" + falls + " blocks=" + groups.Count + "\n");
            var keys = new List<string>(groups.Keys); keys.Sort();
            foreach (var key in keys) sb.AppendLine("  " + key + "  x" + groups[key] + "   (" + first[key] + ")");
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/S014FallTest.txt", sb.ToString());
            k.ResetSession(); rig.GetComponent<S014CampusFreeLocomotion>().enabled = true; Destroy(gameObject);
        }
    }

    public static class S014SelfTest
    {
        [MenuItem("S014/Self test/Run fall test (Play mode)")]
        public static void RunFall()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
            new GameObject("S014FallBot").AddComponent<S014FallBot>();
        }

        [MenuItem("S014/Self test/Run full flow (Play mode, all destinations)")]
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
            new GameObject("S014FlowTester").AddComponent<S014FlowTester>();
        }

        public static void Start(bool stairs, bool lift, string onlyId, float timeScale)
        {
            var t = new GameObject("S014FlowTester").AddComponent<S014FlowTester>();
            t.doStairs = stairs; t.doLift = lift; t.onlyId = onlyId; t.timeScale = timeScale;
        }

        /// <summary>Same flow, but every run starts from a random walkable spot (any floor, stairs included) with the kiosk summoned there.</summary>
        public static void StartAnywhere(bool stairs, bool lift, string onlyId, float timeScale)
        {
            var t = new GameObject("S014FlowTester").AddComponent<S014FlowTester>();
            t.doStairs = stairs; t.doLift = lift; t.onlyId = onlyId; t.timeScale = timeScale; t.fromAnywhere = true;
        }
    }
}
