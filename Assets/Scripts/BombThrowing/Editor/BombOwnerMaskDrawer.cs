#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Editor
{
    [CustomPropertyDrawer(typeof(BombOwnerMask))]
    public sealed class BombOwnerMaskDrawer : PropertyDrawer
    {
        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();

            BombOwnerMask current =
                (BombOwnerMask)property.intValue;
            BombOwnerMask selected = (BombOwnerMask)
                EditorGUI.EnumFlagsField(position, label, current);

            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = (int)selected;
            }
            EditorGUI.EndProperty();
        }
    }
}
#endif
