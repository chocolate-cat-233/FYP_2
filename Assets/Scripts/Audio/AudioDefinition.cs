using UnityEngine;
using UnityEngine.Audio;

namespace SuperFinalFuse.AudioSystem
{
    [CreateAssetMenu(
        fileName = "NewAudioDefinition",
        menuName = "Audio/Audio Definition")]
    public sealed class AudioDefinition : ScriptableObject
    {
        [Header("Audio")]
        [SerializeField] private AudioClip clip;
        [SerializeField] private AudioMixerGroup outputMixerGroup;
        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;
        [Range(0.01f, 3f)]
        [SerializeField] private float pitch = 1f;

        [Header("Spatial Audio")]
        [SerializeField] private bool is3D;
        [Min(0f)]
        [SerializeField] private float minDistance = 1f;
        [Min(0f)]
        [SerializeField] private float maxDistance = 20f;

        [Header("Concurrency")]
        [Min(1)]
        [SerializeField] private int maxConcurrent = 4;

        public AudioClip Clip => clip;
        public AudioMixerGroup OutputMixerGroup => outputMixerGroup;
        public float Volume => volume;
        public float Pitch => pitch;
        public bool Is3D => is3D;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public int MaxConcurrent => maxConcurrent;

        private void OnValidate()
        {
            volume = Mathf.Clamp01(volume);
            pitch = Mathf.Clamp(pitch, 0.01f, 3f);
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            maxConcurrent = Mathf.Max(1, maxConcurrent);
        }
    }
}
