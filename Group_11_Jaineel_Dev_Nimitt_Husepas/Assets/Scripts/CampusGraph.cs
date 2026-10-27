using System;
using System.Collections.Generic;

/// <summary>
/// Multi-floor topological campus graph (R057 - Jaineel Shah).
/// Vertices are decision points (junctions, stair landings, lift lobbies, room entrances);
/// edges are walkable links. The edge cost follows the Research and Implementation Guide, section 1.2:
///
///   W(u, v) = || p_u - p_v ||_2  +  gammaFloor * |floor_u - floor_v|  +  liftWaitSeconds * walkSpeed  (lift edges only)
///
/// and the path is found with A* using the Euclidean distance to the goal as heuristic
/// h(v) = || p_v - p_dest ||_2, which is admissible because gammaFloor and the lift wait are non-negative.
/// Pure C# (no UnityEngine types) so it can be checked outside the editor.
/// </summary>
public class CampusGraph
{
    public class Node
    {
        public string id; public float x, y, z; public int floor;
        public List<Edge> edges = new List<Edge>();
    }

    public class Edge
    {
        public Node to; public bool isLift;
    }

    public float gammaFloorMetres = 6.0f;     // stair-climbing impedance per floor crossed (metre-equivalent)
    public float liftWaitSeconds = 20.0f;     // expected lift dispatch wait
    public float walkSpeedMetresPerSecond = 1.2f;

    private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>();

    public int NodeCount { get { return nodes.Count; } }

    public Node AddNode(string id, float x, float y, float z, int floor)
    {
        Node n;
        if (nodes.TryGetValue(id, out n)) return n;
        n = new Node { id = id, x = x, y = y, z = z, floor = floor };
        nodes[id] = n;
        return n;
    }

    public void AddEdge(string a, string b, bool isLift = false)
    {
        Node na = nodes[a], nb = nodes[b];
        na.edges.Add(new Edge { to = nb, isLift = isLift });
        nb.edges.Add(new Edge { to = na, isLift = isLift });
    }

    public float EdgeCost(Node u, Node v, bool isLift)
    {
        float dx = u.x - v.x, dy = u.y - v.y, dz = u.z - v.z;
        float c = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        c += gammaFloorMetres * Math.Abs(u.floor - v.floor);
        if (isLift) c += liftWaitSeconds * walkSpeedMetresPerSecond;
        return c;
    }

    private float Heuristic(Node a, Node goal)
    {
        float dx = a.x - goal.x, dy = a.y - goal.y, dz = a.z - goal.z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>A* shortest path. Returns the node ids from start to goal, or null if unreachable.</summary>
    public List<string> ShortestPath(string startId, string goalId, out float totalCost)
    {
        totalCost = 0f;
        Node start, goal;
        if (!nodes.TryGetValue(startId, out start) || !nodes.TryGetValue(goalId, out goal)) return null;

        var g = new Dictionary<Node, float> { { start, 0f } };
        var prev = new Dictionary<Node, Node>();
        var open = new List<KeyValuePair<float, Node>> { new KeyValuePair<float, Node>(Heuristic(start, goal), start) };
        var closed = new HashSet<Node>();

        while (open.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++) if (open[i].Key < open[bi].Key) bi = i;
            Node u = open[bi].Value; open.RemoveAt(bi);
            if (closed.Contains(u)) continue;
            if (u == goal)
            {
                var path = new List<string>();
                for (Node n = goal; n != null; prev.TryGetValue(n, out n)) { path.Add(n.id); if (n == start) break; }
                path.Reverse();
                totalCost = g[goal];
                return path;
            }
            closed.Add(u);
            foreach (var e in u.edges)
            {
                if (closed.Contains(e.to)) continue;
                float cand = g[u] + EdgeCost(u, e.to, e.isLift);
                float old;
                if (!g.TryGetValue(e.to, out old) || cand < old)
                {
                    g[e.to] = cand; prev[e.to] = u;
                    open.Add(new KeyValuePair<float, Node>(cand + Heuristic(e.to, goal), e.to));
                }
            }
        }
        return null;
    }

    public Node Get(string id) { Node n; return nodes.TryGetValue(id, out n) ? n : null; }
}
