using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fyp.Tests
{
    public sealed class CheckpointNotificationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private Component manager;
        private Component notification;
        private Component checkpoint;
        private Collider playerCollider;

        // Predefined Assembly-CSharp cannot be referenced by an asmdef. Keep
        // this bridge local to tests instead of reorganizing the game's assemblies.
        private static Type GameType(string name)
        {
            Type type = Type.GetType("SuperFinalFuse.PlayerLife." + name + ", Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Missing game component: " + name);
            return type;
        }

        private GameObject CreateObject(string name)
        {
            var instance = new GameObject(name);
            objects.Add(instance);
            return instance;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameObject player = CreateObject("Test Player");
            playerCollider = player.AddComponent<BoxCollider>();
            manager = player.AddComponent(GameType("PlayerCheckpointManager"));

            GameObject canvas = CreateObject("Test Notification Canvas");
            canvas.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            notification = canvas.AddComponent(GameType("CheckpointNotification"));

            checkpoint = CreateCheckpoint("Checkpoint A");
            Physics.SyncTransforms();
            // Allow the actual manager Start method to save the initial pose.
            yield return null;
        }

        private Component CreateCheckpoint(string name)
        {
            GameObject instance = CreateObject(name);
            instance.transform.position = new Vector3(100f + objects.Count * 10f, 0f, 0f);
            instance.AddComponent<BoxCollider>();
            return instance.AddComponent(GameType("PlayerCheckpoint"));
        }

        private bool IsVisible => (bool)notification.GetType()
            .GetProperty("IsVisible").GetValue(notification);

        private void Enter(Component target)
        {
            // Inject a trigger callback deterministically. These tests cover
            // activation/notification behavior, not PhysX collision detection.
            playerCollider.transform.position = target.transform.position;
            Physics.SyncTransforms();
            target.SendMessage("OnTriggerEnter", playerCollider, SendMessageOptions.RequireReceiver);
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
            // The notification's Update must have a chance to run at the deadline.
            yield return null;
        }

        [UnityTest]
        public IEnumerator InitialSaveDoesNotShowNotification()
        {
            Assert.That((bool)manager.GetType().GetProperty("HasCheckpoint").GetValue(manager), Is.True);
            Assert.That(IsVisible, Is.False);
            yield return null;
            Assert.That(IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator ActivationShowsNotification()
        {
            Enter(checkpoint);
            Assert.That(IsVisible, Is.True);
            yield return null;
            Assert.That(IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator NotificationHidesAfterTwoSeconds()
        {
            Enter(checkpoint);
            Assert.That(IsVisible, Is.True);
            yield return WaitRealtime(2.3f);
            Assert.That(IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator NewCheckpointRestartsDisplayTimer()
        {
            Enter(checkpoint);
            yield return WaitRealtime(1.2f);
            Component next = CreateCheckpoint("Checkpoint B");
            Enter(next);
            yield return WaitRealtime(1.1f);
            Assert.That(IsVisible, Is.True, "A new checkpoint must restart the two-second timer.");
            yield return WaitRealtime(1.2f);
            Assert.That(IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator StayingInsideDoesNotRepeatNotification()
        {
            Enter(checkpoint);
            Assert.That(IsVisible, Is.True);
            float end = Time.realtimeSinceStartup + 2.3f;
            while (Time.realtimeSinceStartup < end)
            {
                checkpoint.SendMessage("OnTriggerStay", playerCollider, SendMessageOptions.RequireReceiver);
                yield return null;
            }
            yield return null;
            Assert.That(IsVisible, Is.False, "Repeated overlap callbacks must not renew the notification.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject instance in objects)
            {
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
            }
            objects.Clear();
            yield return null;
        }
    }
}
