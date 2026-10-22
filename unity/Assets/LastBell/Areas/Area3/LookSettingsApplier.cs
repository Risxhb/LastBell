using UnityEngine;

namespace LastBell.Areas.Area3
{
    // Pushes a LookSettings asset to the GPU in edit and play mode, so tweaking the asset updates the scene live.
    [ExecuteAlways]
    public sealed class LookSettingsApplier : MonoBehaviour
    {
        public LookSettings settings;
        public Light sun;

        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void Update() => Apply();

        public void Apply() { if (settings) settings.SendToGPU(sun); }
    }
}
