namespace TPSBR.EditorTools
{
	using UnityEditor;
	using UnityEditor.Rendering.HighDefinition;
	using UnityEngine;

	[CustomPassDrawer(typeof(TPSBR.VisionHiddenPass))]
	public sealed class VisionHiddenPassDrawer : CustomPassDrawer
	{
		protected override PassUIFlag commonPassUIFlags => PassUIFlag.Name;

		private SerializedProperty _hiddenLayerMask;

		protected override void Initialize(SerializedProperty customPass)
		{
			_hiddenLayerMask = customPass.FindPropertyRelative("_hiddenLayerMask");
		}

		protected override void DoPassGUI(SerializedProperty customPass, Rect rect)
		{
			if (_hiddenLayerMask != null)
			{
				EditorGUI.PropertyField(rect, _hiddenLayerMask, new GUIContent("Hidden Layers"));
			}
		}

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
		}
	}
}
