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
		private SerializedProperty _punctualAttenuationPower;

		protected override void Initialize(SerializedProperty customPass)
		{
			_lightLayerMask = customPass.FindPropertyRelative("_lightLayerMask");
			_punctualAttenuationPower = customPass.FindPropertyRelative("_punctualAttenuationPower");
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

				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_punctualAttenuationPower != null)
			{
				_punctualAttenuationPower.floatValue = EditorGUI.Slider(
					rect,
					new GUIContent("Depth Attenuation Power"),
					_punctualAttenuationPower.floatValue,
					0.1f,
					8.0f);
			}
		}

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 2.0f;
		}
	}
}
