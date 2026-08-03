using SuperFinalFuse.BombThrowing.Buffs;
using UnityEditor;
using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Editor
{
    public sealed class BombBuffRuntimeWindow : EditorWindow
    {
        private BombBuffController target;
        private BombBuffType newBuffType = BombBuffType.TeleportBomb;
        private int newBuffStacks = 1;
        private float newBuffDuration = 30f;
        private bool newBuffInfinite = true;

        [MenuItem("Tools/Super Final Fuse/Bomb Buff Runtime")]
        private static void Open()
        {
            GetWindow<BombBuffRuntimeWindow>("Bomb Buff Runtime");
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            target = null;
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying && target == null)
            {
                FindRuntimePlayer();
            }

            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Runtime Bomb Buff Tester",
                EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect and edit the player's " +
                    "runtime buffs.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            target = (BombBuffController)EditorGUILayout.ObjectField(
                "Player Buff Controller",
                target,
                typeof(BombBuffController),
                true);
            if (GUILayout.Button("Find Player", GUILayout.Width(90f)))
            {
                FindRuntimePlayer();
            }
            EditorGUILayout.EndHorizontal();

            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "No runtime BombBuffController found. Ensure the Player " +
                    "has an enabled BombThrower.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Bomb Blast Immunity",
                target.IsBombBlastImmune
                    ? $"{target.BombBlastImmunityRemaining:0.00}s"
                    : "Inactive");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Grant 1s Immunity"))
            {
                target.GrantBombBlastImmunity(1f);
            }
            if (GUILayout.Button("Clear Immunity"))
            {
                target.ClearBombBlastImmunity();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);
            DrawActiveBuffs();
            EditorGUILayout.Space(12f);
            DrawAddBuff();
        }

        private void DrawActiveBuffs()
        {
            EditorGUILayout.LabelField("Active Buffs", EditorStyles.boldLabel);
            var buffs = target.ActiveBuffs;
            if (buffs.Count == 0)
            {
                EditorGUILayout.HelpBox("No active buffs.", MessageType.None);
            }

            for (int i = buffs.Count - 1; i >= 0; i--)
            {
                BombBuffRuntimeState state = buffs[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(state.Type.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Stacks", state.Stacks.ToString());
                EditorGUILayout.LabelField(
                    "Duration",
                    state.InfiniteDuration
                        ? "Infinite"
                        : $"{state.RemainingDuration:0.00}s");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+1 Stack"))
                {
                    target.AddStacks(state.Type);
                }
                if (GUILayout.Button("-1 Stack"))
                {
                    target.RemoveStacks(state.Type);
                }
                if (GUILayout.Button("Remove"))
                {
                    target.RemoveBuff(state.Type);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            GUI.enabled = buffs.Count > 0;
            if (GUILayout.Button("Clear All Buffs"))
            {
                target.ClearAllBuffs();
            }
            GUI.enabled = true;
        }

        private void DrawAddBuff()
        {
            EditorGUILayout.LabelField("Add / Refresh Buff", EditorStyles.boldLabel);
            newBuffType = (BombBuffType)EditorGUILayout.EnumPopup(
                "Buff Type",
                newBuffType);
            newBuffStacks = EditorGUILayout.IntSlider(
                "Stacks",
                newBuffStacks,
                1,
                99);
            newBuffInfinite = EditorGUILayout.Toggle(
                "Infinite Duration",
                newBuffInfinite);
            using (new EditorGUI.DisabledScope(newBuffInfinite))
            {
                newBuffDuration = EditorGUILayout.FloatField(
                    "Duration (seconds)",
                    Mathf.Max(0.01f, newBuffDuration));
            }

            if (GUILayout.Button("Add Buff", GUILayout.Height(30f)))
            {
                target.AddBuff(
                    newBuffType,
                    newBuffStacks,
                    newBuffDuration,
                    newBuffInfinite);
            }
        }

        private void FindRuntimePlayer()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            target = player != null
                ? player.GetComponent<BombBuffController>()
                : Object.FindFirstObjectByType<BombBuffController>();
        }
    }
}
