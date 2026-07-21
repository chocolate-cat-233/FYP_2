using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 可推箱子。玩家推动箱子来修复热量线路上的断路点。
    ///
    /// 需要 Rigidbody（用于物理推动），自动追踪移动方向供 BreakPoint 检测。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PushableBox : MonoBehaviour
    {
        [Header("箱子设置")]
        [SerializeField, Tooltip("箱子的显示名称")]
        private string boxName = "箱子";

        [Header("调试")]
        [SerializeField, Tooltip("当前移动方向（只读）")]
        private Vector3 debugMoveDirection;

        private Vector3 lastPosition;
        private Vector3 _lastMoveDirection;

        /// <summary>上一次移动的方向（世界坐标系）</summary>
        public Vector3 LastMoveDirection => _lastMoveDirection;

        /// <summary>当前是否在移动中</summary>
        public bool IsMoving => _lastMoveDirection.magnitude > 0.01f;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            // 默认设置为 Kinematic 模式的刚体，适合被角色控制器推动
            // 可在 Inspector 中改为 Dynamic 如果需要物理模拟
            if (rb != null)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }
            lastPosition = transform.position;
        }

        private void Update()
        {
            TrackMovement();
        }

        /// <summary>
        /// 追踪物体移动方向
        /// </summary>
        private void TrackMovement()
        {
            Vector3 currentPos = transform.position;
            Vector3 delta = currentPos - lastPosition;

            if (delta.magnitude > 0.001f)
            {
                _lastMoveDirection = delta.normalized;
                debugMoveDirection = _lastMoveDirection;
            }
            else
            {
                _lastMoveDirection = Vector3.zero;
                debugMoveDirection = Vector3.zero;
            }

            lastPosition = currentPos;
        }

        /// <summary>
        /// 模拟向指定方向推箱子（用于测试，在 Inspector 右键调用）。
        /// </summary>
        /// <param name="direction">世界坐标系方向</param>
        /// <param name="distance">移动距离</param>
        public void Push(Vector3 direction, float distance = 1f)
        {
            transform.position += direction.normalized * distance;
            _lastMoveDirection = direction.normalized;
            debugMoveDirection = _lastMoveDirection;
            lastPosition = transform.position;

            Debug.Log($"[箱子: {boxName}] 被推向 {direction.normalized}, 距离={distance}");
        }

#if UNITY_EDITOR
        // 编辑器下也用 gizmo 更新来追踪手动拖拽
        private void OnDrawGizmosSelected()
        {
            // 确保编辑器拖拽也能更新 lastPosition
            if (!Application.isPlaying)
            {
                lastPosition = transform.position;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);

            // 绘制移动方向箭头
            if (_lastMoveDirection.magnitude > 0.01f)
            {
                Gizmos.color = Color.yellow;
                Vector3 start = transform.position;
                Gizmos.DrawRay(start, _lastMoveDirection * 0.5f);
            }

            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, $"📦 {boxName}");
        }

        // 测试用的右键菜单
        [ContextMenu("向东推 1 单位")]
        private void PushEast() => Push(Vector3.right);

        [ContextMenu("向西推 1 单位")]
        private void PushWest() => Push(Vector3.left);

        [ContextMenu("向北推 1 单位")]
        private void PushNorth() => Push(Vector3.forward);

        [ContextMenu("向南推 1 单位")]
        private void PushSouth() => Push(Vector3.back);
#endif
    }
}
