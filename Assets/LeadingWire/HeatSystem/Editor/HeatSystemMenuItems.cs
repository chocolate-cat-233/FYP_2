using UnityEngine;
using UnityEditor;

namespace HeatSystem.Editor
{
    /// <summary>
    /// 热管系统编辑器快捷菜单。
    /// 右键 Hierarchy → Heat System 创建预配置好的 GameObjects。
    /// </summary>
    public static class HeatSystemMenuItems
    {
        private const string MENU_ROOT = "GameObject/Heat System/";
        private const string PREFAB_ROOT = "Assets/LeadingWire/HeatSystem/";

        #region Create Menu Items

        // ── Public static factories (used by HeatSystemWindow) ──

        public static GameObject CreateHeatSourceStatic()
        {
            GameObject go = CreateBaseObject("HeatSource", typeof(HeatSource));
            go.name = "热源";
            return go;
        }

        public static GameObject CreateHeatNodeStatic()
        {
            GameObject go = CreateBaseObject("HeatNode_Repeater", typeof(HeatNode));
            go.name = "节点_中继器";
            return go;
        }

        public static GameObject CreateHeatLineStatic()
        {
            GameObject go = CreateBaseObject("HeatLine", typeof(HeatLine));
            go.name = "热管";
            return go;
        }

        public static GameObject CreateBreakPointStatic()
        {
            GameObject go = CreateBaseObject("BreakPoint", typeof(BreakPoint));
            go.name = "断路点";
            var col = go.GetComponent<BoxCollider>();
            if (col == null) col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one;
            return go;
        }

        public static GameObject CreatePushableBoxStatic()
        {
            GameObject go = CreateBaseObject("PushableBox", typeof(PushableBox));
            go.name = "可推箱子";
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            return go;
        }

        // ── MenuItem wrappers ──

        [MenuItem(MENU_ROOT + "热源 (Heat Source)", false, 10)]
        private static void CreateHeatSource()
        {
            Selection.activeGameObject = CreateHeatSourceStatic();
        }

        [MenuItem(MENU_ROOT + "节点-中继器 (Repeater)", false, 11)]
        private static void CreateRepeaterNode()
        {
            Selection.activeGameObject = CreateHeatNodeStatic();
        }

        [MenuItem(MENU_ROOT + "节点-与门 (And Gate)", false, 12)]
        private static void CreateAndGateNode()
        {
            GameObject go = CreateHeatNodeStatic();
            go.name = "节点_与门";
            Selection.activeGameObject = go;
        }

        [MenuItem(MENU_ROOT + "热管 (Heat Line)", false, 13)]
        private static void CreateHeatLine()
        {
            Selection.activeGameObject = CreateHeatLineStatic();
        }

        [MenuItem(MENU_ROOT + "断路点 (Break Point)", false, 14)]
        private static void CreateBreakPoint()
        {
            Selection.activeGameObject = CreateBreakPointStatic();
        }

        [MenuItem(MENU_ROOT + "管线段 (Segment)", false, 15)]
        private static void CreateSegment()
        {
            GameObject go = CreateBaseObject("HeatLineSegment", typeof(HeatLineSegment));
            go.name = "管线段";
            go.transform.localScale = Vector3.one * 0.2f;
            Selection.activeGameObject = go;
        }

        [MenuItem(MENU_ROOT + "可推箱子 (Pushable Box)", false, 16)]
        private static void CreatePushableBox()
        {
            Selection.activeGameObject = CreatePushableBoxStatic();
        }

        #endregion

        #region Auto-Connect

        [MenuItem(MENU_ROOT + "自动连线 (选中 Provider + Acceptor)", false, 50)]
        private static void AutoConnect()
        {
            if (Selection.gameObjects.Length < 2)
            {
                Debug.LogWarning("请同时选中一个 Provider（热源/节点）和一个 Acceptor（热管下游节点）");
                return;
            }

            HeatProvider provider = null;
            HeatNode acceptor = null;
            Transform midPoint = null;

            foreach (var obj in Selection.gameObjects)
            {
                var hp = obj.GetComponent<HeatProvider>();
                if (hp != null && provider == null) provider = hp;

                var hn = obj.GetComponent<HeatNode>();
                if (hn != null && acceptor == null) acceptor = hn;

                // 不是 provider 也不是 acceptor 的物体作为中间点
                if (hp == null && hn == null && midPoint == null)
                    midPoint = obj.transform;
            }

            if (provider == null)
            {
                Debug.LogError("未找到 HeatProvider！请选中一个热源或节点。");
                return;
            }
            if (acceptor == null)
            {
                Debug.LogError("未找到 HeatNode 作为下游！请选中一个节点。");
                return;
            }

            // 创建热管
            GameObject lineGo = CreateBaseObject("HeatLine_Auto", typeof(HeatLine));
            lineGo.name = $"热管_{provider.name}_to_{acceptor.name}";

            if (midPoint != null)
            {
                lineGo.transform.position = midPoint.position;
            }
            else
            {
                // 放在 provider 和 acceptor 中间
                lineGo.transform.position = (provider.transform.position + acceptor.transform.position) * 0.5f;
            }

            // 自动连线
            var line = lineGo.GetComponent<HeatLine>();
            // 使用 SerializedObject 来设置私有字段（或通过 reflection）
            SerializedObject so = new SerializedObject(line);
            so.FindProperty("provider").objectReferenceValue = provider;
            so.FindProperty("acceptor").objectReferenceValue = acceptor;
            so.ApplyModifiedProperties();

            // 自动生成管线段（在编辑器中即可看到）
            line.GenerateSegments();

            Selection.activeGameObject = lineGo;
            Debug.Log($"✅ 自动连线完成: {provider.name} → {lineGo.name} → {acceptor.name}");
            Debug.Log($"   管线段已生成，选中热管后在 Inspector 中调整 segmentCount 和 cascadeDelay。");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 创建一个带可视化 Cube 子物体的 GameObject。
        /// </summary>
        private static GameObject CreateBaseObject(string defaultName, params System.Type[] components)
        {
            GameObject go = new GameObject(defaultName, components);

            // 放在当前场景视图焦点附近
            if (SceneView.lastActiveSceneView != null)
            {
                var cam = SceneView.lastActiveSceneView.camera;
                if (cam != null)
                {
                    go.transform.position = cam.transform.position + cam.transform.forward * 5f;
                }
            }

            // 创建可视化 Cube 子物体
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 0.4f;

            // 移除子物体上不需要的组件，保留 MeshRenderer 用于显示颜色
            Object.DestroyImmediate(visual.GetComponent<Rigidbody>());
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            // 确保父物体有正确的 Collider 设置（如果是 BreakPoint）
            var bp = go.GetComponent<BreakPoint>();
            if (bp != null)
            {
                var col = go.GetComponent<BoxCollider>();
                if (col == null) col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = Vector3.one * 0.6f;
            }

            // 撤销注册
            Undo.RegisterCreatedObjectUndo(go, $"创建 {go.name}");

            return go;
        }

        #endregion
    }
}
