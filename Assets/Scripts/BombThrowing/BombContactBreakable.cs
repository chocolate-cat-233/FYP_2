using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing
{
    [DisallowMultipleComponent]
    public sealed class BombContactBreakable : MonoBehaviour,
        IBombDirectHitReceiver,
        ICheckpointStateParticipant
    {
        [SerializeField] private Renderer[] renderersToHide;
        [SerializeField] private Collider[] collidersToDisable;
        [SerializeField] private GameObject breakEffectPrefab;
        [Min(0f)]
        [SerializeField] private float effectLifetime = 3f;
        [SerializeField] private UnityEvent onBroken;

        public bool IsBroken { get; private set; }
        private bool savedIsBroken;
        private bool[] savedRendererStates;
        private bool[] savedColliderStates;
        private GameObject activeBreakEffect;

        private void Awake()
        {
            if (renderersToHide == null || renderersToHide.Length == 0)
            {
                renderersToHide = GetComponentsInChildren<Renderer>();
            }

            if (collidersToDisable == null || collidersToDisable.Length == 0)
            {
                collidersToDisable = GetComponentsInChildren<Collider>();
            }
        }

        public void Break(Vector3 contactPoint)
        {
            if (IsBroken)
            {
                return;
            }

            IsBroken = true;
            foreach (Renderer targetRenderer in renderersToHide)
            {
                if (targetRenderer != null) targetRenderer.enabled = false;
            }

            foreach (Collider targetCollider in collidersToDisable)
            {
                if (targetCollider != null) targetCollider.enabled = false;
            }

            if (breakEffectPrefab != null)
            {
                activeBreakEffect = Instantiate(
                    breakEffectPrefab,
                    contactPoint,
                    Quaternion.identity);
                if (effectLifetime > 0f)
                {
                    Destroy(activeBreakEffect, effectLifetime);
                }
            }

            onBroken?.Invoke();
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (IsBroken)
            {
                return BombHitResult.Ignore;
            }

            Break(hit.Point);
            return BombHitResult.Explode;
        }

        public void CaptureCheckpointState()
        {
            savedIsBroken = IsBroken;
            savedRendererStates = new bool[renderersToHide.Length];
            for (int i = 0; i < renderersToHide.Length; i++)
            {
                savedRendererStates[i] =
                    renderersToHide[i] != null && renderersToHide[i].enabled;
            }

            savedColliderStates = new bool[collidersToDisable.Length];
            for (int i = 0; i < collidersToDisable.Length; i++)
            {
                savedColliderStates[i] =
                    collidersToDisable[i] != null &&
                    collidersToDisable[i].enabled;
            }
        }

        public void RestoreCheckpointState()
        {
            IsBroken = savedIsBroken;
            if (activeBreakEffect != null)
            {
                Destroy(activeBreakEffect);
                activeBreakEffect = null;
            }

            for (int i = 0; i < renderersToHide.Length; i++)
            {
                if (renderersToHide[i] != null &&
                    savedRendererStates != null &&
                    i < savedRendererStates.Length)
                {
                    renderersToHide[i].enabled = savedRendererStates[i];
                }
            }

            for (int i = 0; i < collidersToDisable.Length; i++)
            {
                if (collidersToDisable[i] != null &&
                    savedColliderStates != null &&
                    i < savedColliderStates.Length)
                {
                    collidersToDisable[i].enabled = savedColliderStates[i];
                }
            }

            Physics.SyncTransforms();
        }
    }
}
