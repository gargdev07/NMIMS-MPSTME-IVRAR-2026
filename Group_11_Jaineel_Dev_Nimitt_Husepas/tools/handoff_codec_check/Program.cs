using System;
using System.Collections.Generic;

// Stand-alone checks for the kiosk handoff codec and the multi-floor A* graph (R057).
// Run:  dotnet run --project tools/handoff_codec_check
// Exit code 0 = every check passed.
internal static class Program
{
    private static int failures;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
        if (!ok) failures++;
    }

    private static int Main()
    {
        const string secret = "IVRAR-G11-DEMO-KEY";
        long now = 1_800_000_000L;

        // a realistic route: 90 waypoints, 0.5 m to 2 m apart, climbing two floors
        var wp = new List<float>();
        float x = 0, y = 0, z = 5;
        for (int i = 0; i < 90; i++) { x += (i % 7 == 0 ? 0.8f : 0f); y += (i > 60 ? 0.13f : 0f); z += 0.9f; wp.Add(x); wp.Add(y); wp.Add(z); }
        var p = new HandoffPayloadCodec.Payload { kioskId = "KIOSK_MAIN_LOBBY", destinationId = "f7-ar-vr-lab", floorLevel = 8, issuedAtUnix = now, waypointsXYZ = wp.ToArray() };

        string token = HandoffPayloadCodec.Encode(p, secret);
        HandoffPayloadCodec.Payload q; string err;
        bool ok = HandoffPayloadCodec.TryDecode(token, secret, now + 10, HandoffPayloadCodec.DefaultTtlSeconds, out q, out err);
        Check("round trip decodes", ok && q.destinationId == p.destinationId && q.floorLevel == 8 && q.kioskId == p.kioskId);
        double maxErr = 0;
        for (int i = 0; i < p.waypointsXYZ.Length && ok; i++) maxErr = Math.Max(maxErr, Math.Abs(p.waypointsXYZ[i] - q.waypointsXYZ[i]));
        Check("waypoints survive within 1 cm (max error " + (maxErr * 100).ToString("F2") + " cm)", ok && maxErr <= 0.0101);
        Console.WriteLine("INFO  token length for a 90-waypoint route: " + token.Length + " characters (QR version 40-L holds 2953 bytes)");
        Check("token fits a QR code", token.Length < 2953);

        Check("accepted at the 45 s limit", HandoffPayloadCodec.TryDecode(token, secret, now + 45, 45, out q, out err));
        Check("rejected after 46 s (expired)", !HandoffPayloadCodec.TryDecode(token, secret, now + 46, 45, out q, out err) && err == "expired");
        Check("rejected when issued in the future", !HandoffPayloadCodec.TryDecode(token, secret, now - 5, 45, out q, out err));
        Check("rejected with the wrong secret", !HandoffPayloadCodec.TryDecode(token, "other-key", now + 1, 45, out q, out err) && err == "bad signature");

        char[] chars = token.ToCharArray();
        chars[40] = chars[40] == 'A' ? 'B' : 'A';
        Check("rejected when one character is tampered", !HandoffPayloadCodec.TryDecode(new string(chars), secret, now + 1, 45, out q, out err));
        Check("rejected when truncated", !HandoffPayloadCodec.TryDecode(token.Substring(0, 30), secret, now + 1, 45, out q, out err));
        Check("rejected when not base64", !HandoffPayloadCodec.TryDecode("###", secret, now + 1, 45, out q, out err));
        Check("two tokens of the same payload differ (random IV)", HandoffPayloadCodec.Encode(p, secret) != HandoffPayloadCodec.Encode(p, secret));
        Check("plaintext destination id is not visible in the token", !token.Contains("ar-vr"));

        // A*: two floors, a stair link and a lift link
        var g = new CampusGraph { gammaFloorMetres = 6f, liftWaitSeconds = 20f, walkSpeedMetresPerSecond = 1.2f };
        g.AddNode("K", 0, 0, 0, 0); g.AddNode("S0", 10, 0, 0, 0); g.AddNode("S1", 10, 4, 0, 1);
        g.AddNode("L0", 0, 0, 12, 0); g.AddNode("L1", 0, 4, 12, 1); g.AddNode("D", 10, 4, 8, 1);
        g.AddEdge("K", "S0"); g.AddEdge("S0", "S1"); g.AddEdge("S1", "D");
        g.AddEdge("K", "L0"); g.AddEdge("L0", "L1", true); g.AddEdge("L1", "D");
        float cost; var path = g.ShortestPath("K", "D", out cost);
        // stairs: 10 + (4+6) + 8 = 28 ; lift: 12 + (4+6+24) + 10.8 = 56.8  -> stairs win
        Check("A* picks the cheaper stair route (cost " + cost.ToString("F1") + ", expected 28.0)", path != null && string.Join(">", path) == "K>S0>S1>D" && Math.Abs(cost - 28f) < 0.01f);
        // far-away stairs make the lift worth its 20 s wait: stairs 40+10+31.05=81.05 ; lift 12+10+24+10.77=56.77
        var g2 = new CampusGraph { gammaFloorMetres = 6f, liftWaitSeconds = 20f, walkSpeedMetresPerSecond = 1.2f };
        g2.AddNode("K", 0, 0, 0, 0); g2.AddNode("S0", 40, 0, 0, 0); g2.AddNode("S1", 40, 4, 0, 1);
        g2.AddNode("L0", 0, 0, 12, 0); g2.AddNode("L1", 0, 4, 12, 1); g2.AddNode("D", 10, 4, 8, 1);
        g2.AddEdge("K", "S0"); g2.AddEdge("S0", "S1"); g2.AddEdge("S1", "D");
        g2.AddEdge("K", "L0"); g2.AddEdge("L0", "L1", true); g2.AddEdge("L1", "D");
        path = g2.ShortestPath("K", "D", out cost);
        Check("A* switches to the lift when the stairs are far (cost " + cost.ToString("F2") + ", expected 56.77)", path != null && string.Join(">", path) == "K>L0>L1>D" && Math.Abs(cost - 56.77f) < 0.05f);
        Check("unreachable goal returns null", g.ShortestPath("K", "missing", out cost) == null);

        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
        return failures;
    }
}
