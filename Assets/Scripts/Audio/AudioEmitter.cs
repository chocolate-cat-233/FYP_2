using UnityEngine;

namespace SuperFinalFuse.AudioSystem
{
    public sealed class AudioEmitter : MonoBehaviour
    {
        [SerializeField] private AudioDefinition definition;

        public void Play()
        {
            if (definition == null)
            {
                Debug.LogWarning(
                    $"{nameof(AudioEmitter)} on '{name}' has no " +
                    $"{nameof(AudioDefinition)} assigned.",
                    this);
                return;
            }

            AudioManager.Instance.Play(definition, transform.position);
        }
    }
}
