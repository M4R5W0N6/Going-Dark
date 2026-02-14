namespace TPSBR.EditorTools
{
	using UnityEditor;
	using UnityEditor.Rendering.HighDefinition;
	using UnityEngine;
	using UnityEngine.Rendering;

	[CustomPassDrawer(typeof(TPSBR.VisionMaskPass))]
	public sealed class VisionMaskPassDrawer : CustomPassDrawer
	{
		protected override PassUIFlag commonPassUIFlags => PassUIFlag.Name;

		private SerializedProperty _lightLayerMask;

		protected override void Initialize(SerializedProperty customPass)
		{
			_lightLayerMask = customPass.FindPropertyRelative("_lightLayerMask");
		}

		protected override void DoPassGUI(SerializedProperty customPass, Rect rect)
		{
			if (_lightLayerMask != null)
			{
				EditorGUI.BeginChangeCheck();
				RenderingLayerMask selectedMask = (RenderingLayerMask)_lightLayerMask.uintValue;
				selectedMask = EditorGUI.RenderingLayerMaskField(rect, "Light Layers", selectedMask);
				if (EditorGUI.EndChangeCheck())
				{
					_lightLayerMask.uintValue = (uint)selectedMask;
				}
			}
		}

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
		}
	}
}
