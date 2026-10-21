using LastBell.Run;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Menu
{
    // Reuse the project's GPU particle batches across menu visits. Inactive outside the menu;
    // separate from the run's persistent FX and never attached to a CombatWorld.
    public sealed class MenuFxLifetime : MonoBehaviour
    {
        static MenuFxLifetime instance;
        public PlayerFx Player { get; private set; }
        public BellkeeperFx Boss { get; private set; }
        public static MenuFxLifetime Acquire()
        {
            if (!instance)
            {
                var root = new GameObject("Menu spell FX (presentation only)"); DontDestroyOnLoad(root);
                instance = root.AddComponent<MenuFxLifetime>();
                instance.Player = PlayerFx.Create(root.transform);
                instance.Boss = BellkeeperFx.Create(); instance.Boss.transform.SetParent(root.transform, false);
            }
            instance.gameObject.SetActive(true); return instance;
        }
        public void Release()
        { Player.ResetAll(); Boss.ResetAll(); gameObject.SetActive(false); }
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
