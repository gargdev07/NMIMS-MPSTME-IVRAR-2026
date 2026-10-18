using System;
using System.Text;
using UnityEngine;

namespace R057.Kiosk
{
    public sealed class R057QRCodeHandoff : MonoBehaviour
    {
        [Serializable]
        private sealed class WebHandoffPayload
        {
            public string sessionId;
            public string routeId;
            public string destinationId;
            public string destinationName;
            public string targetFloor;
            public string routeVersion;
        }

        [SerializeField] private string browserBaseUrl = "https://gargdev07.github.io/NMIMS-MPSTME-IVRAR-2026/webxr/index.html";
        [SerializeField, Min(8)] private int pixelsPerModule = 12;

        public Texture2D QrTexture { get; private set; }
        public string PayloadUrl { get; private set; }
        public string ErrorMessage { get; private set; }

        public void Generate(R057NavigationPayload payload)
        {
            Clear();
            if (payload == null)
            {
                ErrorMessage = "Cannot generate handoff: session payload is missing.";
                return;
            }
            if (string.IsNullOrWhiteSpace(payload.sessionId) ||
                string.IsNullOrWhiteSpace(payload.routeId) ||
                string.IsNullOrWhiteSpace(payload.destinationId) ||
                string.IsNullOrWhiteSpace(payload.destinationName) ||
                string.IsNullOrWhiteSpace(payload.targetFloor) ||
                string.IsNullOrWhiteSpace(payload.routeVersion))
            {
                ErrorMessage = "Cannot generate handoff: the session is missing route identity fields.";
                return;
            }
            if (!Guid.TryParseExact(payload.sessionId, "N", out _))
            {
                ErrorMessage = "Cannot generate handoff: session ID is not a valid 32-character GUID.";
                return;
            }
            if (!string.Equals(payload.routeVersion, "2.0", StringComparison.Ordinal))
            {
                ErrorMessage = "Cannot generate handoff: unsupported route version " + payload.routeVersion + ".";
                return;
            }
            if (!string.Equals(payload.routeId, "CampusRoute_" + payload.destinationId, StringComparison.Ordinal))
            {
                ErrorMessage = "Cannot generate handoff: route ID does not match the selected destination ID.";
                return;
            }
            if (payload.targetFloor != "Ground" && payload.targetFloor != "First")
            {
                ErrorMessage = "Cannot generate handoff: destination floor is not supported.";
                return;
            }

            Uri baseUri;
            if (!Uri.TryCreate(browserBaseUrl, UriKind.Absolute, out baseUri) ||
                !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "A valid HTTPS WebXR handoff URL is required.";
                return;
            }

            var webPayload = new WebHandoffPayload
            {
                sessionId = payload.sessionId,
                routeId = payload.routeId,
                destinationId = payload.destinationId,
                destinationName = payload.destinationName,
                targetFloor = payload.targetFloor,
                routeVersion = payload.routeVersion
            };
            var encodedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(webPayload)));
            PayloadUrl = browserBaseUrl.TrimEnd('/') + "?payload=" + Uri.EscapeDataString(encodedPayload);
            if (IsLocalDevelopmentUrl(baseUri))
            {
                ErrorMessage = "This HTTPS URL points to a local development host and is not reachable by a phone.";
                PayloadUrl = null;
                return;
            }

            try
            {
                var matrix = R057QrCodeEncoder.Encode(PayloadUrl);
                QrTexture = CreateTexture(matrix);
                ErrorMessage = string.Empty;
            }
            catch (ArgumentException exception)
            {
                PayloadUrl = null;
                ErrorMessage = "QR generation failed: " + exception.Message;
            }
        }

        public void Clear()
        {
            if (QrTexture != null)
            {
                if (Application.isPlaying) Destroy(QrTexture);
                else DestroyImmediate(QrTexture);
                QrTexture = null;
            }
            PayloadUrl = null;
            ErrorMessage = string.Empty;
        }

        private Texture2D CreateTexture(bool[,] modules)
        {
            const int quietZone = 4;
            var moduleCount = modules.GetLength(0) + quietZone * 2;
            var scale = Mathf.Max(8, pixelsPerModule);
            var dimension = moduleCount * scale;
            var texture = new Texture2D(dimension, dimension, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Local destination QR code"
            };
            var pixels = new Color32[dimension * dimension];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (var i = 0; i < pixels.Length; i++) pixels[i] = white;
            for (var y = 0; y < modules.GetLength(0); y++)
            {
                for (var x = 0; x < modules.GetLength(1); x++)
                {
                    if (!modules[y, x]) continue;
                    var left = (x + quietZone) * scale;
                    var top = (y + quietZone) * scale;
                    for (var dy = 0; dy < scale; dy++)
                    {
                        var pixelY = dimension - 1 - (top + dy);
                        var row = pixelY * dimension;
                        for (var dx = 0; dx < scale; dx++)
                            pixels[row + left + dx] = black;
                    }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static bool IsLocalDevelopmentUrl(Uri uri)
        {
            return uri.IsLoopback ||
                   uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
