using System;
using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.PlayerLife
{
    [DisallowMultipleComponent]
    public sealed class CheckpointResettable : MonoBehaviour,
        ICheckpointStateParticipant
    {
        [SerializeField] private bool captureTransform = true;
        [SerializeField] private bool captureActiveState = true;
        [SerializeField] private bool captureRigidbody = true;
        [SerializeField] private bool captureAnimators = true;
        [SerializeField] private bool captureAudioSources = true;
        [SerializeField] private UnityEvent onCheckpointRestored;

        private bool saved;
        private bool savedActive;
        private Vector3 savedPosition;
        private Quaternion savedRotation;
        private Vector3 savedScale;
        private Rigidbody body;
        private RigidbodySnapshot bodySnapshot;
        private Animator[] animators;
        private AnimatorSnapshot[] animatorSnapshots;
        private AudioSource[] audioSources;
        private AudioSnapshot[] audioSnapshots;

        public void CaptureCheckpointState()
        {
            saved = true;
            savedActive = gameObject.activeSelf;
            savedPosition = transform.position;
            savedRotation = transform.rotation;
            savedScale = transform.localScale;

            body = captureRigidbody ? GetComponent<Rigidbody>() : null;
            if (body != null)
            {
                bodySnapshot = new RigidbodySnapshot(body);
            }

            animators = captureAnimators
                ? GetComponentsInChildren<Animator>(true)
                : Array.Empty<Animator>();
            animatorSnapshots = new AnimatorSnapshot[animators.Length];
            for (int i = 0; i < animators.Length; i++)
            {
                animatorSnapshots[i] = new AnimatorSnapshot(animators[i]);
            }

            audioSources = captureAudioSources
                ? GetComponentsInChildren<AudioSource>(true)
                : Array.Empty<AudioSource>();
            audioSnapshots = new AudioSnapshot[audioSources.Length];
            for (int i = 0; i < audioSources.Length; i++)
            {
                audioSnapshots[i] = new AudioSnapshot(audioSources[i]);
            }
        }

        public void RestoreCheckpointState()
        {
            if (!saved)
            {
                return;
            }

            if (captureActiveState && !gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (captureTransform)
            {
                transform.SetPositionAndRotation(
                    savedPosition,
                    savedRotation);
                transform.localScale = savedScale;
            }

            if (body != null)
            {
                bodySnapshot.Restore(body);
            }

            for (int i = 0; i < animators.Length; i++)
            {
                animatorSnapshots[i].Restore(animators[i]);
            }

            for (int i = 0; i < audioSources.Length; i++)
            {
                audioSnapshots[i].Restore(audioSources[i]);
            }

            onCheckpointRestored?.Invoke();
            if (captureActiveState && !savedActive)
            {
                gameObject.SetActive(false);
            }
        }

        private readonly struct RigidbodySnapshot
        {
            private readonly Vector3 velocity;
            private readonly Vector3 angularVelocity;
            private readonly bool isKinematic;
            private readonly bool useGravity;

            public RigidbodySnapshot(Rigidbody target)
            {
                velocity = target.linearVelocity;
                angularVelocity = target.angularVelocity;
                isKinematic = target.isKinematic;
                useGravity = target.useGravity;
            }

            public void Restore(Rigidbody target)
            {
                target.isKinematic = isKinematic;
                target.useGravity = useGravity;
                if (!target.isKinematic)
                {
                    target.linearVelocity = velocity;
                    target.angularVelocity = angularVelocity;
                }
            }
        }

        private readonly struct AnimatorSnapshot
        {
            private readonly bool enabled;
            private readonly int stateHash;
            private readonly float normalizedTime;

            public AnimatorSnapshot(Animator target)
            {
                enabled = target != null && target.enabled;
                if (target != null && target.layerCount > 0 &&
                    target.isActiveAndEnabled)
                {
                    AnimatorStateInfo state =
                        target.GetCurrentAnimatorStateInfo(0);
                    stateHash = state.fullPathHash;
                    normalizedTime = state.normalizedTime;
                }
                else
                {
                    stateHash = 0;
                    normalizedTime = 0f;
                }
            }

            public void Restore(Animator target)
            {
                if (target == null)
                {
                    return;
                }

                target.enabled = enabled;
                if (enabled && stateHash != 0)
                {
                    target.Play(stateHash, 0, normalizedTime);
                    target.Update(0f);
                }
            }
        }

        private readonly struct AudioSnapshot
        {
            private readonly bool wasPlaying;
            private readonly float time;

            public AudioSnapshot(AudioSource target)
            {
                wasPlaying = target != null && target.isPlaying;
                time = target != null ? target.time : 0f;
            }

            public void Restore(AudioSource target)
            {
                if (target == null || target.clip == null)
                {
                    return;
                }

                target.Stop();
                target.time = Mathf.Clamp(time, 0f, target.clip.length);
                if (wasPlaying)
                {
                    target.Play();
                }
            }
        }
    }
}
