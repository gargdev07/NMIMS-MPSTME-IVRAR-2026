using UnityEngine;
using UnityEngine.InputSystem;

namespace S014.CampusVR
{
    /// <summary>Casts a ray from the right controller (or the editor mouse when
    /// no controller is connected), highlights the kiosk tile under it, and
    /// clicks it on trigger / left mouse button.</summary>
    public class S014CampusPointer : MonoBehaviour
    {
        public Transform playerRoot;
        public Transform rightAnchor;
        public Camera cam;
        public LineRenderer beam;
        [Min(1f)] public float maxDistance = 6f;

        private S014KioskButton hovered;
        private bool prevTrigger;

        private void Update()
        {
            Ray ray;
            bool clickDown;

            if (S014VRInput.ControllerConnected())
            {
                if (rightAnchor != null) ray = new Ray(rightAnchor.position, rightAnchor.forward);
                else if (S014VRInput.TryRightPose(playerRoot, out var pp, out var rr)) ray = new Ray(pp, rr * Vector3.forward);
                else { var c0 = cam != null ? cam : Camera.main; ray = c0 != null ? new Ray(c0.transform.position, c0.transform.forward) : new Ray(transform.position, transform.forward); }
                bool trig = S014VRInput.RightTrigger();
                clickDown = trig && !prevTrigger;
                prevTrigger = trig;
            }
            else
            {
                var c = cam != null ? cam : Camera.main;
                if (c == null) { if (beam != null) beam.enabled = false; return; }
                Vector2 mp = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                ray = c.ScreenPointToRay(mp);
                clickDown = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            }

            S014KioskButton hit = null;
            Vector3 end = ray.origin + ray.direction * maxDistance;
            if (Physics.Raycast(ray, out var info, maxDistance, ~0, QueryTriggerInteraction.Collide))
            {
                hit = info.collider.GetComponentInParent<S014KioskButton>();
                end = info.point;
            }

            if (beam != null)
            {
                beam.enabled = true;
                beam.positionCount = 2;
                beam.SetPosition(0, ray.origin);
                beam.SetPosition(1, end);
            }

            if (hovered != hit)
            {
                if (hovered != null) hovered.SetHighlight(false);
                hovered = hit;
                if (hovered != null) hovered.SetHighlight(true);
            }

            if (hit != null && clickDown) hit.Click();
        }
    }
}
