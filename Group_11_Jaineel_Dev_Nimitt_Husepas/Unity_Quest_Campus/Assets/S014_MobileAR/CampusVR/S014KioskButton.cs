using UnityEngine;

namespace S014.CampusVR
{
    /// <summary>A pickable 3D kiosk tile. Interaction is driven by
    /// S014CampusPointer (controller ray or editor mouse) via a physics ray.
    /// Lives in its own file so Unity can serialize it into the scene/build.</summary>
    public class S014KioskButton : MonoBehaviour
    {
        public string destinationId;   // room id, or "floor:N", "mode:lift", "mode:stairs", "__start__", "__reset__"
        public S014CampusKiosk kiosk;
        public Renderer targetRenderer;
        public Color baseColor = new Color(0.10f, 0.16f, 0.28f);
        public Color hotColor = new Color(0.18f, 0.55f, 0.95f);

        public void SetHighlight(bool on)
        {
            if (targetRenderer != null) targetRenderer.material.color = on ? hotColor : baseColor;
        }

        public void Click() { if (kiosk != null) kiosk.Select(destinationId); }
    }
}
