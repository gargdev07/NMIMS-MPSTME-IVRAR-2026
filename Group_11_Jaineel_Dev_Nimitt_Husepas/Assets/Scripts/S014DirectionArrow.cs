using UnityEngine;

namespace S014.MobileAR
{
    public class S014DirectionArrow : MonoBehaviour, IS014NavigationGuidance, IS014PoseDrivenGuidance
    {
        [SerializeField, Range(0.05f, 1f)] private float smoothing = 0.25f;
        [SerializeField] private float heightOffset = 0.2f;
        [SerializeField] private float minimumScale = 0.65f;
        [SerializeField] private float maximumScale = 1.1f;
        private S014Waypoint target;
        private Vector3 userPosition;
        private Quaternion userRotation = Quaternion.identity;
        private bool hasPose;
        private bool hasSmoothedRotation;
        private Quaternion smoothedRotation;

        public void SetPose(Vector3 position, Quaternion rotation) { userPosition = position; userRotation = rotation; hasPose = true; }
        public void SetTarget(S014Waypoint waypoint) { target = waypoint; gameObject.SetActive(waypoint != null); }
        public void ClearTarget() { target = null; gameObject.SetActive(false); }
        private void Awake() { EnsureVisual(); }
        private void Update()
        {
            if (target == null || !hasPose) return;
            var delta = target.worldPosition - userPosition; delta.y = 0f;
            if (delta.sqrMagnitude < 0.01f) return;
            var desired = Quaternion.LookRotation(delta.normalized, Vector3.up);
            smoothedRotation = hasSmoothedRotation ? Quaternion.Slerp(smoothedRotation, desired, smoothing) : desired;
            hasSmoothedRotation = true;
            transform.SetPositionAndRotation(userPosition + Vector3.up * heightOffset, smoothedRotation);
            var scale = Mathf.Clamp(0.55f + delta.magnitude * 0.03f, minimumScale, maximumScale);
            transform.localScale = new Vector3(scale, scale, scale);
        }
        private void EnsureVisual()
        {
            if (transform.childCount > 0) return;
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder); shaft.name = "ArrowShaft"; shaft.transform.SetParent(transform, false); shaft.transform.localPosition = new Vector3(0f, 0f, 0.28f); shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); shaft.transform.localScale = new Vector3(0.08f, 0.28f, 0.08f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder); head.name = "ArrowHead"; head.transform.SetParent(transform, false); head.transform.localPosition = new Vector3(0f, 0f, 0.72f); head.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); head.transform.localScale = new Vector3(0.26f, 0.22f, 0.26f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(0.1f, 0.8f, 1f, 1f); shaft.GetComponent<Renderer>().sharedMaterial = material; head.GetComponent<Renderer>().sharedMaterial = material;
            DestroyCollider(shaft); DestroyCollider(head);
        }
        private static void DestroyCollider(GameObject obj)
        {
            var collider = obj.GetComponent<Collider>();
            if (collider == null) return;
#if UNITY_EDITOR
            Object.DestroyImmediate(collider);
#else
            Object.Destroy(collider);
#endif
        }
    }
}
