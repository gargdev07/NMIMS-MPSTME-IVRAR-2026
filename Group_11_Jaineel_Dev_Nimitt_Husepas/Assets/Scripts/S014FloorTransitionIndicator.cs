using UnityEngine;

namespace S014.MobileAR
{
    public class S014FloorTransitionIndicator : MonoBehaviour, IS014PoseDrivenGuidance
    {
        [SerializeField] private float height = 1.8f;
        private TextMesh label;
        private Vector3 userPosition;
        private Quaternion userRotation = Quaternion.identity;
        private bool hasPose;
        public void SetPose(Vector3 position, Quaternion rotation) { userPosition = position; userRotation = rotation; hasPose = true; }
        public void Show(S014Waypoint waypoint)
        {
            EnsureLabel();
            label.text = "FLOOR TRANSITION\n" + waypoint.floor + " -> " + (string.IsNullOrEmpty(waypoint.destinationFloor) ? "next level" : waypoint.destinationFloor) + "\n" + waypoint.instruction;
            gameObject.SetActive(true);
        }
        public void Hide() { gameObject.SetActive(false); }
        private void LateUpdate()
        {
            if (!gameObject.activeSelf || !hasPose) return;
            transform.position = userPosition + Vector3.up * height + userRotation * Vector3.forward * 1.2f;
            var camera = Camera.main;
            if (camera != null) transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
        }
        private void EnsureLabel()
        {
            if (label != null) return;
            label = GetComponent<TextMesh>(); if (label == null) label = gameObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.characterSize = 0.06f; label.fontSize = 48; label.color = new Color(1f, 0.8f, 0.2f);
        }
    }
}
