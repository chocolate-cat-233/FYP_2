using UnityEngine;

namespace SuperFinalFuse.AudioSystem
{
    internal sealed class AudioVoice : MonoBehaviour
    {
        private AudioSource audioSource;

        internal AudioDefinition CurrentDefinition { get; private set; }
        internal bool IsPlaying =>
            audioSource != null && audioSource.isPlaying;

        internal void Initialize(AudioSource source)
        {
            audioSource = source;
        }

        internal bool IsPlayingDefinition(AudioDefinition definition)
        {
            return IsPlaying && CurrentDefinition == definition;
        }

        internal void Play(
            AudioDefinition definition,
            Vector3 worldPosition)
        {
            CurrentDefinition = definition;

            audioSource.clip = definition.Clip;
            audioSource.outputAudioMixerGroup =
                definition.OutputMixerGroup;
            audioSource.volume = definition.Volume;
            audioSource.pitch = definition.Pitch;
            audioSource.loop = false;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = definition.Is3D ? 1f : 0f;

            if (definition.Is3D)
            {
                transform.position = worldPosition;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.minDistance = definition.MinDistance;
                audioSource.maxDistance = definition.MaxDistance;
            }
            else
            {
                transform.localPosition = Vector3.zero;
            }

            audioSource.Play();
        }
    }
}
