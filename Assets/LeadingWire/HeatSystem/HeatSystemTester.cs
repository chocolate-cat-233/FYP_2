using System.Collections;
using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 热管系统最小测试器。
    /// 挂到场景中的空物体上，拖入被测组件引用，右键运行测试。
    ///
    /// 测试链结构：
    ///   [热源] ──热管──→ [中继器节点] ──输出热管──→ [终端节点]
    ///
    /// 测试内容：
    ///   1. 断路阻断验证 — 有断路点时热管不流通
    ///   2. 连通验证 — 修复断路点后热量传播
    ///   3. 断开验证 — 重置断路点后热量断开
    ///   4. 与门验证 — 多输入与门的逻辑
    /// </summary>
    public class HeatSystemTester : MonoBehaviour
    {
        [Header("测试链引用")]
        [SerializeField, Tooltip("被测热源")]
        private HeatSource testSource;

        [SerializeField, Tooltip("热源下游的热管（可含断路点）")]
        private HeatLine sourceLine;

        [SerializeField, Tooltip("热管下游的中继器节点")]
        private HeatNode repeaterNode;

        [SerializeField, Tooltip("节点下游的输出热管（无断路点）")]
        private HeatLine outputLine;

        [Header("与门测试（可选）")]
        [SerializeField, Tooltip("第二个热源（用于与门测试）")]
        private HeatSource testSource2;

        [SerializeField, Tooltip("与门节点的第一条输入热管")]
        private HeatLine andGateLine1;

        [SerializeField, Tooltip("与门节点的第二条输入热管")]
        private HeatLine andGateLine2;

        [SerializeField, Tooltip("被测与门节点")]
        private HeatNode andGateNode;

        private void Start()
        {
            Debug.Log("=== HeatSystemTester 已就绪 ===\n" +
                      "在 Inspector 中右键此组件，选择以下测试：\n" +
                      "  'Run Basic Test'     — 基础连通/断开测试\n" +
                      "  'Run AndGate Test'   — 与门逻辑测试\n" +
                      "  'Run All Tests'      — 全部测试");
        }

        #region Basic Flow Test

        [ContextMenu("Run Basic Test")]
        private void RunBasicTestContextMenu() => StartCoroutine(RunBasicTest());

        /// <summary>
        /// 基础测试：热源 → 热管(断路点) → 中继器 → 输出热管 → 终端
        /// </summary>
        private IEnumerator RunBasicTest()
        {
            Debug.Log("\n══════════════════════════════════════");
            Debug.Log("🧪 基础测试开始：接通/断开验证");
            Debug.Log("══════════════════════════════════════");

            if (testSource == null || sourceLine == null || repeaterNode == null)
            {
                Debug.LogError("❌ 基础测试失败：请在 Inspector 中设置 testSource, sourceLine, repeaterNode！");
                yield break;
            }

            var breakPoints = sourceLine.BreakPoints;
            int bpCount = breakPoints != null ? breakPoints.Length : 0;
            Debug.Log($"  热管 [{sourceLine.name}] 包含 {bpCount} 个断路点");

            // Step 1: 初始状态检查
            Debug.Log("\n── Step 1: 初始状态 ──");
            yield return null;

            bool sourceOutputting = testSource.IsOutputting;
            bool lineFlowing = sourceLine.IsFlowing;
            bool nodeOutputting = repeaterNode.IsOutputting;

            LogResult("热源输出", sourceOutputting, true);
            LogResult("热管流动（所有断路点未修复 → 应阻断）", lineFlowing, false);
            LogResult("节点输出（输入未到 → 应无输出）", nodeOutputting, false);

            if (lineFlowing) Debug.LogError("  ⚠ 断路点未修复但热管却在流动！检查逻辑。");
            if (nodeOutputting) Debug.LogError("  ⚠ 输入未到但节点却有输出！检查逻辑。");

            // Step 2: 逐个修复断路点
            if (bpCount > 0)
            {
                Debug.Log("\n── Step 2: 修复断路点 ──");
                for (int i = 0; i < breakPoints.Length; i++)
                {
                    breakPoints[i].Connect();
                    yield return null;
                    Debug.Log($"  修复第 {i + 1} 个断路点后 → 热管流动={sourceLine.IsFlowing}, " +
                              $"节点输出={repeaterNode.IsOutputting}");
                }
            }
            else
            {
                Debug.Log("\n── Step 2: 无断路点 ──");
                Debug.Log("  热管应直接流通（Provider已输出 + 无断路点）");
                yield return null;
            }

            // Step 3: 验证连通
            Debug.Log("\n── Step 3: 验证连通 ──");
            yield return null;
            LogResult("热管流动", sourceLine.IsFlowing, true);
            LogResult("节点输出", repeaterNode.IsOutputting, true);

            if (!sourceLine.IsFlowing)
                Debug.LogError("  ⚠ 所有断路点已修复但热管不流动！");
            if (!repeaterNode.IsOutputting)
                Debug.LogError("  ⚠ 热管流动但中继器节点不输出！");

            // Step 4: 验证输出热管也被触发
            if (outputLine != null)
            {
                yield return null;
                LogResult("输出热管流动", outputLine.IsFlowing, true);
                if (!outputLine.IsFlowing)
                    Debug.LogError("  ⚠ 节点有输出但下游输出热管不流动！（检查 outputLine 的 provider 是否指向 repeaterNode）");
            }

            // Step 5: 断开测试 — 重置断路点
            if (bpCount > 0)
            {
                Debug.Log("\n── Step 5: 断开验证（重置断路点）──");
                var bp = breakPoints[0];
                bp.Disconnect();
                yield return null;

                LogResult("热管流动（断路后→应阻断）", sourceLine.IsFlowing, false);
                LogResult("节点输出（输入断后→应无输出）", repeaterNode.IsOutputting, false);

                if (sourceLine.IsFlowing)
                    Debug.LogError("  ⚠ 断路点已重置但热管仍在流动！");
                if (repeaterNode.IsOutputting)
                    Debug.LogError("  ⚠ 输入断开但节点仍有输出！");

                // 恢复
                bp.Connect();
                yield return null;
                Debug.Log("  恢复断路点 → 热管应重新流动");
                LogResult("热管恢复流动", sourceLine.IsFlowing, true);
            }

            Debug.Log("\n══════════════════════════════════════");
            Debug.Log("🧪 基础测试完成");
            Debug.Log("══════════════════════════════════════\n");
        }

        #endregion

        #region AndGate Test

        [ContextMenu("Run AndGate Test")]
        private void RunAndGateTestContextMenu() => StartCoroutine(RunAndGateTest());

        private IEnumerator RunAndGateTest()
        {
            Debug.Log("\n══════════════════════════════════════");
            Debug.Log("🧪 与门测试开始");
            Debug.Log("══════════════════════════════════════");

            if (andGateNode == null || andGateLine1 == null || andGateLine2 == null)
            {
                Debug.LogError("❌ 与门测试失败：请在 Inspector 中设置 andGateNode, andGateLine1, andGateLine2！");
                yield break;
            }

            if (andGateNode.NodeType != NodeType.AndGate)
            {
                Debug.LogWarning("⚠ 节点不是与门类型，请在 Inspector 中将节点设为 AndGate");
            }

            // 先确保两条线的断路点都断开
            var bp1 = andGateLine1.BreakPoints;
            var bp2 = andGateLine2.BreakPoints;
            foreach (var bp in bp1) bp.Disconnect();
            foreach (var bp in bp2) bp.Disconnect();
            yield return null;

            Debug.Log("\n── 只修复第一条线的断路点 ──");
            foreach (var bp in bp1) bp.Connect();
            yield return null;

            LogResult("Line1 流动", andGateLine1.IsFlowing, true);
            LogResult("Line2 流动（未修复→应阻断）", andGateLine2.IsFlowing, false);
            LogResult("与门输出（只有1/2 → 应无输出）", andGateNode.IsOutputting, false);

            if (andGateNode.IsOutputting)
                Debug.LogError("  ⚠ 与门只有1条输入却有输出！");

            Debug.Log("\n── 再修复第二条线的断路点 ──");
            foreach (var bp in bp2) bp.Connect();
            yield return null;

            LogResult("Line1 流动", andGateLine1.IsFlowing, true);
            LogResult("Line2 流动", andGateLine2.IsFlowing, true);
            LogResult("与门输出（2/2 → 应有输出）", andGateNode.IsOutputting, true);

            if (!andGateNode.IsOutputting)
                Debug.LogError("  ⚠ 与门全部输入就绪但无输出！");

            Debug.Log("\n── 断开其中一条线 ──");
            bp1[0].Disconnect();
            yield return null;

            LogResult("与门输出（断开1条→应无输出）", andGateNode.IsOutputting, false);

            if (andGateNode.IsOutputting)
                Debug.LogError("  ⚠ 与门输入已断开但仍有输出！");

            // 恢复
            bp1[0].Connect();
            yield return null;

            Debug.Log("\n══════════════════════════════════════");
            Debug.Log("🧪 与门测试完成");
            Debug.Log("══════════════════════════════════════\n");
        }

        #endregion

        #region All Tests

        [ContextMenu("Run All Tests")]
        private void RunAllTestsContextMenu() => StartCoroutine(RunAllTests());

        private IEnumerator RunAllTests()
        {
            yield return RunBasicTest();
            yield return new WaitForSeconds(0.5f);
            yield return RunAndGateTest();

            Debug.Log("\n🎉 全部测试完成！检查上方日志中是否有 ❌/⚠ 标记。\n");
        }

        #endregion

        #region Helpers

        private void LogResult(string description, bool actual, bool expected)
        {
            bool pass = actual == expected;
            string mark = pass ? "✅" : "❌";
            string note = pass ? "" : $" (期望={expected}, 实际={actual})";
            Debug.Log($"  {mark} {description}{note}");
        }

        #endregion
    }
}
