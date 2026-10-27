using System;
using System.Collections.Generic;
using UnityEngine;

namespace S014.CampusVR
{
    /// <summary>
    /// Draws a floor route line and a floating chevron from the walker's current
    /// position to the selected gate, reports remaining distance, and fires
    /// OnArrived when the gate is reached. Works with free locomotion: the path
    /// is the kiosk-provided lobby-to-gate polyline, re-anchored to the walker
    /// each frame so the guidance keeps leading forward.
    /// </summary>
    public class S014CampusWayfindingGuide : MonoBehaviour
    {
        public Transform player;
        public LineRenderer line;
        public Transform arrow;
        [Min(0.5f)] public float arrivalRadius = 1.6f;

        public bool IsActive { get; private set; }
        public CampusDestinationAnchor Target { get; private set; }
        public float RemainingDistance { get; private set; }
        public Action<CampusDestinationAnchor> OnArrived;

        private readonly List<Vector3> path = new List<Vector3>();

        // True while we are only leading the walker TO the lift on the current
        // floor (not to the final gate), so arrival must not fire the banner.
        public bool GuidingToLift { get; private set; }

        public void Begin(CampusDestinationAnchor dest)
        {
            GuidingToLift = false;
            Target = dest;
            path.Clear();
            if (dest != null)
            {
                if (dest.pathFromLobby != null) path.AddRange(dest.pathFromLobby);
                if (dest.anchor != null) path.Add(dest.anchor.position);
            }
            IsActive = dest != null && path.Count > 0;
            if (line != null) line.enabled = IsActive;
            if (arrow != null) arrow.gameObject.SetActive(IsActive);
        }

        /// <summary>Lead the walker to the lift doors on the current floor. The
        /// destination is kept for the HUD, but arrival is NOT auto-detected: the
        /// walker confirms "I've entered the lift" on the kiosk toggle, which then
        /// rides up and hands off to BeginExplicit for the destination floor.</summary>
        public void GuideToLift(CampusDestinationAnchor dest, Vector3 liftDoor)
        {
            GuidingToLift = true;
            Target = dest;
            path.Clear();
            path.Add(liftDoor);
            IsActive = true;
            if (line != null) line.enabled = true;
            if (arrow != null) arrow.gameObject.SetActive(true);
        }

        /// <summary>Same, but along a planned walkable route (from wherever the walker stands) ending at the lift doors.</summary>
        public void GuideToLift(CampusDestinationAnchor dest, List<Vector3> routeToLift)
        {
            GuidingToLift = true;
            Target = dest;
            path.Clear();
            if (routeToLift != null) path.AddRange(routeToLift);
            IsActive = path.Count > 0;
            if (line != null) line.enabled = IsActive;
            if (arrow != null) arrow.gameObject.SetActive(IsActive);
        }

        /// <summary>Begin with an explicit waypoint list (used for the lift path,
        /// which starts from the lift exit rather than the lobby).</summary>
        public void BeginExplicit(CampusDestinationAnchor dest, List<Vector3> explicitPath)
        {
            GuidingToLift = false;
            Target = dest;
            path.Clear();
            if (explicitPath != null) path.AddRange(explicitPath);
            if (dest != null && dest.anchor != null) path.Add(dest.anchor.position);
            IsActive = dest != null && path.Count > 0;
            if (line != null) line.enabled = IsActive;
            if (arrow != null) arrow.gameObject.SetActive(IsActive);
        }

        public void Stop()
        {
            GuidingToLift = false;
            IsActive = false;
            if (line != null) line.enabled = false;
            if (arrow != null) arrow.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsActive || player == null || path.Count == 0) return;

            Vector3 p = player.position;
            // Re-anchor on the nearest point of the route itself (not just the nearest corner), so
            // after a wrong turn the line leads straight back onto the route and then forward.
            int start; Vector3 foot;
            NearestOnRoute(p, out start, out foot);

            var pts = new List<Vector3> { p };
            if (Vector2.Distance(new Vector2(foot.x, foot.z), new Vector2(p.x, p.z)) > 0.35f || Mathf.Abs(foot.y - p.y) > 0.5f) pts.Add(foot);
            for (int i = start; i < path.Count; i++) pts.Add(path[i]);

            float d = 0f;
            for (int i = 0; i < pts.Count - 1; i++) d += Vector3.Distance(pts[i], pts[i + 1]);
            RemainingDistance = d;

            if (line != null)
            {
                line.positionCount = pts.Count;
                for (int i = 0; i < pts.Count; i++) line.SetPosition(i, pts[i] + Vector3.up * 0.06f);
            }

            if (arrow != null && pts.Count > 1)
            {
                // the arrow floats well ahead of the visitor along the line, so it is easy to read
                Vector3 look = Vector3.zero, apos = p;
                float ahead = 2.4f;
                for (int i = 0; i < pts.Count - 1 && ahead > 0f; i++)
                {
                    Vector3 seg = pts[i + 1] - pts[i]; seg.y = 0f; float len = seg.magnitude;
                    if (len < 0.001f) continue;
                    look = seg / len;
                    float use = Mathf.Min(ahead, len);
                    apos = new Vector3(pts[i].x, pts[i].y, pts[i].z) + new Vector3(look.x, 0f, look.z) * use;
                    apos.y = Mathf.Lerp(pts[i].y, pts[i + 1].y, use / len);
                    ahead -= use;
                }
                arrow.position = apos + Vector3.up * 1.05f;
                if (look.sqrMagnitude > 0.01f) arrow.rotation = Quaternion.LookRotation(look.normalized);
            }

            if (GuidingToLift) return;   // the kiosk toggle ends this leg, not proximity

            Vector3 fin = path[path.Count - 1];
            if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(fin.x, 0f, fin.z)) < arrivalRadius)
            {
                var reached = Target;
                Stop();
                OnArrived?.Invoke(reached);
            }
        }

        // nearest point on the route polyline; `next` is the index of the corner after it
        private void NearestOnRoute(Vector3 p, out int next, out Vector3 foot)
        {
            next = Mathf.Min(1, path.Count - 1); foot = path[0];
            if (path.Count == 1) { next = 0; return; }
            float best = float.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i], b = path[i + 1];
                Vector3 ab = b - a; ab.y = 0f; Vector3 ap = p - a; ap.y = 0f;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
                Vector3 q = Vector3.Lerp(a, b, t);
                float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(p.x, p.z)) + Mathf.Abs(q.y - p.y) * 1.5f;
                if (d < best) { best = d; next = i + 1; foot = q; }
            }
        }

        private int NearestIndex(Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                float dd = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(path[i].x, 0f, path[i].z));
                if (dd < bd) { bd = dd; best = i; }
            }
            return best;
        }
    }
}
