using UnityEngine;
using UnityEngine.InputSystem;

namespace S014.CampusVR
{
    /// <summary>
    /// Free-roam locomotion for the XR rig: left stick walks relative to head
    /// facing, right stick snap-turns, gravity keeps the rig on the floor and
    /// lets it climb the campus ramps. Keyboard (WASD/arrows + Q/E) mirrors the
    /// sticks so the demo can be driven in the editor without a headset.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class S014CampusFreeLocomotion : MonoBehaviour
    {
        public Transform head;
        [Min(0.2f)] public float moveSpeed = 2.6f;
        public float snapAngle = 30f;
        public float snapCooldown = 0.3f;
        public float gravity = -9.81f;

        public float lookSensitivity = 0.12f;

        private CharacterController cc;
        private float vy;
        private float lastSnap;
        private bool snapLatched;
        private float pitch;

        private void Awake() { cc = GetComponent<CharacterController>(); }

        private void Update()
        {
            Vector2 move = S014VRInput.LeftStick();
            float turn = S014VRInput.RightStick().x;

            var kb = Keyboard.current;
            if (kb != null)
            {
                float kx = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float ky = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                         - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                if (Mathf.Abs(kx) + Mathf.Abs(ky) > 0.01f) move = new Vector2(kx, ky);
                if (kb.qKey.isPressed) turn = -1f;
                if (kb.eKey.isPressed) turn = 1f;
            }

            if (move.sqrMagnitude < 0.03f) move = Vector2.zero;

            Transform f = head != null ? head : transform;
            Vector3 fwd = f.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = f.right; right.y = 0f; right.Normalize();
            Vector3 dir = right * move.x + fwd * move.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += gravity * Time.deltaTime;

            cc.Move((dir * moveSpeed + Vector3.up * vy) * Time.deltaTime);

            if (Mathf.Abs(turn) > 0.7f)
            {
                if (!snapLatched && Time.time - lastSnap > snapCooldown)
                {
                    transform.Rotate(0f, snapAngle * Mathf.Sign(turn), 0f, Space.World);
                    lastSnap = Time.time;
                    snapLatched = true;
                }
            }
            else snapLatched = false;

#if UNITY_EDITOR
            // Desktop look: hold right mouse button and move the mouse (no headset).
            var mouse = Mouse.current;
            if (mouse != null && head != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                transform.Rotate(0f, d.x * lookSensitivity, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - d.y * lookSensitivity, -80f, 80f);
                var e = head.localEulerAngles;
                head.localEulerAngles = new Vector3(pitch, e.y, e.z);
            }
#endif
        }
    }
}
