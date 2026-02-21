namespace TPSBR.EditorTools
{
	using UnityEditor;
	using UnityEditor.Rendering.HighDefinition;
	using UnityEngine;

	[CustomPassDrawer(typeof(TPSBR.VisionPostPass))]
	public sealed class VisionPostPassDrawer : CustomPassDrawer
	{
		protected override PassUIFlag commonPassUIFlags => PassUIFlag.Name;

		private SerializedProperty _shader;
		private SerializedProperty _visionInside;
		private SerializedProperty _visionOutside;
		private SerializedProperty _hidden;
		private SerializedProperty _strength;
		private SerializedProperty _tintColor;
		private SerializedProperty _saturationStrength;
		private SerializedProperty _overlayTexture;
		private SerializedProperty _textureTiling;
		private SerializedProperty _textureScrollSpeed;
		private SerializedProperty _triplanarSharpness;
		private SerializedProperty _outlineColor;
		private SerializedProperty _outlineThickness;
		private SerializedProperty _depthDistance;
		private SerializedProperty _depthAttenuationPower;
		private SerializedProperty _renderLayerMask;
		private const string DEFAULT_SHADER_NAME = "Hidden/TPSBR/HDRP/PostGreyscale";

		protected override void Initialize(SerializedProperty customPass)
		{
			_shader = customPass.FindPropertyRelative("_shader");
			_visionInside = customPass.FindPropertyRelative("_visionInside");
			_visionOutside = customPass.FindPropertyRelative("_visionOutside");
			_hidden = customPass.FindPropertyRelative("_hidden");
			_strength = customPass.FindPropertyRelative("_strength");
			_tintColor = customPass.FindPropertyRelative("_tintColor");
			_saturationStrength = customPass.FindPropertyRelative("_saturationStrength");
			_overlayTexture = customPass.FindPropertyRelative("_overlayTexture");
			_textureTiling = customPass.FindPropertyRelative("_textureTiling");
			_textureScrollSpeed = customPass.FindPropertyRelative("_textureScrollSpeed");
			_triplanarSharpness = customPass.FindPropertyRelative("_triplanarSharpness");
			_outlineColor = customPass.FindPropertyRelative("_outlineColor");
			_outlineThickness = customPass.FindPropertyRelative("_outlineThickness");
			_depthDistance = customPass.FindPropertyRelative("_depthDistance");
			_depthAttenuationPower = customPass.FindPropertyRelative("_depthAttenuationPower");
			_renderLayerMask = customPass.FindPropertyRelative("_renderLayerMask");
		}

		protected override void DoPassGUI(SerializedProperty customPass, Rect rect)
		{
			Shader activeShader = GetActiveShader();

			if (_shader != null)
			{
				EditorGUI.PropertyField(rect, _shader, new GUIContent("Shader"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_renderLayerMask != null)
			{
				EditorGUI.PropertyField(rect, _renderLayerMask, new GUIContent("Render Layers"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_visionInside != null)
			{
				_visionInside.floatValue = EditorGUI.Slider(rect, new GUIContent("Vision Inside"), _visionInside.floatValue, 0.0f, 1.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_visionOutside != null)
			{
				_visionOutside.floatValue = EditorGUI.Slider(rect, new GUIContent("Vision Outside"), _visionOutside.floatValue, 0.0f, 1.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_hidden != null)
			{
				_hidden.floatValue = EditorGUI.Slider(rect, new GUIContent("Hidden"), _hidden.floatValue, 0.0f, 1.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_strength != null)
			{
				_strength.floatValue = EditorGUI.Slider(rect, new GUIContent("Strength"), _strength.floatValue, -1.0f, 1.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_tintColor != null && ShaderHasProperty(activeShader, "_TintColor"))
			{
				EditorGUI.PropertyField(rect, _tintColor, new GUIContent("Tint Color"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_saturationStrength != null && ShaderHasProperty(activeShader, "_SaturationStrength"))
			{
				_saturationStrength.floatValue = EditorGUI.Slider(rect, new GUIContent("Saturation"), _saturationStrength.floatValue, 0.0f, 1.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_overlayTexture != null && ShaderHasProperty(activeShader, "_OverlayTex"))
			{
				EditorGUI.PropertyField(rect, _overlayTexture, new GUIContent("Overlay Texture"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_textureTiling != null && ShaderHasProperty(activeShader, "_TextureTiling"))
			{
				EditorGUI.PropertyField(rect, _textureTiling, new GUIContent("Texture Tiling"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_textureScrollSpeed != null && ShaderHasProperty(activeShader, "_TextureScrollSpeed"))
			{
				EditorGUI.PropertyField(rect, _textureScrollSpeed, new GUIContent("Texture Scroll"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_triplanarSharpness != null && ShaderHasProperty(activeShader, "_TriplanarSharpness"))
			{
				_triplanarSharpness.floatValue = EditorGUI.Slider(rect, new GUIContent("Triplanar Sharpness"), _triplanarSharpness.floatValue, 1.0f, 32.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_outlineColor != null && ShaderHasProperty(activeShader, "_OutlineColor"))
			{
				EditorGUI.PropertyField(rect, _outlineColor, new GUIContent("Outline Color"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_outlineThickness != null && ShaderHasProperty(activeShader, "_OutlineThickness"))
			{
				_outlineThickness.floatValue = EditorGUI.Slider(rect, new GUIContent("Outline Thickness"), _outlineThickness.floatValue, 0.5f, 8.0f);
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_depthDistance != null && ShaderHasProperty(activeShader, "_DepthDistance"))
			{
				EditorGUI.PropertyField(rect, _depthDistance, new GUIContent("Depth Distance"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_depthAttenuationPower != null && ShaderHasProperty(activeShader, "_DepthAttenuationPower"))
			{
				_depthAttenuationPower.floatValue = EditorGUI.Slider(rect, new GUIContent("Depth Attenuation Power"), _depthAttenuationPower.floatValue, 0.0f, 1.0f);
			}

		}

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			Shader activeShader = GetActiveShader();
			int lineCount = 6; // Shader + Render Layers + Vision Inside + Vision Outside + Hidden + Strength.
			if (ShaderHasProperty(activeShader, "_TintColor")) lineCount++;
			if (ShaderHasProperty(activeShader, "_SaturationStrength")) lineCount++;
			if (ShaderHasProperty(activeShader, "_OverlayTex")) lineCount++;
			if (ShaderHasProperty(activeShader, "_TextureTiling")) lineCount++;
			if (ShaderHasProperty(activeShader, "_TextureScrollSpeed")) lineCount++;
			if (ShaderHasProperty(activeShader, "_TriplanarSharpness")) lineCount++;
			if (ShaderHasProperty(activeShader, "_OutlineColor")) lineCount++;
			if (ShaderHasProperty(activeShader, "_OutlineThickness")) lineCount++;
			if (ShaderHasProperty(activeShader, "_DepthDistance")) lineCount++;
			if (ShaderHasProperty(activeShader, "_DepthAttenuationPower")) lineCount++;
			return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * lineCount;
		}

		private Shader GetActiveShader()
		{
			if (_shader != null && _shader.objectReferenceValue is Shader selectedShader)
				return selectedShader;

			return Shader.Find(DEFAULT_SHADER_NAME);
		}

		private static bool ShaderHasProperty(Shader shader, string propertyName)
		{
			if (shader == null)
				return false;

			int propertyCount = shader.GetPropertyCount();
			for (int i = 0; i < propertyCount; i++)
			{
				if (shader.GetPropertyName(i) == propertyName)
					return true;
			}

			return false;
		}
	}
}
