using UnityEngine;

namespace S014.MobileAR
{
    public sealed class S014InteriorRevealSwitch : MonoBehaviour
    {
        [SerializeField] private GameObject interiorRoot;
        [SerializeField] private Renderer selectedExteriorRenderer;
        [SerializeField] private Renderer[] additionalExteriorRenderers;
        [SerializeField] private bool revealMode;
        private bool[] originalAdditionalEnabled;
        private bool originalInteriorActive;
        private bool originalSelectedEnabled;
        private bool originalStatesCaptured;

        public bool RevealMode => revealMode;

        public void SetRevealMode(bool enabled)
        {
            CaptureOriginalStates();
            revealMode = enabled;
            Apply();
        }

        private void Awake()
        {
            CaptureOriginalStates();
            Apply();
        }

        private void CaptureOriginalStates()
        {
            if (originalStatesCaptured) return;
            originalInteriorActive = interiorRoot != null && interiorRoot.activeSelf;
            originalSelectedEnabled = selectedExteriorRenderer != null && selectedExteriorRenderer.enabled;
            originalAdditionalEnabled = new bool[additionalExteriorRenderers == null ? 0 : additionalExteriorRenderers.Length];
            for (var i = 0; i < originalAdditionalEnabled.Length; i++)
            {
                var renderer = additionalExteriorRenderers[i];
                originalAdditionalEnabled[i] = renderer != null && renderer.enabled;
            }
            originalStatesCaptured = true;
        }

        private void Apply()
        {
            if (interiorRoot != null) interiorRoot.SetActive(revealMode || originalInteriorActive);
            if (selectedExteriorRenderer != null)
                selectedExteriorRenderer.enabled = revealMode ? false : originalSelectedEnabled;
            if (additionalExteriorRenderers == null) return;
            for (var i = 0; i < additionalExteriorRenderers.Length; i++)
            {
                var renderer = additionalExteriorRenderers[i];
                if (renderer != null)
                    renderer.enabled = revealMode ? false :
                        (i < originalAdditionalEnabled.Length && originalAdditionalEnabled[i]);
            }
        }
    }
}
