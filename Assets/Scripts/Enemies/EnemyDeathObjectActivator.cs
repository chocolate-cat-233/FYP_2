using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.Enemies
{
    [DisallowMultipleComponent]
    public sealed class EnemyDeathObjectActivator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ChargingEnemyController[] enemies =
            Array.Empty<ChargingEnemyController>();
        [SerializeField] private GameObject targetToEnable;

        private readonly HashSet<ChargingEnemyController> trackedEnemies =
            new HashSet<ChargingEnemyController>();
        private bool activated;

        private void Awake()
        {
            CacheTrackedEnemies();
            WarnAboutInvalidConfiguration();
        }

        private void OnEnable()
        {
            SubscribeToEnemyDeaths();
            TryActivateTarget();
        }

        private void OnDisable()
        {
            UnsubscribeFromEnemyDeaths();
        }

        private void CacheTrackedEnemies()
        {
            trackedEnemies.Clear();
            if (enemies == null)
            {
                return;
            }

            foreach (ChargingEnemyController enemy in enemies)
            {
                if (enemy != null)
                {
                    trackedEnemies.Add(enemy);
                }
            }
        }

        private void SubscribeToEnemyDeaths()
        {
            foreach (ChargingEnemyController enemy in trackedEnemies)
            {
                if (enemy != null)
                {
                    enemy.Died -= HandleEnemyDied;
                    enemy.Died += HandleEnemyDied;
                }
            }
        }

        private void UnsubscribeFromEnemyDeaths()
        {
            foreach (ChargingEnemyController enemy in trackedEnemies)
            {
                if (enemy != null)
                {
                    enemy.Died -= HandleEnemyDied;
                }
            }
        }

        private void HandleEnemyDied()
        {
            TryActivateTarget();
        }

        private void TryActivateTarget()
        {
            if (activated || targetToEnable == null ||
                trackedEnemies.Count == 0)
            {
                return;
            }

            foreach (ChargingEnemyController enemy in trackedEnemies)
            {
                if (enemy != null &&
                    enemy.State != ChargingEnemyState.Dead)
                {
                    return;
                }
            }

            activated = true;
            targetToEnable.SetActive(true);
            UnsubscribeFromEnemyDeaths();
        }

        private void WarnAboutInvalidConfiguration()
        {
            if (trackedEnemies.Count > 0 && targetToEnable != null &&
                targetToEnable != gameObject)
            {
                return;
            }

            Debug.LogWarning(
                $"{nameof(EnemyDeathObjectActivator)} on '{name}' requires " +
                "at least one enemy and a separate target object.",
                this);
        }
    }
}
