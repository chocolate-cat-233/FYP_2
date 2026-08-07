using UnityEngine;
using UnityEngine.InputSystem;

namespace SuperFinalFuse.BombThrowing
{
    /// <summary>
    /// Independent monitor that keeps running even if BombThrower is disabled.
    /// Remove it after the intermittent issue has been diagnosed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BombThrowDiagnostics : MonoBehaviour
    {
        [SerializeField] private BombThrower thrower;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private Key dumpReportKey = Key.F8;
        [SerializeField] private Key forceUnlockKey = Key.F9;

        private string lastReport = "Waiting for runtime data";
        private GUIStyle boxStyle;

        private void Awake()
        {
            if (thrower == null)
            {
                thrower = GetComponent<BombThrower>();
            }
        }

        private void Update()
        {
            if (Keyboard.current == null || thrower == null)
            {
                return;
            }

            if (Keyboard.current[dumpReportKey].wasPressedThisFrame)
            {
                lastReport = thrower.GetDiagnosticReport();
                Debug.Log(lastReport, thrower);
            }

            if (Keyboard.current[forceUnlockKey].wasPressedThisFrame)
            {
                Debug.LogWarning(
                    "Bomb diagnostics: F9 force unlock requested.",
                    thrower);
                thrower.ForceClearThrowLockForDiagnostics();
            }
        }

        private void OnGUI()
        {
            if (!showOverlay || thrower == null)
            {
                return;
            }

            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 14,
                    wordWrap = false
                };
                boxStyle.normal.textColor = Color.white;
            }

            string aimState = $"BombAim={thrower.IsAimInputHeld}";
            string text =
                "BOMB DIAGNOSTICS  [F8 dump] [F9 force unlock]\n" +
                $"thrower enabled={thrower.enabled}, active={thrower.gameObject.activeInHierarchy}\n" +
                $"canThrow={thrower.CanThrow}, inputBlocked={thrower.IsInputBlocked}\n" +
                $"lockId={thrower.ActiveBombInstanceId}, age={thrower.CurrentLockAge:0.00}s\n" +
                $"{aimState}, focus={Application.isFocused}\n" +
                $"last: {thrower.LastDiagnosticEvent}";

            GUI.Box(new Rect(10f, 10f, 610f, 125f), text, boxStyle);
        }
    }
}
