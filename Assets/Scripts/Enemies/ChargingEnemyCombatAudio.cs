using SuperFinalFuse.AudioSystem;
using UnityEngine;

namespace SuperFinalFuse.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(
        typeof(ChargingEnemyController),
        typeof(AudioSource))]
    public sealed class ChargingEnemyCombatAudio : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioDefinition combatAudio;
        [Min(0f)]
        [SerializeField] private float fadeDuration = 0.5f;

        private ChargingEnemyController enemyController;
        private AudioSource audioSource;
        private float audibleVolume;
        private bool hasStarted;
        private bool enemyDied;
        private bool isConfigured;
        private bool warningLogged;

        private void Awake()
        {
            enemyController = GetComponent<ChargingEnemyController>();
            audioSource = GetComponent<AudioSource>();
            isConfigured = TryConfigureAudioSource();
        }

        private void OnEnable()
        {
            if (enemyController == null)
            {
                enemyController = GetComponent<ChargingEnemyController>();
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (enemyController != null)
            {
                enemyController.Died += HandleEnemyDied;
            }

            if (!isConfigured && !enemyDied)
            {
                isConfigured = TryConfigureAudioSource();
            }
        }

        private void Update()
        {
            if (!isConfigured || enemyDied)
            {
                return;
            }

            bool shouldBeAudible =
                enemyController.IsPlayerInsideAttackArea;
            if (!hasStarted)
            {
                if (!shouldBeAudible)
                {
                    return;
                }

                StartPlayback();
            }
            else if (!audioSource.isPlaying)
            {
                if (!shouldBeAudible)
                {
                    return;
                }

                StartPlayback();
            }

            float targetVolume = shouldBeAudible
                ? audibleVolume
                : 0f;
            ApplyVolume(targetVolume);
        }

        private void OnDisable()
        {
            if (enemyController != null)
            {
                enemyController.Died -= HandleEnemyDied;
            }

            if (!enemyDied && audioSource != null && audioSource.isPlaying)
            {
                audioSource.volume = 0f;
            }
        }

        private void OnDestroy()
        {
            if (enemyController != null)
            {
                enemyController.Died -= HandleEnemyDied;
            }

            StopPlayback();
        }

        private bool TryConfigureAudioSource()
        {
            if (enemyController == null)
            {
                return LogConfigurationWarning(
                    "requires a ChargingEnemyController component.");
            }

            if (audioSource == null)
            {
                return LogConfigurationWarning(
                    "requires an AudioSource component.");
            }

            if (combatAudio == null)
            {
                return LogConfigurationWarning(
                    "requires a Combat Audio definition.");
            }

            if (combatAudio.Clip == null)
            {
                return LogConfigurationWarning(
                    "requires the Combat Audio definition to contain a clip.");
            }

            audibleVolume = Mathf.Clamp01(combatAudio.Volume);
            audioSource.clip = combatAudio.Clip;
            audioSource.outputAudioMixerGroup =
                combatAudio.OutputMixerGroup;
            audioSource.pitch = combatAudio.Pitch;
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.spatialize = false;
            audioSource.spatializePostEffects = false;
            audioSource.volume = 0f;
            return true;
        }

        private void StartPlayback()
        {
            audioSource.volume = fadeDuration <= 0f
                ? audibleVolume
                : 0f;
            audioSource.Play();
            hasStarted = true;
        }

        private void ApplyVolume(float targetVolume)
        {
            if (fadeDuration <= 0f)
            {
                audioSource.volume = targetVolume;
                return;
            }

            float volumeChangePerSecond = audibleVolume / fadeDuration;
            audioSource.volume = Mathf.MoveTowards(
                audioSource.volume,
                targetVolume,
                volumeChangePerSecond * Time.deltaTime);
        }

        private void HandleEnemyDied()
        {
            enemyDied = true;
            StopPlayback();
        }

        private void StopPlayback()
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.Stop();
            audioSource.volume = 0f;
            hasStarted = false;
        }

        private bool LogConfigurationWarning(string message)
        {
            if (!warningLogged)
            {
                Debug.LogWarning(
                    $"{nameof(ChargingEnemyCombatAudio)} on '{name}' " +
                    message,
                    this);
                warningLogged = true;
            }

            return false;
        }

        private void OnValidate()
        {
            fadeDuration = Mathf.Max(0f, fadeDuration);
        }
    }
}
