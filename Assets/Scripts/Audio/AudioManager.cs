using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.AudioSystem
{
    [DefaultExecutionOrder(-100)]
    public sealed class AudioManager : MonoBehaviour
    {
        private const int InitialPoolSize = 16;
        private const int MaxPoolSize = 32;

        private static AudioManager instance;

        private readonly List<AudioVoice> voices =
            new List<AudioVoice>(MaxPoolSize);

        public static AudioManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<AudioManager>();

                    if (instance == null)
                    {
                        GameObject managerObject =
                            new GameObject("[AudioManager]");
                        instance = managerObject.AddComponent<AudioManager>();
                    }
                    else
                    {
                        instance.EnsureInitialPool();
                    }
                }

                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureInitialPool();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public bool Play(AudioDefinition definition)
        {
            if (definition != null && definition.Is3D)
            {
                Debug.LogWarning(
                    $"'{definition.name}' is a 3D sound and requires a " +
                    "world position.",
                    definition);
                return false;
            }

            return PlayInternal(definition, Vector3.zero);
        }

        public bool Play(
            AudioDefinition definition,
            Vector3 worldPosition)
        {
            return PlayInternal(definition, worldPosition);
        }

        private bool PlayInternal(
            AudioDefinition definition,
            Vector3 worldPosition)
        {
            if (definition == null)
            {
                Debug.LogWarning(
                    $"{nameof(AudioManager)} cannot play a null " +
                    $"{nameof(AudioDefinition)}.",
                    this);
                return false;
            }

            if (definition.Clip == null)
            {
                Debug.LogWarning(
                    $"'{definition.name}' has no AudioClip assigned.",
                    definition);
                return false;
            }

            if (CountPlaying(definition) >= definition.MaxConcurrent)
            {
                return false;
            }

            AudioVoice voice = GetAvailableVoice();

            if (voice == null)
            {
                return false;
            }

            voice.Play(definition, worldPosition);
            return true;
        }

        private int CountPlaying(AudioDefinition definition)
        {
            int playingCount = 0;

            for (int i = 0; i < voices.Count; i++)
            {
                AudioVoice voice = voices[i];

                if (voice != null &&
                    voice.IsPlayingDefinition(definition))
                {
                    playingCount++;
                }
            }

            return playingCount;
        }

        private AudioVoice GetAvailableVoice()
        {
            EnsureInitialPool();

            for (int i = 0; i < voices.Count; i++)
            {
                AudioVoice voice = voices[i];

                if (voice != null && !voice.IsPlaying)
                {
                    return voice;
                }
            }

            return voices.Count < MaxPoolSize
                ? CreateVoice()
                : null;
        }

        private void EnsureInitialPool()
        {
            while (voices.Count < InitialPoolSize)
            {
                CreateVoice();
            }
        }

        private AudioVoice CreateVoice()
        {
            GameObject voiceObject = new GameObject(
                $"Audio Voice {voices.Count + 1:00}");
            voiceObject.transform.SetParent(transform, false);

            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;

            AudioVoice voice = voiceObject.AddComponent<AudioVoice>();
            voice.Initialize(source);
            voices.Add(voice);

            return voice;
        }
    }
}
