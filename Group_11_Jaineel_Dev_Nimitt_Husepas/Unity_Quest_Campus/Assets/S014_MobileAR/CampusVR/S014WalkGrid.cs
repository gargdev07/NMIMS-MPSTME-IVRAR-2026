using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace S014.CampusVR
{
    /// <summary>
    /// Runtime route planner over the walkable-surface grid baked by the editor tool
    /// (Resources/S014WalkGrid.bytes). Lets the kiosk plan a route from wherever the visitor
    /// stands, on any floor (stairs included), to any room.
    /// </summary>
    public static class S014WalkGrid
    {
        const float C = 0.5f, MAXUP = 0.30f, MAXDOWN = 1.0f;
        static float x0, z0;
        static int nx, nz, total;
        static float[] ys;
        static bool[] wide;
        static int[] start;       // first node of each cell, length nx*nz+1
        static bool loaded, failed;

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public static bool Load()
        {
            if (loaded) return true;
            if (failed) return false;
            var ta = Resources.Load<TextAsset>("S014WalkGrid");
            if (ta == null) { failed = true; Debug.LogWarning("[S014] walk grid missing - routes fall back to the main gate"); return false; }
            using (var br = new BinaryReader(new MemoryStream(ta.bytes)))
            {
                nx = br.ReadInt32(); nz = br.ReadInt32(); x0 = br.ReadSingle(); z0 = br.ReadSingle(); total = br.ReadInt32();
                ys = new float[total]; wide = new bool[total]; start = new int[nx * nz + 1];
                int n = 0;
                for (int c = 0; c < nx * nz; c++)
                {
                    start[c] = n; int cnt = br.ReadByte();
                    for (int k = 0; k < cnt; k++) { ys[n] = br.ReadSingle(); wide[n] = br.ReadBoolean(); n++; }
                }
                start[nx * nz] = n;
            }
            loaded = true;
            Debug.Log("[S014] walk grid loaded: " + total + " nodes");
            return true;
        }

        static int CellOf(int node)
        {
            int lo = 0, hi = nx * nz - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (start[mid] <= node) lo = mid; else hi = mid - 1; }
            return lo;
        }

        static Vector3 Pos(int node)
        {
            int c = CellOf(node); int ix = c / nz, iz = c % nz;
            return new Vector3(x0 + (ix + 0.5f) * C, ys[node], z0 + (iz + 0.5f) * C);
        }

        static int NearestNode(Vector3 p, float maxXZ, float maxY)
        {
            int ix0 = Mathf.FloorToInt((p.x - x0) / C), iz0 = Mathf.FloorToInt((p.z - z0) / C);
            int best = -1; float bd = float.MaxValue; int rad = Mathf.CeilToInt(maxXZ / C);
            for (int ix = ix0 - rad; ix <= ix0 + rad; ix++)
                for (int iz = iz0 - rad; iz <= iz0 + rad; iz++)
                {
                    if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) continue;
                    int c = ix * nz + iz;
                    for (int k = start[c]; k < start[c + 1]; k++)
                    {
                        float dy = Mathf.Abs(ys[k] - p.y); if (dy > maxY) continue;
                        float d = Vector2.Distance(new Vector2(x0 + (ix + 0.5f) * C, z0 + (iz + 0.5f) * C), new Vector2(p.x, p.z)) + dy * 0.5f;
                        if (d < bd) { bd = d; best = k; }
                    }
                }
            return best;
        }

        /// <summary>Walkable route from `from` to `to` (feet positions); null if there is none.</summary>
        public static List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            if (!Load()) return null;
            int s = NearestNode(from, 1.5f, 1.2f);
            if (s < 0) s = NearestNode(from, 3.5f, 1.8f);
            int g = NearestNode(to, 2.0f, 1.0f);
            if (s < 0 || g < 0) return null;

            var dist = new float[total]; var parent = new int[total];
            for (int i = 0; i < total; i++) { dist[i] = float.MaxValue; parent[i] = -1; }
            var heap = new List<KeyValuePair<float, int>>();
            dist[s] = 0f; Push(heap, 0f, s);
            while (heap.Count > 0)
            {
                var top = Pop(heap); float d = top.Key; int u = top.Value;
                if (d > dist[u]) continue;
                if (u == g) break;
                int c = CellOf(u); int ix = c / nz, iz = c % nz; float y = ys[u]; bool uw = wide[u];
                for (int e = 0; e < 8; e++)
                {
                    int cx = ix + DX[e], cz = iz + DZ[e];
                    if (cx < 0 || cz < 0 || cx >= nx || cz >= nz) continue;
                    if (e >= 4 && (!HasStep(ix + DX[e], iz, y) || !HasStep(ix, iz + DZ[e], y))) continue;
                    int vc = cx * nz + cz;
                    for (int v = start[vc]; v < start[vc + 1]; v++)
                    {
                        float dy = ys[v] - y; if (dy > MAXUP || dy < -MAXDOWN) continue;
                        float nd = d + (e < 4 ? C : C * 1.4142f) * ((uw && wide[v]) ? 1f : 40f);
                        if (nd < dist[v]) { dist[v] = nd; parent[v] = u; Push(heap, nd, v); }
                    }
                }
            }
            if (dist[g] == float.MaxValue) return null;

            var rev = new List<Vector3>(); int n = g;
            while (n >= 0) { rev.Add(Pos(n)); n = parent[n]; }
            rev.Reverse();
            return Simplify(rev, 0.08f);
        }

        static bool HasStep(int cx, int cz, float y)
        {
            if (cx < 0 || cz < 0 || cx >= nx || cz >= nz) return false;
            int c = cx * nz + cz;
            for (int k = start[c]; k < start[c + 1]; k++) { float dy = ys[k] - y; if (dy <= MAXUP && dy >= -MAXDOWN) return true; }
            return false;
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
    }
}
