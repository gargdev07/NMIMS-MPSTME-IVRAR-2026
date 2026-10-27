using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace S014.CampusVR.EditorTools
{
    /// <summary>
    /// Builds a walkable-surface grid from the scene's real colliders (using the
    /// player's CharacterController size and step/slope limits) and computes every
    /// destination's stairs route (from the main-gate lobby) and lift route (from
    /// the south lifts on the destination floor) with Dijkstra, so every route is
    /// guaranteed to be walkable. Re-run it after changing campus geometry.
    /// </summary>
    public static class S014RouteTool
    {
        const float C = 0.5f, X0 = -26f, Z0 = 0f;
        const int NX = 104, NZ = 154;
        const float MAXUP = 0.30f, MAXDOWN = 1.0f;   // MAXUP = the CharacterController's real stepOffset

        static float R, H, STEP;
        static float[][] ys;
        static bool[][] wide;
        static int[] start;
        static int N;
        static float[] dist;
        static int[] parent;

        [MenuItem("S014/Routes/Rebuild walkable routes (apply)")]
        public static void MenuRebuild() { Debug.Log(Rebuild(true)); }

        [MenuItem("S014/Routes/Check walkability (report only)")]
        public static void MenuReport() { Debug.Log(Rebuild(false)); }

        static int Cell(int ix, int iz) { return ix * NZ + iz; }

        static int CellOf(int node)
        {
            int lo = 0, hi = NX * NZ - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (start[mid] <= node) lo = mid; else hi = mid - 1; }
            return lo;
        }

        static Vector3 Pos(int node)
        {
            int c = CellOf(node); int ix = c / NZ, iz = c % NZ;
            return new Vector3(X0 + (ix + 0.5f) * C, ys[c][node - start[c]], Z0 + (iz + 0.5f) * C);
        }

        static void BuildGrid()
        {
            ys = new float[NX * NZ][]; wide = new bool[NX * NZ][]; start = new int[NX * NZ + 1];
            var tmp = new List<float>(); var tw = new List<bool>(); int n = 0;
            for (int ix = 0; ix < NX; ix++)
                for (int iz = 0; iz < NZ; iz++)
                {
                    int c = Cell(ix, iz); start[c] = n;
                    // sampled a hair off the cell centre so box edges that sit exactly on a cell centre
                    // (stair flights, slabs) are not counted as floor
                    float x = X0 + (ix + 0.5f) * C + 0.013f, z = Z0 + (iz + 0.5f) * C + 0.013f;
                    var hits = Physics.RaycastAll(new Vector3(x, 48f, z), Vector3.down, 70f, ~0, QueryTriggerInteraction.Ignore);
                    var cand = new List<float>();
                    foreach (var h in hits) if (h.normal.y >= 0.7f) cand.Add(h.point.y);
                    cand.Sort(); tmp.Clear(); tw.Clear(); float last = -999f;
                    foreach (var y in cand)
                    {
                        if (y - last < 0.15f) continue;
                        var b = new Vector3(x, y + STEP + R + 0.02f, z); var t = new Vector3(x, y + H - R, z);
                        if (t.y < b.y) t.y = b.y;
                        if (Physics.CheckCapsule(b, t, R, ~0, QueryTriggerInteraction.Ignore)) continue;
                        tmp.Add(y); last = y;
                        // generous-clearance test: a fatter capsule, lifted so it does not clip the floor itself
                        float RW = R + 0.45f;
                        var bw = new Vector3(x, y + STEP + RW + 0.02f, z); var tw2 = new Vector3(x, Mathf.Max(bw.y, y + H - RW), z);
                        tw.Add(!Physics.CheckCapsule(bw, tw2, RW, ~0, QueryTriggerInteraction.Ignore));
                    }
                    ys[c] = tmp.ToArray(); wide[c] = tw.ToArray(); n += ys[c].Length;
                }
            start[NX * NZ] = n; N = n;
            MarkEdges();
        }

        // A cell is only a "wide" (preferred) cell if every neighbour has floor at a
        // step-able height. That keeps routes away from walls AND away from open
        // drops such as the side of a stair ramp or a stairwell opening.
        static void MarkEdges()
        {
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            for (int ix = 0; ix < NX; ix++)
                for (int iz = 0; iz < NZ; iz++)
                {
                    int c = Cell(ix, iz);
                    for (int k = 0; k < ys[c].Length; k++)
                    {
                        if (!wide[c][k]) continue;
                        float y = ys[c][k];
                        for (int e = 0; e < 8; e++) if (Near(ix + dx[e], iz + dz[e], y) < 0) { wide[c][k] = false; break; }
                    }
                }
        }

        // surface in cell (nx,nz) reachable from height y with an allowed step; -1 if none
        static int Near(int nx, int nz, float y)
        {
            if (nx < 0 || nz < 0 || nx >= NX || nz >= NZ) return -1;
            int c = Cell(nx, nz); var a = ys[c]; int best = -1; float bd = float.MaxValue;
            for (int k = 0; k < a.Length; k++)
            {
                float dy = a[k] - y;
                if (dy > MAXUP || dy < -MAXDOWN) continue;
                float d = Mathf.Abs(dy); if (d < bd) { bd = d; best = start[c] + k; }
            }
            return best;
        }

        static void Dijkstra(int src)
        {
            if (dist == null || dist.Length != N) { dist = new float[N]; parent = new int[N]; }
            for (int i = 0; i < N; i++) { dist[i] = float.MaxValue; parent[i] = -1; }
            var hp = new List<KeyValuePair<float, int>>(); dist[src] = 0; Push(hp, 0f, src);
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (hp.Count > 0)
            {
                var top = Pop(hp); float d = top.Key; int u = top.Value; if (d > dist[u]) continue;
                int c = CellOf(u); int ix = c / NZ, iz = c % NZ; float y = ys[c][u - start[c]]; bool uw = wide[c][u - start[c]];
                for (int e = 0; e < 8; e++)
                {
                    int nx = ix + dx[e], nz = iz + dz[e];
                    if (nx < 0 || nz < 0 || nx >= NX || nz >= NZ) continue;
                    if (e >= 4 && (Near(ix + dx[e], iz, y) < 0 || Near(ix, iz + dz[e], y) < 0)) continue;
                    // a cell can hold several surfaces (e.g. a floor slab with a ramp above it):
                    // connect to EVERY surface that is a legal step away, not only the closest one
                    int vcell = Cell(nx, nz); var arr = ys[vcell];
                    for (int k2 = 0; k2 < arr.Length; k2++)
                    {
                        float dyv = arr[k2] - y; if (dyv > MAXUP || dyv < -MAXDOWN) continue;
                        int v = start[vcell] + k2; bool vw = wide[vcell][k2];
                        float nd = d + (e < 4 ? C : C * 1.4142f) * ((uw && vw) ? 1f : 40f);   // strongly prefer corridor / ramp centres
                        if (nd < dist[v]) { dist[v] = nd; parent[v] = u; Push(hp, nd, v); }
                    }
                }
            }
        }

        static void Push(List<KeyValuePair<float, int>> h, float k, int v)
        {
            h.Add(new KeyValuePair<float, int>(k, v)); int i = h.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (h[p].Key <= h[i].Key) break; var t = h[p]; h[p] = h[i]; h[i] = t; i = p; }
        }

        static KeyValuePair<float, int> Pop(List<KeyValuePair<float, int>> h)
        {
            var r = h[0]; var last = h[h.Count - 1]; h.RemoveAt(h.Count - 1);
            if (h.Count > 0)
            {
                h[0] = last; int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, rr = l + 1, m = i;
                    if (l < h.Count && h[l].Key < h[m].Key) m = l;
                    if (rr < h.Count && h[rr].Key < h[m].Key) m = rr;
                    if (m == i) break;
                    var t = h[m]; h[m] = h[i]; h[i] = t; i = m;
                }
            }
            return r;
        }

        static int NearestNode(Vector3 p, float maxXZ, float maxY)
        {
            int ix0 = Mathf.FloorToInt((p.x - X0) / C), iz0 = Mathf.FloorToInt((p.z - Z0) / C);
            int best = -1; float bd = float.MaxValue; int rad = Mathf.CeilToInt(maxXZ / C);
            for (int ix = ix0 - rad; ix <= ix0 + rad; ix++)
                for (int iz = iz0 - rad; iz <= iz0 + rad; iz++)
                {
                    if (ix < 0 || iz < 0 || ix >= NX || iz >= NZ) continue;
                    int c = Cell(ix, iz); var a = ys[c];
                    for (int k = 0; k < a.Length; k++)
                    {
                        if (Mathf.Abs(a[k] - p.y) > maxY) continue;
                        float d = Vector2.Distance(new Vector2(X0 + (ix + 0.5f) * C, Z0 + (iz + 0.5f) * C), new Vector2(p.x, p.z));
                        if (d < bd && d <= maxXZ) { bd = d; best = start[c] + k; }
                    }
                }
            return best;
        }

        static List<Vector3> Extract(int goal)
        {
            var rev = new List<Vector3>(); int u = goal;
            while (u >= 0) { rev.Add(Pos(u)); u = parent[u]; }
            rev.Reverse(); return rev;
        }

        static List<Vector3> Simplify(List<Vector3> p, float tol)
        {
            if (p.Count < 3) return new List<Vector3>(p);
            var keep = new bool[p.Count]; keep[0] = keep[p.Count - 1] = true; DP(p, 0, p.Count - 1, tol, keep);
            var o = new List<Vector3>(); for (int i = 0; i < p.Count; i++) if (keep[i]) o.Add(p[i]); return o;
        }

        static void DP(List<Vector3> p, int a, int b, float tol, bool[] keep)
        {
            float md = 0; int mi = -1;
            for (int i = a + 1; i < b; i++) { float d = DistSeg(p[i], p[a], p[b]); if (d > md) { md = d; mi = i; } }
            if (md > tol && mi >= 0) { keep[mi] = true; DP(p, a, mi, tol, keep); DP(p, mi, b, tol, keep); }
        }

        static float DistSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        static void Prepare()
        {
            var loco = Object.FindAnyObjectByType<S014CampusFreeLocomotion>();
            var cc = loco.GetComponent<CharacterController>();
            R = cc.radius; H = cc.height; STEP = cc.stepOffset;
            Physics.SyncTransforms();
            BuildGrid();
        }

        /// <summary>Lists the walkable surfaces in the grid cell under (x,z): height and whether it is a preferred "wide" cell.</summary>
        public static string Probe(float x, float z, float yLo, float yHi)
        {
            if (ys == null) Prepare();
            int ix = Mathf.FloorToInt((x - X0) / C), iz = Mathf.FloorToInt((z - Z0) / C); int c = Cell(ix, iz);
            var sb = new StringBuilder("cell x=" + (X0 + (ix + 0.5f) * C) + " z=" + (Z0 + (iz + 0.5f) * C) + ": ");
            for (int k = 0; k < ys[c].Length; k++) if (ys[c][k] >= yLo && ys[c][k] <= yHi) sb.Append("[y=" + ys[c][k].ToString("F2") + (wide[c][k] ? " wide" : " narrow") + "] ");
            return sb.ToString();
        }

        /// <summary>ASCII map of what is reachable from the lobby spawn (for diagnosing blockers).</summary>
        public static string Map(float xMin, float xMax, float zMin, float zMax, float yLo, float yHi)
        {
            Prepare();
            Dijkstra(NearestNode(new Vector3(0f, 0.1f, 5f), 1.5f, 0.8f));
            var sb = new StringBuilder(); sb.AppendLine($"map x[{xMin},{xMax}] z[{zMin},{zMax}] y[{yLo},{yHi}]   '.'=reachable  'o'=walkable but unreachable  ' '=no floor");
            for (float z = zMax; z >= zMin; z -= C)
            {
                sb.Append(z.ToString("F1").PadLeft(5)).Append(' ');
                for (float x = xMin; x <= xMax; x += C)
                {
                    int ix = Mathf.FloorToInt((x - X0) / C), iz = Mathf.FloorToInt((z - Z0) / C);
                    char ch = ' '; int c = Cell(ix, iz);
                    for (int k = 0; k < ys[c].Length; k++)
                    {
                        float y = ys[c][k]; if (y < yLo || y > yHi) continue;
                        int node = start[c] + k; ch = dist[node] < float.MaxValue ? '.' : 'o'; if (ch == '.') break;
                    }
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Reachable floor points (one per `spacing` metres) for physical walk tests.</summary>
        public static List<Vector3> ReachableSamples(float spacing)
        {
            Prepare();
            Dijkstra(NearestNode(new Vector3(0f, 0.1f, 5f), 1.5f, 0.8f));
            var o = new List<Vector3>(); int stepCells = Mathf.Max(1, Mathf.RoundToInt(spacing / C));
            for (int ix = 0; ix < NX; ix += stepCells)
                for (int iz = 0; iz < NZ; iz += stepCells)
                {
                    int c = Cell(ix, iz);
                    for (int k = 0; k < ys[c].Length; k++) if (dist[start[c] + k] != float.MaxValue) o.Add(new Vector3(X0 + (ix + 0.5f) * C, ys[c][k], Z0 + (iz + 0.5f) * C));
                }
            return o;
        }

        /// <summary>Every reachable floor cell that has an unprotected drop (> dropMin) beside it, grouped in 2 m blocks.</summary>
        public static string FallEdges(float dropMin)
        {
            Prepare();
            Dijkstra(NearestNode(new Vector3(0f, 0.1f, 5f), 1.5f, 0.8f));
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            var groups = new SortedDictionary<string, int>();
            for (int ix = 0; ix < NX; ix++)
                for (int iz = 0; iz < NZ; iz++)
                {
                    int c = Cell(ix, iz);
                    for (int k = 0; k < ys[c].Length; k++)
                    {
                        int node = start[c] + k; if (dist[node] == float.MaxValue) continue;
                        float y = ys[c][k]; float x = X0 + (ix + 0.5f) * C, z = Z0 + (iz + 0.5f) * C;
                        for (int e = 0; e < 4; e++)
                        {
                            int nx = ix + dx[e], nz = iz + dz[e]; if (nx < 0 || nz < 0 || nx >= NX || nz >= NZ) continue;
                            if (Near(nx, nz, y) >= 0) continue;                      // a normal step exists: not a drop
                            // is anything solid in the way at body height (wall/rail)?
                            Vector3 o = new Vector3(x, y + 0.9f, z), dir = new Vector3(dx[e], 0, dz[e]);
                            if (Physics.Raycast(o, dir, C + R, ~0, QueryTriggerInteraction.Ignore)) continue;
                            if (Physics.Raycast(o + Vector3.up * 0.5f, dir, C + R, ~0, QueryTriggerInteraction.Ignore)) continue;
                            // how deep is the drop beside the cell?
                            Vector3 p = new Vector3(x + dx[e] * (C + 0.05f), y - 0.1f, z + dz[e] * (C + 0.05f));
                            float drop; RaycastHit h;
                            drop = Physics.Raycast(p, Vector3.down, out h, 60f, ~0, QueryTriggerInteraction.Ignore) ? (y - h.point.y) : 99f;
                            if (drop < dropMin) continue;
                            string key = "y=" + y.ToString("F1") + "  x=" + (Mathf.Floor(x / 2f) * 2f) + ".." + (Mathf.Floor(x / 2f) * 2f + 2f) + "  z=" + (Mathf.Floor(z / 2f) * 2f) + ".." + (Mathf.Floor(z / 2f) * 2f + 2f) + "  drop~" + (drop > 50f ? "void" : drop.ToString("F0") + "m");
                            groups[key] = (groups.ContainsKey(key) ? groups[key] : 0) + 1;
                        }
                    }
                }
            var sb = new StringBuilder("fall hazards (reachable cell with open drop >= " + dropMin + " m): " + groups.Count + " blocks\n");
            foreach (var kv in groups) sb.AppendLine("  " + kv.Key + "   edges=" + kv.Value);
            return sb.ToString();
        }

        const string GridPath = "Assets/S014_MobileAR/CampusVR/Resources/S014WalkGrid.bytes";

        [MenuItem("S014/Routes/Bake runtime walk grid")]
        public static void MenuBake() { Prepare(); Debug.Log(BakeGrid()); }

        /// <summary>Writes the walkable grid for the runtime planner (S014WalkGrid) so the kiosk can route from anywhere.</summary>
        public static string BakeGrid()
        {
            if (ys == null) Prepare();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GridPath));
            using (var bw = new System.IO.BinaryWriter(System.IO.File.Create(GridPath)))
            {
                bw.Write(NX); bw.Write(NZ); bw.Write(X0); bw.Write(Z0); bw.Write(N);
                for (int c = 0; c < NX * NZ; c++)
                {
                    bw.Write((byte)ys[c].Length);
                    for (int k = 0; k < ys[c].Length; k++) { bw.Write(ys[c][k]); bw.Write(wide[c][k]); }
                }
            }
            AssetDatabase.ImportAsset(GridPath);
            return "baked walk grid: " + N + " nodes -> " + GridPath;
        }

        static string Enc(List<Vector3> p)
        {
            var s = new StringBuilder();
            for (int i = 0; i < p.Count; i++) { if (i > 0) s.Append(';'); s.Append(p[i].x.ToString("F2")).Append(',').Append(p[i].y.ToString("F2")).Append(',').Append(p[i].z.ToString("F2")); }
            return s.ToString();
        }

        public static string Rebuild(bool apply)
        {
            var dir = Object.FindAnyObjectByType<S014CampusDirectory>();
            Prepare();
            var sb = new StringBuilder(); var bad = new List<string>();
            int sOk = 0, lOk = 0; var perFloor = new SortedDictionary<string, int>();

            int s0 = NearestNode(new Vector3(0f, 0.1f, 5f), 1.5f, 0.8f); Dijkstra(s0);
            var stairs = new Dictionary<string, List<Vector3>>();
            foreach (var d in dir.destinations)
            {
                int g = NearestNode(d.anchor.position, 2.0f, 1.0f);
                if (g < 0 || dist[g] == float.MaxValue) { bad.Add("STAIRS unreachable " + d.id + " (floor " + d.floorLabel + ")"); continue; }
                stairs[d.id] = Simplify(Extract(g), 0.08f); sOk++;
                string fk = "floor " + d.floorLabel; perFloor[fk] = (perFloor.ContainsKey(fk) ? perFloor[fk] : 0) + 1;
            }

            var lift = new Dictionary<string, List<Vector3>>(); int lastSrc = -2; var srcCache = new Dictionary<string, int>();
            foreach (var d in dir.destinations)
            {
                var exit = new Vector3(0f, d.liftExit.y, 30.5f);        // always the south lifts in the lobby
                string key = exit.y.ToString("F1"); int src;
                if (!srcCache.TryGetValue(key, out src)) { src = NearestNode(exit, 1.5f, 0.8f); srcCache[key] = src; }
                if (src < 0) { bad.Add("LIFT no exit floor node for floor " + d.floorLabel); continue; }
                if (lastSrc != src) { Dijkstra(src); lastSrc = src; }
                int g = NearestNode(d.anchor.position, 2.0f, 1.0f);
                if (g < 0 || dist[g] == float.MaxValue) { bad.Add("LIFT unreachable " + d.id + " (floor " + d.floorLabel + ")"); continue; }
                lift[d.id] = Simplify(Extract(g), 0.08f); lOk++;
            }

            sb.AppendLine($"destinations={dir.destinations.Count}  stairs walkable={sOk}  lift walkable={lOk}  gridNodes={N}");
            sb.AppendLine("stairs walkable per floor: " + string.Join(", ", new List<string>(perFloor.Keys).ConvertAll(x => x + "=" + perFloor[x])));
            foreach (var b in bad) sb.AppendLine("  " + b);

            if (apply)
            {
                foreach (var d in dir.destinations)
                {
                    d.liftExit = new Vector3(0f, d.liftExit.y, 30.5f);
                    List<Vector3> lp;
                    d.liftPath = lift.TryGetValue(d.id, out lp) ? new List<Vector3>(lp) : new List<Vector3> { d.liftExit };
                    List<Vector3> sp;
                    d.stairsWalkable = stairs.TryGetValue(d.id, out sp);
                    d.pathFromLobby = d.stairsWalkable ? new List<Vector3>(sp) : new List<Vector3>();
                }
                EditorUtility.SetDirty(dir);
                sb.AppendLine("APPLIED to the campus directory (save the scene).");
                sb.AppendLine(BakeGrid());
            }
            return sb.ToString();
        }
    }
}
