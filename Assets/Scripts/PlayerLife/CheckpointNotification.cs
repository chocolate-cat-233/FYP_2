using UnityEngine;
using UnityEngine.UI;

namespace SuperFinalFuse.PlayerLife
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class CheckpointNotification : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float displayDuration = 2f;

        private GameObject panel;
        private Font notificationFont;
        private bool ownsFont;
        private float hideAt;

        public bool IsVisible => panel != null && panel.activeSelf;

        private void OnEnable()
        {
            PlayerCheckpoint.Activated += HandleCheckpointActivated;
        }

        private void OnDisable()
        {
            PlayerCheckpoint.Activated -= HandleCheckpointActivated;
            Hide();
        }

        private void HandleCheckpointActivated(PlayerCheckpoint checkpoint)
        {
            Show();
        }

        private void Show()
        {
            if (panel == null)
            {
                CreatePanel();
            }

            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            hideAt = Time.unscaledTime + Mathf.Max(0.1f, displayDuration);
        }

        private void Update()
        {
            if (IsVisible && Time.unscaledTime >= hideAt)
            {
                Hide();
            }
        }

        private void Hide()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void CreatePanel()
        {
            panel = new GameObject("Checkpoint Saved Notification", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -64f);
            rect.sizeDelta = new Vector2(460f, 64f);

            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.06f, 0.1f, 0.08f, 0.9f);
            background.raycastTarget = false;

            var textObject = new GameObject("Message", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(panel.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 8f);
            textRect.offsetMax = new Vector2(-16f, -8f);

            // OS fonts avoid adding a new font asset to this Windows-only build.
            notificationFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" }, 28);
            ownsFont = notificationFont != null;
            if (notificationFont == null)
            {
                notificationFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            Text message = textObject.GetComponent<Text>();
            message.font = notificationFont;
            message.fontSize = 28;
            message.alignment = TextAnchor.MiddleCenter;
            message.color = Color.white;
            message.raycastTarget = false;
            const string chineseMessage = "检查点已保存";
            bool supportsChinese = true;
            foreach (char character in chineseMessage)
            {
                supportsChinese &= notificationFont.HasCharacter(character);
            }
            message.text = supportsChinese ? chineseMessage : "Checkpoint saved";
        }

        private void OnDestroy()
        {
            if (panel != null)
            {
                Destroy(panel);
            }
            if (ownsFont && notificationFont != null)
            {
                Destroy(notificationFont);
            }
        }
    }
}
