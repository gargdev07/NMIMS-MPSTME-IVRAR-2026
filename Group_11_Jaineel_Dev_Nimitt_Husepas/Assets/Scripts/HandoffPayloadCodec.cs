using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Serialises a kiosk-to-device handoff (destination, floor, compressed route) into a short,
/// encrypted and authenticated token that fits a QR code, and verifies it again with a lease time.
///
/// Design (R057 - Jaineel Shah):
///   plaintext  = version | issuedAt(unix s) | floor | kioskId | destinationId | waypoints (cm, delta + zig-zag varint)
///   ciphertext = AES-256-CBC(plaintext) with a random 16-byte IV
///   token      = base64url( IV | ciphertext | HMAC-SHA256(IV | ciphertext)[0..15] )   (encrypt-then-MAC)
///   The token expires <c>ttlSeconds</c> after <c>issuedAt</c> (default 45 s, the QR lease of the title sheet).
///
/// Pure C# with no UnityEngine types, so it can be unit-checked outside the editor
/// (see tools/handoff_codec_check).
/// NOTE: the shared secret is a demonstration key supplied by the kiosk. A production kiosk would hold
/// it server-side; the WebXR client would receive it over a secure channel, not embed it.
/// </summary>
public static class HandoffPayloadCodec
{
    public const byte FormatVersion = 1;
    public const int DefaultTtlSeconds = 45;
    private const int TagBytes = 16;

    [Serializable]
    public struct Payload
    {
        public string kioskId;
        public string destinationId;
        public int floorLevel;
        public long issuedAtUnix;
        public float[] waypointsXYZ; // flat x,y,z metres
    }

    public static string Encode(Payload p, string secret, byte[] ivForTests = null)
    {
        if (string.IsNullOrEmpty(secret)) throw new ArgumentException("secret required");
        byte[] plain = Serialise(p);
        byte[] iv = ivForTests;
        if (iv == null) { iv = new byte[16]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv); }
        byte[] cipher;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = Derive(secret, "enc"); aes.IV = iv;
            using (var enc = aes.CreateEncryptor()) cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
        }
        byte[] body = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, body, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, body, iv.Length, cipher.Length);
        byte[] tag = Tag(secret, body);
        byte[] all = new byte[body.Length + TagBytes];
        Buffer.BlockCopy(body, 0, all, 0, body.Length);
        Buffer.BlockCopy(tag, 0, all, body.Length, TagBytes);
        return B64UrlEncode(all);
    }

    /// <summary>Returns true and the payload if the token is authentic, well-formed and not expired.</summary>
    public static bool TryDecode(string token, string secret, long nowUnix, int ttlSeconds, out Payload payload, out string error)
    {
        payload = default(Payload); error = null;
        byte[] all;
        try { all = B64UrlDecode(token); } catch (Exception) { error = "malformed"; return false; }
        if (all == null || all.Length < 16 + 16 + TagBytes) { error = "too short"; return false; }
        int bodyLen = all.Length - TagBytes;
        byte[] body = new byte[bodyLen];
        Buffer.BlockCopy(all, 0, body, 0, bodyLen);
        byte[] gotTag = new byte[TagBytes];
        Buffer.BlockCopy(all, bodyLen, gotTag, 0, TagBytes);
        if (!ConstantTimeEquals(gotTag, Tag(secret, body))) { error = "bad signature"; return false; }

        byte[] iv = new byte[16];
        Buffer.BlockCopy(body, 0, iv, 0, 16);
        byte[] cipher = new byte[bodyLen - 16];
        Buffer.BlockCopy(body, 16, cipher, 0, cipher.Length);
        byte[] plain;
        try
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = Derive(secret, "enc"); aes.IV = iv;
                using (var dec = aes.CreateDecryptor()) plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            }
            payload = Deserialise(plain);
        }
        catch (Exception) { error = "bad payload"; return false; }

        long age = nowUnix - payload.issuedAtUnix;
        if (age < 0 || age > ttlSeconds) { error = "expired"; return false; }
        return true;
    }

    // ---------------------------------------------------------------- binary layout

    private static byte[] Serialise(Payload p)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(FormatVersion);
            for (int i = 7; i >= 0; i--) w.Write((byte)((p.issuedAtUnix >> (8 * i)) & 0xFF));
            w.Write((sbyte)p.floorLevel);
            WriteString(w, p.kioskId); WriteString(w, p.destinationId);
            int n = p.waypointsXYZ == null ? 0 : p.waypointsXYZ.Length / 3;
            WriteVarint(w, (ulong)n);
            long px = 0, py = 0, pz = 0;
            for (int i = 0; i < n; i++)
            {
                long x = (long)Math.Round(p.waypointsXYZ[3 * i] * 100.0);
                long y = (long)Math.Round(p.waypointsXYZ[3 * i + 1] * 100.0);
                long z = (long)Math.Round(p.waypointsXYZ[3 * i + 2] * 100.0);
                WriteVarint(w, ZigZag(x - px)); WriteVarint(w, ZigZag(y - py)); WriteVarint(w, ZigZag(z - pz));
                px = x; py = y; pz = z;
            }
            w.Flush();
            return ms.ToArray();
        }
    }

    private static Payload Deserialise(byte[] data)
    {
        using (var ms = new MemoryStream(data))
        using (var r = new BinaryReader(ms))
        {
            if (r.ReadByte() != FormatVersion) throw new InvalidDataException("version");
            long t = 0; for (int i = 0; i < 8; i++) t = (t << 8) | r.ReadByte();
            var p = new Payload();
            p.issuedAtUnix = t;
            p.floorLevel = r.ReadSByte();
            p.kioskId = ReadString(r); p.destinationId = ReadString(r);
            int n = (int)ReadVarint(r);
            if (n < 0 || n > 4096) throw new InvalidDataException("count");
            p.waypointsXYZ = new float[3 * n];
            long px = 0, py = 0, pz = 0;
            for (int i = 0; i < n; i++)
            {
                px += UnZigZag(ReadVarint(r)); py += UnZigZag(ReadVarint(r)); pz += UnZigZag(ReadVarint(r));
                p.waypointsXYZ[3 * i] = px / 100f; p.waypointsXYZ[3 * i + 1] = py / 100f; p.waypointsXYZ[3 * i + 2] = pz / 100f;
            }
            return p;
        }
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s ?? "");
        if (b.Length > 255) throw new ArgumentException("string too long");
        w.Write((byte)b.Length); w.Write(b);
    }

    private static string ReadString(BinaryReader r) { int n = r.ReadByte(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
    private static ulong ZigZag(long v) { return (ulong)((v << 1) ^ (v >> 63)); }
    private static long UnZigZag(ulong v) { return (long)(v >> 1) ^ -(long)(v & 1); }

    private static void WriteVarint(BinaryWriter w, ulong v)
    {
        while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
        w.Write((byte)v);
    }

    private static ulong ReadVarint(BinaryReader r)
    {
        ulong v = 0; int shift = 0;
        while (true)
        {
            byte b = r.ReadByte();
            v |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return v;
            shift += 7; if (shift > 63) throw new InvalidDataException("varint");
        }
    }

    // ---------------------------------------------------------------- crypto helpers

    private static byte[] Derive(string secret, string label)
    {
        using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(secret + "|" + label));
    }

    private static byte[] Tag(string secret, byte[] body)
    {
        using (var h = new HMACSHA256(Derive(secret, "mac")))
        {
            byte[] full = h.ComputeHash(body);
            byte[] t = new byte[TagBytes];
            Buffer.BlockCopy(full, 0, t, 0, TagBytes);
            return t;
        }
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
        return d == 0;
    }

    private static string B64UrlEncode(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

    private static byte[] B64UrlDecode(string s)
    {
        string t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; case 1: throw new FormatException(); }
        return Convert.FromBase64String(t);
    }
}
