using UnityEngine;

namespace SuperFinalFuse.WallHide
{
    /// <summary>
    /// Marker component. Objects in the WallHideManager occlusion mask that
    /// carry this component (on themselves or any parent) are never hidden.
    /// Attach it to the player itself or to foreground props that should stay
    /// visible when the camera looks past them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallHideIgnore : MonoBehaviour
    {
    }
}
