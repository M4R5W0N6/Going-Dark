using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TPSBR.EditorTools
{
	public static class HDRPMaterialMigrationUtility
	{
		[MenuItem("Tools/Rendering/HDRP/Report Unsupported Materials")]
		public static void ReportUnsupportedMaterials()
		{
			var entries = new List<string>();

			foreach (var mat in EnumerateAllMaterials())
			{
				if (mat == null)
					continue;

				var path = AssetDatabase.GetAssetPath(mat);
				if (ShouldConvert(mat, path) == false)
					continue;

				string shaderName = mat.shader != null ? mat.shader.name : "<missing shader>";
				entries.Add($"{path} :: {shaderName}");
			}

			Debug.Log($"[HDRP Migration] Unsupported/legacy materials: {entries.Count}");
			for (int i = 0; i < entries.Count; i++)
			{
				Debug.Log($"[HDRP Migration] {entries[i]}");
			}
		}

		[MenuItem("Tools/Rendering/HDRP/Report Materials Using URP Package Shaders")]
		public static void ReportMaterialsUsingURPPackageShaders()
		{
			var entries = new List<string>();

			foreach (var mat in EnumerateAllMaterials())
			{
				if (mat == null)
					continue;

				if (IsUsingURPPackageShader(mat) == false)
					continue;

				string path = AssetDatabase.GetAssetPath(mat);
				string shaderPath = mat.shader != null ? AssetDatabase.GetAssetPath(mat.shader) : "<missing shader>";
				string shaderName = mat.shader != null ? mat.shader.name : "<missing shader>";
				entries.Add($"{path} :: {shaderName} :: {shaderPath}");
			}

			Debug.Log($"[HDRP Migration] Materials using URP package shaders: {entries.Count}");
			for (int i = 0; i < entries.Count; i++)
			{
				Debug.Log($"[HDRP Migration] {entries[i]}");
			}
		}

		[MenuItem("Tools/Rendering/HDRP/Convert Unsupported Materials To HDRP Fallback")]
		public static void ConvertUnsupportedMaterialsToHDRPFallback()
		{
			var hdrpLit = Shader.Find("HDRP/Lit");
			var hdrpUnlit = Shader.Find("HDRP/Unlit");

			if (hdrpLit == null || hdrpUnlit == null)
			{
				Debug.LogError("[HDRP Migration] HDRP shaders were not found. Make sure HDRP is installed and loaded.");
				return;
			}

			int converted = 0;
			int skipped = 0;

			foreach (var mat in EnumerateAllMaterials())
			{
				if (mat == null)
				{
					skipped++;
					continue;
				}

				string path = AssetDatabase.GetAssetPath(mat);
				if (ShouldConvert(mat, path) == false)
				{
					skipped++;
					continue;
				}

				bool useUnlit = IsLikelyUnlit(mat, path);
				Shader target = useUnlit ? hdrpUnlit : hdrpLit;

				Texture mainTex = GetTexture(mat, "_BaseMap", "_MainTex");
				Color color = GetColor(mat, "_BaseColor", "_Color");
				bool transparent = IsLikelyTransparent(mat);

				Undo.RecordObject(mat, "Convert Material To HDRP Fallback");
				mat.shader = target;

				if (target == hdrpLit)
				{
					if (mainTex != null && mat.HasProperty("_BaseColorMap"))
						mat.SetTexture("_BaseColorMap", mainTex);
					if (mat.HasProperty("_BaseColor"))
						mat.SetColor("_BaseColor", color);
					if (mat.HasProperty("_SurfaceType"))
						mat.SetFloat("_SurfaceType", transparent ? 1f : 0f);
					if (mat.HasProperty("_BlendMode"))
						mat.SetFloat("_BlendMode", 0f);
				}
				else
				{
					if (mainTex != null && mat.HasProperty("_UnlitColorMap"))
						mat.SetTexture("_UnlitColorMap", mainTex);
					if (mat.HasProperty("_UnlitColor"))
						mat.SetColor("_UnlitColor", color);
					if (mat.HasProperty("_SurfaceType"))
						mat.SetFloat("_SurfaceType", transparent ? 1f : 0f);
					if (mat.HasProperty("_BlendMode"))
						mat.SetFloat("_BlendMode", 0f);
				}

				EditorUtility.SetDirty(mat);
				converted++;
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[HDRP Migration] Converted {converted} materials to HDRP fallback shaders. Skipped {skipped}.");
		}

		private static IEnumerable<Material> EnumerateAllMaterials()
		{
			string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (path.EndsWith(".mat") == false)
					continue;
				yield return AssetDatabase.LoadAssetAtPath<Material>(path);
			}
		}

		private static bool ShouldConvert(Material mat, string path)
		{
			// Guard against non-material containers accidentally returned as material-typed sub-assets.
			if (path.EndsWith(".mat") == false)
				return false;

			if (mat.shader == null)
				return true;

			// Broken/import-error materials in a different render pipeline often end up here.
			string shaderName = mat.shader.name;
			if (shaderName == "Hidden/InternalErrorShader")
				return true;

			if (IsUsingURPPackageShader(mat))
				return true;

			// Some internal fallback shaders have no stable asset path but still indicate a broken material.
			string shaderAssetPath = AssetDatabase.GetAssetPath(mat.shader);
			if (string.IsNullOrEmpty(shaderAssetPath) && shaderName.StartsWith("Hidden/Internal"))
				return true;

			if (shaderName.StartsWith("Universal Render Pipeline/"))
				return true;

			if (shaderName.Contains("Shader Graphs/URP_"))
				return true;

			// Third-party URP support folders commonly used by effects packs.
			if (path.Contains("/Render Pipelines support/") && shaderName.Contains("URP"))
				return true;

			return false;
		}

		private static bool IsUsingURPPackageShader(Material mat)
		{
			if (mat == null || mat.shader == null)
				return false;

			string shaderAssetPath = AssetDatabase.GetAssetPath(mat.shader);
			if (string.IsNullOrEmpty(shaderAssetPath))
				return false;

			return shaderAssetPath.StartsWith("Packages/com.unity.render-pipelines.universal/");
		}

		private static bool IsLikelyUnlit(Material mat, string path)
		{
			string shaderName = mat.shader != null ? mat.shader.name : string.Empty;

			if (shaderName.Contains("Unlit"))
				return true;
			if (shaderName.Contains("Particles"))
				return true;
			if (shaderName.Contains("Sprite"))
				return true;
			if (path.Contains("/VFX/") || path.Contains("/Effects/"))
				return true;

			return false;
		}

		private static bool IsLikelyTransparent(Material mat)
		{
			if (mat.renderQueue >= 3000)
				return true;
			if (mat.HasProperty("_SurfaceType") && mat.GetFloat("_SurfaceType") > 0.5f)
				return true;
			if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f)
				return true;

			return false;
		}

		private static Texture GetTexture(Material mat, params string[] names)
		{
			for (int i = 0; i < names.Length; i++)
			{
				if (mat.HasProperty(names[i]))
				{
					var tex = mat.GetTexture(names[i]);
					if (tex != null)
						return tex;
				}
			}

			return null;
		}

		private static Color GetColor(Material mat, params string[] names)
		{
			for (int i = 0; i < names.Length; i++)
			{
				if (mat.HasProperty(names[i]))
					return mat.GetColor(names[i]);
			}

			return Color.white;
		}
	}
}
