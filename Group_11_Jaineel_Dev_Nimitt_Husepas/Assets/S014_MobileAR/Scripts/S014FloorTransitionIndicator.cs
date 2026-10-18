using UnityEngine;

namespace S014.MobileAR
{
    public class S014FloorTransitionIndicator : MonoBehaviour, IS014PoseDrivenGuidance
    {
        private S014Waypoint currentWaypoint;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle instructionStyle;

        public void SetPose(Vector3 position, Quaternion rotation) { }

        public void Show(S014Waypoint waypoint)
        {
            currentWaypoint = waypoint;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            currentWaypoint = null;
            gameObject.SetActive(false);
        }

        private void OnGUI()
        {
            if (!gameObject.activeInHierarchy || currentWaypoint == null) return;
            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box);
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.8f, 0.2f) }
                };
                instructionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    wordWrap = false,
                    normal = { textColor = Color.white }
                };
            }

            var width = Mathf.Min(440f, Screen.width - 32f);
            var panel = new Rect((Screen.width - width) * 0.5f, 18f, width, 64f);
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 4f, panel.width - 16f, 26f),
                FormatFloor(currentWaypoint.floor) + "  >  " +
                FormatFloor(string.IsNullOrEmpty(currentWaypoint.destinationFloor) ? "next level" : currentWaypoint.destinationFloor),
                titleStyle);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 32f, panel.width - 16f, 24f),
                currentWaypoint.instruction, instructionStyle);
        }

        private static string FormatFloor(string floor)
        {
            if (string.Equals(floor, "Ground", System.StringComparison.OrdinalIgnoreCase)) return "GROUND FLOOR";
            if (string.Equals(floor, "First", System.StringComparison.OrdinalIgnoreCase)) return "FIRST FLOOR";
            return floor.ToUpperInvariant();
        }
    }
}
