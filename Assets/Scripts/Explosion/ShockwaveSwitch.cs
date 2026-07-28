using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.Explosion
{
    public enum ShockwaveSwitchMode
    {
        ActivateOnly,
        DeactivateOnly,
        Toggle
    }

    [DisallowMultipleComponent]
    public sealed class ShockwaveSwitch : MonoBehaviour, IShockwaveReactive
    {
        [SerializeField] private ShockwaveSwitchMode mode =
            ShockwaveSwitchMode.Toggle;
        [SerializeField] private bool isActive;
        [SerializeField] private UnityEvent onActivated;
        [SerializeField] private UnityEvent onDeactivated;

        public bool IsActive => isActive;

        public void OnShockwaveHit(ShockwaveHit hit)
        {
            switch (mode)
            {
                case ShockwaveSwitchMode.ActivateOnly:
                    SetActive(true);
                    break;
                case ShockwaveSwitchMode.DeactivateOnly:
                    SetActive(false);
                    break;
                case ShockwaveSwitchMode.Toggle:
                    SetActive(!isActive);
                    break;
            }
        }

        public void SetActive(bool value)
        {
            if (isActive == value)
            {
                return;
            }

            isActive = value;
            if (isActive)
            {
                onActivated?.Invoke();
            }
            else
            {
                onDeactivated?.Invoke();
            }
        }
    }
}
