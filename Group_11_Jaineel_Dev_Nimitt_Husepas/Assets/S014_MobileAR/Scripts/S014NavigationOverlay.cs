using UnityEngine;

namespace S014.MobileAR
{
    public class S014NavigationOverlay : MonoBehaviour
    {
        [SerializeField] private S014NavigationController navigator;
        [SerializeField] private bool visible = true;
        private GUIStyle boxStyle;
        private void OnGUI()
        {
            if (!visible || navigator == null) return;
            if (boxStyle == null) boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(16, 16, 12, 12) };
            var waypoint = navigator.CurrentWaypoint;
            var state = navigator.State == S014NavigationState.Active ? "ACTIVE" : navigator.State.ToString().ToUpperInvariant();
            var text = "S014 MOBILE AR NAVIGATION\n" + state + "\n" +
                "Current waypoint: " + (waypoint == null ? "-" : (waypoint.index + 1) + " / " + navigator.Route.Count) + "\n" +
                "Instruction: " + (waypoint == null ? "-" : waypoint.instruction) + "\n" +
                "Distance: " + (waypoint == null ? "-" : navigator.CurrentDistanceMeters.ToString("0.0") + " m") + "\n" +
                "Floor: " + (waypoint == null ? "-" : waypoint.floor);
            GUI.Box(new Rect(20, 20, 340, 150), text, boxStyle);
        }
    }
}
