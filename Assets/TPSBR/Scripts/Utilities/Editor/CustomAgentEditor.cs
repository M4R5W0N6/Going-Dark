namespace TPSBR
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Reflection;
	using Fusion;
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.SceneManagement;
	using UnityScene = UnityEngine.SceneManagement.Scene;

	public sealed class CustomAgentEditor : EditorWindow
	{
		private const string AGENT_BASE_PATH = "Assets/TPSBR/Prefabs/Agents/AgentBase.prefab";
		private const string MENU_AGENT_BASE_PATH = "Assets/TPSBR/Prefabs/Agents/Menu/MenuAgentBase.prefab";
		private const string AGENT_SETTINGS_PATH = "Assets/TPSBR/Resources/Settings/AgentSettings.asset";
		private const string ICON_OUTPUT_FOLDER = "Assets/TPSBR/UI/AgentIcons";
		private const string STAGE_SCENE_PATH = "Assets/TPSBR/Scenes/Stage.unity";
		private const string DEFAULT_OUTPUT_FOLDER = "Assets/TPSBR/Prefabs/Agents";
		private const string MENU_OUTPUT_FOLDER = "Assets/TPSBR/Prefabs/Agents/Menu";
		private const string FUSION_ANIMATOR_GRAPH_PATH = "Assets/FusionAnimator/Graphs/FusionAnimatorGraph.asset";
		private const int SLOT_COUNT = 8;
		private const int DEFAULT_AGENT_LAYER = 8;
		private const int ICON_CAPTURE_LAYER = 31;
		private const int ICON_RESOLUTION = 512;
		private const float ICON_FOV = 10.0f;
		private const float ICON_VERTICAL_OFFSET_METERS = 0.5f;

		[SerializeField] private AgentSettings _agentSettings;
		[SerializeField] private GameObject _characterPrefab;
		[SerializeField] private string _agentName = "CustomAgent";
		[SerializeField] private string _outputFolder = DEFAULT_OUTPUT_FOLDER;
		[SerializeField] private string _agentId = "Agent.CustomAgent";
		[SerializeField] private string _displayName = "Custom Agent";
		[SerializeField, TextArea(3, 6)] private string _description = string.Empty;
		[SerializeField] private Sprite _icon;
		[SerializeField] private GameObject _agentPrefab;
		[SerializeField] private GameObject _menuAgentPrefab;
		private IconGenerationJob _iconGenerationJob;

		[MenuItem("Assets/Create/TPSBR/Expansion/Custom Agent", false, 320)]
		[MenuItem("Tools/Fusion/Custom Agent", false, 251)]
		private static void OpenWindow()
		{
			CustomAgentEditor window = GetWindow<CustomAgentEditor>("Custom Agent");
			window.minSize = new Vector2(540.0f, 390.0f);
			window.Show();
		}

		private void OnEnable()
		{
			if (_agentSettings == null)
			{
				_agentSettings = AssetDatabase.LoadAssetAtPath<AgentSettings>(AGENT_SETTINGS_PATH);
			}

			SyncSetupFieldsFromAgentName(false);
		}

		private void OnDisable()
		{
			if (_iconGenerationJob != null)
			{
				CleanupIconGenerationJob(_iconGenerationJob);
				_iconGenerationJob = null;
			}
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Create Agent Variant", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					_characterPrefab = (GameObject)EditorGUILayout.ObjectField("Agent Visuals", _characterPrefab, typeof(GameObject), false);
					using (new EditorGUI.DisabledScope(CanNormalizeVisualsImport() == false))
					{
						if (GUILayout.Button("Normalize Import", GUILayout.Width(130.0f)) == true)
						{
							NormalizeAgentVisualsImport();
						}
					}
				}
				_agentName = EditorGUILayout.TextField("Agent Name", _agentName);
				_outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
				_agentSettings = (AgentSettings)EditorGUILayout.ObjectField("Agent Settings", _agentSettings, typeof(AgentSettings), false);
			}

			EditorGUILayout.Space(4.0f);
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Sync ID/Display From Agent Name", GUILayout.Width(230.0f)) == true)
				{
					SyncSetupFieldsFromAgentName(true);
				}
			}

			EditorGUILayout.Space(6.0f);
			EditorGUILayout.LabelField("AgentSetup", EditorStyles.boldLabel);
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				_agentId = EditorGUILayout.TextField("Id", _agentId);
				_displayName = EditorGUILayout.TextField("Display Name", _displayName);
				EditorGUILayout.LabelField("Description");
				_description = EditorGUILayout.TextArea(_description, GUILayout.MinHeight(58.0f));

				EditorGUILayout.Space(6.0f);

				using (new EditorGUILayout.HorizontalScope())
				{
					_icon = (Sprite)EditorGUILayout.ObjectField("Icon", _icon, typeof(Sprite), false);
					using (new EditorGUI.DisabledScope(CanGenerateIcon() == false || _iconGenerationJob != null))
					{
						if (GUILayout.Button(_iconGenerationJob == null ? "Generate" : "Generating...", GUILayout.Width(90.0f)) == true)
						{
							GenerateIconForCurrentInput();
						}
					}
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					_agentPrefab = (GameObject)EditorGUILayout.ObjectField("Agent Prefab", _agentPrefab, typeof(GameObject), false);
					using (new EditorGUI.DisabledScope(CanGenerateAgentPrefab() == false))
					{
						if (GUILayout.Button("Generate", GUILayout.Width(90.0f)) == true)
						{
							GenerateAgentPrefabForCurrentInput();
						}
					}
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					_menuAgentPrefab = (GameObject)EditorGUILayout.ObjectField("Menu Agent Prefab", _menuAgentPrefab, typeof(GameObject), false);
					using (new EditorGUI.DisabledScope(CanGenerateMenuPrefab() == false))
					{
						if (GUILayout.Button("Generate", GUILayout.Width(90.0f)) == true)
						{
							GenerateMenuPrefabForCurrentInput();
						}
					}
				}

				EditorGUILayout.Space(8.0f);
				using (new EditorGUI.DisabledScope(CanAssignToSettings() == false))
				{
					if (GUILayout.Button("Assign to AgentSettings", GUILayout.Height(30.0f)) == true)
					{
						AssignCurrentSetupToSettings();
					}
				}
			}
		}

		private bool CanGenerateIcon()
		{
			return _characterPrefab != null && string.IsNullOrWhiteSpace(_agentName) == false;
		}

		private bool CanNormalizeVisualsImport()
		{
			return TryGetModelAssetPathFromAgentVisuals(_characterPrefab, out _);
		}

		private bool CanGenerateAgentPrefab()
		{
			return _characterPrefab != null && string.IsNullOrWhiteSpace(_agentName) == false && string.IsNullOrWhiteSpace(_outputFolder) == false;
		}

		private bool CanGenerateMenuPrefab()
		{
			return _characterPrefab != null && string.IsNullOrWhiteSpace(_agentName) == false;
		}

		private bool CanAssignToSettings()
		{
			return _agentSettings != null && string.IsNullOrWhiteSpace(_agentId) == false && string.IsNullOrWhiteSpace(_displayName) == false;
		}

		private void NormalizeAgentVisualsImport()
		{
			if (TryNormalizeAgentVisualsImport(_characterPrefab, out string error) == false)
			{
				EditorUtility.DisplayDialog("Normalize Import", error, "OK");
				return;
			}

			EditorUtility.DisplayDialog("Normalize Import", "Agent visuals importer settings were normalized and reimported.", "OK");
		}

		private void SyncSetupFieldsFromAgentName(bool force)
		{
			if (string.IsNullOrWhiteSpace(_agentName) == true)
				return;

			string expectedId = GetAgentId(_agentName);
			string expectedDisplayName = GetDisplayName(_agentName);

			if (force == true || string.IsNullOrWhiteSpace(_agentId) == true)
			{
				_agentId = expectedId;
			}

			if (force == true || string.IsNullOrWhiteSpace(_displayName) == true)
			{
				_displayName = expectedDisplayName;
			}
		}

		private void GenerateIconForCurrentInput()
		{
			if (ValidateCharacterPrefabInput(requireOutputFolder: false, out string validationError) == false)
			{
				EditorUtility.DisplayDialog("Generate Icon", validationError, "OK");
				return;
			}

			if (StartIconGeneration(_agentName.Trim(), _characterPrefab, out string iconError) == false)
			{
				EditorUtility.DisplayDialog("Generate Icon", iconError, "OK");
				return;
			}
		}

		private bool StartIconGeneration(string agentName, GameObject characterPrefab, out string error)
		{
			error = string.Empty;

			if (_iconGenerationJob != null)
			{
				error = "Icon generation is already running.";
				return false;
			}

			IconGenerationJob job = new IconGenerationJob();
			try
			{
				if (AssetDatabase.LoadAssetAtPath<SceneAsset>(STAGE_SCENE_PATH) == null)
				{
					error = $"Cannot find stage scene at {STAGE_SCENE_PATH}";
					return false;
				}

				job.AgentName = agentName;
				job.StageScene = SceneManager.GetSceneByPath(STAGE_SCENE_PATH);
				if (job.StageScene.isLoaded == false)
				{
					job.StageScene = EditorSceneManager.OpenScene(STAGE_SCENE_PATH, OpenSceneMode.Additive);
					job.OpenedStageScene = true;
				}

				if (job.StageScene.IsValid() == false || job.StageScene.isLoaded == false)
				{
					error = $"Failed to load stage scene {STAGE_SCENE_PATH}";
					return false;
				}

				job.StageCamera = FindStageCaptureCamera(job.StageScene);
				if (job.StageCamera == null)
				{
					error = "No capture camera found in Stage scene. Please add/enable one camera in Stage.unity.";
					return false;
				}

				job.CharacterInstance = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
				if (job.CharacterInstance == null)
				{
					error = "Failed to instantiate character prefab for icon generation.";
					return false;
				}

				SceneManager.MoveGameObjectToScene(job.CharacterInstance, job.StageScene);
				job.CharacterInstance.name = characterPrefab.name;
				job.CharacterInstance.transform.position = Vector3.zero;
				job.CharacterInstance.transform.rotation = Quaternion.identity;
				job.CharacterInstance.transform.localScale = Vector3.one;

				if (TryResolveCharacterRig(job.CharacterInstance, out _, out error) == false)
					return false;

				Animator[] iconAnimators = job.CharacterInstance.GetComponentsInChildren<Animator>(true);
				for (int i = 0; i < iconAnimators.Length; ++i)
				{
					iconAnimators[i].enabled = false;
				}

				Animation[] legacyAnimations = job.CharacterInstance.GetComponentsInChildren<Animation>(true);
				for (int i = 0; i < legacyAnimations.Length; ++i)
				{
					legacyAnimations[i].enabled = false;
				}

				int pathTracingSampleCount = GetPathTracingSampleCount(job.StageScene);
				job.UsesPathTracing = pathTracingSampleCount > 0;
				job.SampleCount = job.UsesPathTracing == true ? pathTracingSampleCount : 1;
				job.SamplesRendered = 0;
				job.IconRT = RenderTexture.GetTemporary(ICON_RESOLUTION, ICON_RESOLUTION, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				job.StageCameraOriginalTargetTexture = job.StageCamera.targetTexture;
				job.StageCamera.targetTexture = job.IconRT;

				_iconGenerationJob = job;
				EditorApplication.update += UpdateIconGeneration;
				Repaint();
				return true;
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				error = "Exception during icon generation setup. See Console for details.";
				return false;
			}
			finally
			{
				if (_iconGenerationJob == null)
				{
					CleanupIconGenerationJob(job);
				}
			}
		}

		private void UpdateIconGeneration()
		{
			IconGenerationJob job = _iconGenerationJob;
			if (job == null)
			{
				EditorApplication.update -= UpdateIconGeneration;
				EditorUtility.ClearProgressBar();
				return;
			}

			try
			{
				if (job.SamplesRendered < job.SampleCount)
				{
					job.StageCamera.Render();
					job.SamplesRendered += 1;

					float progress = job.SampleCount > 0 ? Mathf.Clamp01((float)job.SamplesRendered / job.SampleCount) : 0.0f;
					string progressText = job.UsesPathTracing == true
						? $"Accumulating path tracing samples ({job.SamplesRendered}/{job.SampleCount})"
						: "Rendering single frame (1/1)";
					EditorUtility.DisplayProgressBar("Generate Icon", progressText, progress);
					SceneView.RepaintAll();
					Repaint();
					return;
				}

				if (FinalizeIconGeneration(job, out Sprite iconSprite, out string error) == false)
				{
					StopAndCleanupIconGeneration(job);
					EditorUtility.DisplayDialog("Generate Icon", error, "OK");
					return;
				}

				_icon = iconSprite;
				StopAndCleanupIconGeneration(job);
				EditorUtility.FocusProjectWindow();
				Selection.activeObject = iconSprite;
				EditorGUIUtility.PingObject(iconSprite);
				Repaint();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				StopAndCleanupIconGeneration(job);
				EditorUtility.DisplayDialog("Generate Icon", "Exception during icon generation. See Console for details.", "OK");
			}
		}

		private static bool FinalizeIconGeneration(IconGenerationJob job, out Sprite iconSprite, out string error)
		{
			iconSprite = null;
			error = string.Empty;

			RenderTexture previousActiveRT = RenderTexture.active;
			try
			{
				RenderTexture.active = job.IconRT;

				job.IconTexture = new Texture2D(ICON_RESOLUTION, ICON_RESOLUTION, TextureFormat.RGBA32, false, false);
				job.IconTexture.ReadPixels(new Rect(0, 0, ICON_RESOLUTION, ICON_RESOLUTION), 0, 0, false);
				job.IconTexture.Apply(false, false);

				byte[] png = job.IconTexture.EncodeToPNG();
				if (png == null || png.Length == 0)
				{
					error = "Failed to encode generated icon PNG.";
					return false;
				}

				CreateFolderRecursively(ICON_OUTPUT_FOLDER);
				string fileName = $"{SanitizeFileName(job.AgentName)}Icon.png";
				string assetPath = $"{ICON_OUTPUT_FOLDER}/{fileName}";
				string absolutePath = Path.GetFullPath(assetPath);
				string directoryPath = Path.GetDirectoryName(absolutePath);

				if (string.IsNullOrEmpty(directoryPath) == true)
				{
					error = $"Invalid icon output path: {assetPath}";
					return false;
				}

				Directory.CreateDirectory(directoryPath);
				File.WriteAllBytes(absolutePath, png);

				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
				TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
				if (importer != null)
				{
					importer.textureType = TextureImporterType.Sprite;
					importer.spriteImportMode = SpriteImportMode.Single;
					importer.alphaIsTransparency = true;
					importer.mipmapEnabled = false;
					importer.filterMode = FilterMode.Bilinear;
					importer.textureCompression = TextureImporterCompression.Compressed;
					importer.maxTextureSize = ICON_RESOLUTION;
					importer.SaveAndReimport();
				}

				iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
				if (iconSprite == null)
				{
					error = $"Failed to load generated icon sprite at {assetPath}";
					return false;
				}

				return true;
			}
			finally
			{
				RenderTexture.active = previousActiveRT;
			}
		}

		private void StopAndCleanupIconGeneration(IconGenerationJob job)
		{
			if (ReferenceEquals(_iconGenerationJob, job) == true)
			{
				_iconGenerationJob = null;
			}

			CleanupIconGenerationJob(job);
		}

		private void CleanupIconGenerationJob(IconGenerationJob job)
		{
			EditorApplication.update -= UpdateIconGeneration;
			EditorUtility.ClearProgressBar();

			if (job == null)
				return;

			if (job.StageCamera != null)
			{
				job.StageCamera.targetTexture = job.StageCameraOriginalTargetTexture;
			}

			if (job.IconRT != null)
			{
				RenderTexture.ReleaseTemporary(job.IconRT);
				job.IconRT = null;
			}

			if (job.IconTexture != null)
			{
				DestroyImmediate(job.IconTexture);
				job.IconTexture = null;
			}

			if (job.CharacterInstance != null)
			{
				DestroyImmediate(job.CharacterInstance);
				job.CharacterInstance = null;
			}

			if (job.OpenedStageScene == true && job.StageScene.IsValid() == true && job.StageScene.isLoaded == true)
			{
				EditorSceneManager.CloseScene(job.StageScene, true);
			}
		}

		private void GenerateAgentPrefabForCurrentInput()
		{
			if (ValidateCharacterPrefabInput(requireOutputFolder: true, out string validationError) == false)
			{
				EditorUtility.DisplayDialog("Generate Agent Prefab", validationError, "OK");
				return;
			}

			GameObject agentBasePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AGENT_BASE_PATH);
			if (agentBasePrefab == null)
			{
				EditorUtility.DisplayDialog("Generate Agent Prefab", $"Cannot find AgentBase prefab at {AGENT_BASE_PATH}", "OK");
				return;
			}

			GameObject agentInstance = null;
			try
			{
				string trimmedName = _agentName.Trim();
				agentInstance = (GameObject)PrefabUtility.InstantiatePrefab(agentBasePrefab);
				agentInstance.name = trimmedName;

				if (BuildAgent(agentInstance, _characterPrefab, out string buildError) == false)
				{
					EditorUtility.DisplayDialog("Generate Agent Prefab", buildError, "OK");
					return;
				}

				CreateFolderRecursively(_outputFolder);
				string agentPrefabPath = $"{_outputFolder.TrimEnd('/', '\\')}/{trimmedName}.prefab";
				GameObject savedAgentPrefab = PrefabUtility.SaveAsPrefabAsset(agentInstance, agentPrefabPath, out bool saveSuccess);
				if (saveSuccess == false || savedAgentPrefab == null)
				{
					EditorUtility.DisplayDialog("Generate Agent Prefab", $"Failed to save prefab at {agentPrefabPath}", "OK");
					return;
				}

				_agentPrefab = savedAgentPrefab;
				SyncSetupFieldsFromAgentName(false);

				EditorUtility.FocusProjectWindow();
				Selection.activeObject = savedAgentPrefab;
				EditorGUIUtility.PingObject(savedAgentPrefab);
				Repaint();
			}
			finally
			{
				if (agentInstance != null)
				{
					DestroyImmediate(agentInstance);
				}
			}
		}

		private void GenerateMenuPrefabForCurrentInput()
		{
			if (ValidateCharacterPrefabInput(requireOutputFolder: false, out string validationError) == false)
			{
				EditorUtility.DisplayDialog("Generate Menu Prefab", validationError, "OK");
				return;
			}

			if (CreateMenuAgentPrefab(_characterPrefab, _agentName.Trim(), out GameObject savedMenuPrefab, out string menuError) == false)
			{
				EditorUtility.DisplayDialog("Generate Menu Prefab", menuError, "OK");
				return;
			}

			_menuAgentPrefab = savedMenuPrefab;
			EditorUtility.FocusProjectWindow();
			Selection.activeObject = savedMenuPrefab;
			EditorGUIUtility.PingObject(savedMenuPrefab);
			Repaint();
		}

		private void AssignCurrentSetupToSettings()
		{
			if (_agentSettings == null)
			{
				_agentSettings = AssetDatabase.LoadAssetAtPath<AgentSettings>(AGENT_SETTINGS_PATH);
			}

			if (_agentSettings == null)
			{
				EditorUtility.DisplayDialog("Assign to AgentSettings", $"Cannot find AgentSettings at {AGENT_SETTINGS_PATH}", "OK");
				return;
			}

			if (CreateOrUpdateAgentSettings(_agentSettings, _agentId, _displayName, _description, _icon, _agentPrefab, _menuAgentPrefab, out string settingsError) == false)
			{
				EditorUtility.DisplayDialog("Assign to AgentSettings", settingsError, "OK");
				return;
			}

			EditorUtility.FocusProjectWindow();
			Selection.activeObject = _agentSettings;
			EditorGUIUtility.PingObject(_agentSettings);
			Debug.Log($"[CustomAgent] Assigned setup '{_agentId}' to AgentSettings.", _agentSettings);
		}

		private bool ValidateCharacterPrefabInput(bool requireOutputFolder, out string error)
		{
			error = string.Empty;

			if (_characterPrefab == null)
			{
				error = "Agent Visuals is required.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(_agentName) == true)
			{
				error = "Agent Name is required.";
				return false;
			}

			if (requireOutputFolder == true && string.IsNullOrWhiteSpace(_outputFolder) == true)
			{
				error = "Output Folder is required.";
				return false;
			}

			if (PrefabUtility.GetPrefabAssetType(_characterPrefab) == PrefabAssetType.NotAPrefab)
			{
				error = "Agent Visuals must be a prefab asset.";
				return false;
			}

			GameObject preview = null;
			try
			{
				preview = (GameObject)PrefabUtility.InstantiatePrefab(_characterPrefab);
				if (TryResolveCharacterRig(preview, out _, out string compatibilityError) == false)
				{
					error = compatibilityError;
					return false;
				}
			}
			finally
			{
				if (preview != null)
				{
					DestroyImmediate(preview);
				}
			}

			return true;
		}

		private bool BuildAgent(GameObject agentInstance, GameObject characterPrefab, out string error)
		{
			error = string.Empty;

			Transform visualsRoot = agentInstance.transform.Find("VisualsRoot");
			if (visualsRoot == null)
			{
				error = "AgentBase is missing VisualsRoot.";
				return false;
			}

			ReplaceCharacterUnderVisualsRoot(visualsRoot);

			GameObject characterInstance = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
			characterInstance.name = characterPrefab.name;
			characterInstance.transform.SetParent(visualsRoot, false);
			characterInstance.transform.localPosition = Vector3.zero;
			characterInstance.transform.localRotation = Quaternion.identity;
			characterInstance.transform.localScale = Vector3.one;
			SetLayerRecursively(characterInstance.transform, GetAgentLayer());
			Animator[] insertedAnimators = characterInstance.GetComponentsInChildren<Animator>(true);
			for (int i = 0; i < insertedAnimators.Length; ++i)
			{
				insertedAnimators[i].applyRootMotion = false;
			}

			if (TryResolveCharacterRig(characterInstance, out CharacterRig rig, out error) == false)
			{
				return false;
			}

			AssignCharacterView(agentInstance, rig);
			AssignAnimationController(agentInstance, rig);
			AssignWeaponSlots(agentInstance, rig);
			AssignBodyPartsAndHitboxes(agentInstance, rig);

			return true;
		}

		private static bool CreateMenuAgentPrefab(GameObject characterPrefab, string agentName, out GameObject savedMenuPrefab, out string error)
		{
			savedMenuPrefab = null;
			error = string.Empty;

			GameObject menuBasePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MENU_AGENT_BASE_PATH);
			if (menuBasePrefab == null)
			{
				error = $"Cannot find MenuAgentBase prefab at {MENU_AGENT_BASE_PATH}";
				return false;
			}

			GameObject menuInstance = null;
			try
			{
				menuInstance = (GameObject)PrefabUtility.InstantiatePrefab(menuBasePrefab);
				string menuName = GetMenuPrefabName(agentName);
				menuInstance.name = menuName;

				Transform menuRoot = menuInstance.transform;
				for (int i = menuRoot.childCount - 1; i >= 0; --i)
				{
					Transform child = menuRoot.GetChild(i);
					if (child.name == "Placeholder" || IsCharacterHierarchy(child) == true)
					{
						DestroyImmediate(child.gameObject);
					}
				}

				GameObject characterInstance = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
				characterInstance.name = characterPrefab.name;
				characterInstance.transform.SetParent(menuRoot, false);
				characterInstance.transform.localPosition = Vector3.zero;
				characterInstance.transform.localRotation = Quaternion.identity;
				characterInstance.transform.localScale = Vector3.one;

				SetLayerRecursively(characterInstance.transform, GetAgentLayer());

				Animator[] animators = characterInstance.GetComponentsInChildren<Animator>(true);
				for (int i = 0; i < animators.Length; ++i)
				{
					animators[i].applyRootMotion = false;
					animators[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
				}

				CreateFolderRecursively(MENU_OUTPUT_FOLDER);
				string menuPath = $"{MENU_OUTPUT_FOLDER}/{menuName}.prefab";
				savedMenuPrefab = PrefabUtility.SaveAsPrefabAsset(menuInstance, menuPath, out bool saveSuccess);
				if (saveSuccess == false || savedMenuPrefab == null)
				{
					error = $"Failed to save menu prefab at {menuPath}";
					return false;
				}

				return true;
			}
			finally
			{
				if (menuInstance != null)
				{
					DestroyImmediate(menuInstance);
				}
			}
		}

		private static bool CreateOrUpdateAgentSettings(AgentSettings settingsAsset, string id, string displayName, string description, Sprite icon, GameObject agentPrefab, GameObject menuAgentPrefab, out string error)
		{
			error = string.Empty;

			if (settingsAsset == null)
			{
				error = "AgentSettings reference is null.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(id) == true)
			{
				error = "AgentSetup Id is required.";
				return false;
			}

			SerializedObject settingsSO = new SerializedObject(settingsAsset);
			SerializedProperty agents = settingsSO.FindProperty("_agents");
			if (agents == null)
			{
				error = "AgentSettings does not contain _agents property.";
				return false;
			}

			int index = FindAgentSetupIndex(agents, id);
			if (index < 0)
			{
				index = agents.arraySize;
				agents.arraySize += 1;
			}

			SerializedProperty setup = agents.GetArrayElementAtIndex(index);
			SetString(setup.FindPropertyRelative("_id"), id);
			SetString(setup.FindPropertyRelative("_displayName"), string.IsNullOrWhiteSpace(displayName) ? id : displayName);
			SetString(setup.FindPropertyRelative("_description"), description ?? string.Empty);
			SetObjectReference(setup.FindPropertyRelative("_icon"), icon);

			SerializedProperty prefabRef = setup.FindPropertyRelative("_agentPrefab");
			if (prefabRef == null)
			{
				error = "AgentSettings setup is missing _agentPrefab property.";
				return false;
			}

			if (agentPrefab != null)
			{
				string agentPrefabPath = AssetDatabase.GetAssetPath(agentPrefab);
				string agentPrefabGuid = AssetDatabase.AssetPathToGUID(agentPrefabPath);
				if (TrySetNetworkPrefabGuid(prefabRef, agentPrefabGuid) == false)
				{
					error = "Unable to set NetworkPrefabRef RawGuidValue in AgentSettings.";
					return false;
				}
			}

			SetObjectReference(setup.FindPropertyRelative("_menuAgentPrefab"), menuAgentPrefab);

			settingsSO.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(settingsAsset);
			AssetDatabase.SaveAssets();
			return true;
		}

		private static bool GenerateAndSaveIconSprite(string agentName, GameObject characterPrefab, out Sprite iconSprite, out string error)
		{
			iconSprite = null;
			error = string.Empty;

			GameObject characterInstance = null;
			RenderTexture iconRT = null;
			Texture2D iconTexture = null;
			RenderTexture previousActiveRT = null;
			UnityScene stageScene = default;
			bool openedStageScene = false;
			Camera stageCamera = null;
			RenderTexture stageCameraOriginalTargetTexture = null;

			try
			{
				if (AssetDatabase.LoadAssetAtPath<SceneAsset>(STAGE_SCENE_PATH) == null)
				{
					error = $"Cannot find stage scene at {STAGE_SCENE_PATH}";
					return false;
				}

				stageScene = SceneManager.GetSceneByPath(STAGE_SCENE_PATH);
				if (stageScene.isLoaded == false)
				{
					stageScene = EditorSceneManager.OpenScene(STAGE_SCENE_PATH, OpenSceneMode.Additive);
					openedStageScene = true;
				}

				if (stageScene.IsValid() == false || stageScene.isLoaded == false)
				{
					error = $"Failed to load stage scene {STAGE_SCENE_PATH}";
					return false;
				}

				stageCamera = FindStageCaptureCamera(stageScene);
				if (stageCamera == null)
				{
					error = "No capture camera found in Stage scene. Please add/enable one camera in Stage.unity.";
					return false;
				}

				characterInstance = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
				if (characterInstance == null)
				{
					error = "Failed to instantiate character prefab for icon generation.";
					return false;
				}

				SceneManager.MoveGameObjectToScene(characterInstance, stageScene);
				characterInstance.name = characterPrefab.name;
				characterInstance.transform.position = Vector3.zero;
				characterInstance.transform.rotation = Quaternion.identity;
				characterInstance.transform.localScale = Vector3.one;

				if (TryResolveCharacterRig(characterInstance, out _, out error) == false)
					return false;

				int sampleCount = GetPathTracingSampleCount(stageScene);

				iconRT = RenderTexture.GetTemporary(ICON_RESOLUTION, ICON_RESOLUTION, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				stageCameraOriginalTargetTexture = stageCamera.targetTexture;
				stageCamera.targetTexture = iconRT;
				for (int i = 0; i < sampleCount; ++i)
				{
					stageCamera.Render();
					if ((i & 7) == 0)
					{
						EditorUtility.DisplayProgressBar("Generate Icon", $"Accumulating path tracing samples ({i + 1}/{sampleCount})", (float)(i + 1) / sampleCount);
					}
					EditorApplication.QueuePlayerLoopUpdate();
					SceneView.RepaintAll();
				}
				stageCamera.targetTexture = stageCameraOriginalTargetTexture;

				previousActiveRT = RenderTexture.active;
				RenderTexture.active = iconRT;

				iconTexture = new Texture2D(ICON_RESOLUTION, ICON_RESOLUTION, TextureFormat.RGBA32, false, false);
				iconTexture.ReadPixels(new Rect(0, 0, ICON_RESOLUTION, ICON_RESOLUTION), 0, 0, false);
				iconTexture.Apply(false, false);

				byte[] png = iconTexture.EncodeToPNG();
				if (png == null || png.Length == 0)
				{
					error = "Failed to encode generated icon PNG.";
					return false;
				}

				CreateFolderRecursively(ICON_OUTPUT_FOLDER);
				string fileName = $"{SanitizeFileName(agentName)}Icon.png";
				string assetPath = $"{ICON_OUTPUT_FOLDER}/{fileName}";
				string absolutePath = Path.GetFullPath(assetPath);
				string directoryPath = Path.GetDirectoryName(absolutePath);

				if (string.IsNullOrEmpty(directoryPath) == true)
				{
					error = $"Invalid icon output path: {assetPath}";
					return false;
				}

				Directory.CreateDirectory(directoryPath);
				File.WriteAllBytes(absolutePath, png);

				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
				TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
				if (importer != null)
				{
					importer.textureType = TextureImporterType.Sprite;
					importer.spriteImportMode = SpriteImportMode.Single;
					importer.alphaIsTransparency = true;
					importer.mipmapEnabled = false;
					importer.filterMode = FilterMode.Bilinear;
					importer.textureCompression = TextureImporterCompression.Compressed;
					importer.maxTextureSize = ICON_RESOLUTION;
					importer.SaveAndReimport();
				}

				iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
				if (iconSprite == null)
				{
					error = $"Failed to load generated icon sprite at {assetPath}";
					return false;
				}

				return true;
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				error = "Exception during icon generation. See Console for details.";
				return false;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				RenderTexture.active = previousActiveRT;
				if (stageCamera != null)
				{
					stageCamera.targetTexture = stageCameraOriginalTargetTexture;
				}

				if (iconRT != null)
				{
					RenderTexture.ReleaseTemporary(iconRT);
				}

				if (iconTexture != null)
				{
					DestroyImmediate(iconTexture);
				}

				if (characterInstance != null)
				{
					DestroyImmediate(characterInstance);
				}

				if (openedStageScene == true && stageScene.IsValid() == true && stageScene.isLoaded == true)
				{
					EditorSceneManager.CloseScene(stageScene, true);
				}
			}
		}

		private static bool TryGetCharacterBounds(Transform root, out Bounds bounds)
		{
			bounds = default;

			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
			bool hasBounds = false;

			for (int i = 0; i < renderers.Length; ++i)
			{
				if (renderers[i] is ParticleSystemRenderer)
					continue;

				if (hasBounds == false)
				{
					bounds = renderers[i].bounds;
					hasBounds = true;
				}
				else
				{
					bounds.Encapsulate(renderers[i].bounds);
				}
			}

			return hasBounds;
		}

		private static bool TryResolveCharacterRig(GameObject characterRoot, out CharacterRig rig, out string error)
		{
			rig = default;
			error = string.Empty;

			Animator animator = characterRoot.GetComponentInChildren<Animator>(true);
			if (animator == null)
			{
				error = "Character prefab has no Animator.";
				return false;
			}

			if (animator.avatar == null || animator.avatar.isHuman == false)
			{
				error = "Character prefab Animator must use a Humanoid avatar.";
				return false;
			}

			Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
			Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
			Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
			Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
			Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
			Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
			Transform leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
			Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
			Transform spine = animator.GetBoneTransform(HumanBodyBones.UpperChest);
			if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Chest);
			if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);

			if (hips == null || head == null || leftFoot == null || rightFoot == null || leftHand == null || rightHand == null || leftLowerArm == null || leftUpperArm == null)
			{
				error = "Character prefab is missing required humanoid bones (hips/head/feet/hands/left arm chain).";
				return false;
			}

			rig = new CharacterRig
			{
				CharacterRoot = characterRoot.transform,
				Animator = animator,
				Hips = hips,
				Spine = spine != null ? spine : hips,
				Head = head,
				LeftHand = leftHand,
				RightHand = rightHand,
				LeftLowerArm = leftLowerArm,
				LeftUpperArm = leftUpperArm,
				LeftFoot = leftFoot,
				RightFoot = rightFoot,
			};

			return true;
		}

		private static bool TryNormalizeAgentVisualsImport(GameObject agentVisuals, out string error)
		{
			error = string.Empty;

			if (agentVisuals == null)
			{
				error = "Agent Visuals is required.";
				return false;
			}

			if (TryGetModelAssetPathFromAgentVisuals(agentVisuals, out string modelPath) == false)
			{
				error = "Could not resolve a model asset from Agent Visuals.";
				return false;
			}

			ModelImporter modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
			if (modelImporter == null)
			{
				error = $"Resolved asset is not a ModelImporter: {modelPath}";
				return false;
			}

			try
			{
				modelImporter.animationType = ModelImporterAnimationType.Human;
				modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
				modelImporter.sourceAvatar = null;
				modelImporter.importAnimation = true;
				modelImporter.globalScale = 1.0f;
				modelImporter.useFileScale = true;
				modelImporter.bakeAxisConversion = false;
				modelImporter.preserveHierarchy = false;
				modelImporter.SaveAndReimport();
			}
			catch (Exception e)
			{
				error = $"Normalize import failed: {e.Message}";
				return false;
			}

			UnityEngine.Object modelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(modelPath);
			if (modelAsset != null)
			{
				Selection.activeObject = modelAsset;
				EditorGUIUtility.PingObject(modelAsset);
			}

			return true;
		}

		private static bool TryGetModelAssetPathFromAgentVisuals(GameObject agentVisuals, out string modelAssetPath)
		{
			modelAssetPath = string.Empty;
			if (agentVisuals == null)
				return false;

			string sourcePath = AssetDatabase.GetAssetPath(agentVisuals);
			if (string.IsNullOrWhiteSpace(sourcePath) == true)
				return false;

			string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
			if (extension == ".fbx" || extension == ".obj" || extension == ".dae" || extension == ".dxf" || extension == ".3ds")
			{
				if (AssetImporter.GetAtPath(sourcePath) is ModelImporter)
				{
					modelAssetPath = sourcePath;
					return true;
				}
			}

			if (extension != ".prefab")
				return false;

			GameObject prefabRoot = null;
			try
			{
				prefabRoot = PrefabUtility.LoadPrefabContents(sourcePath);
				if (prefabRoot == null)
					return false;

				Animator animator = prefabRoot.GetComponentInChildren<Animator>(true);
				if (animator != null)
				{
					GameObject sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(animator.gameObject);
					if (sourceObject != null)
					{
						string animatorSourcePath = AssetDatabase.GetAssetPath(sourceObject);
						if (string.IsNullOrWhiteSpace(animatorSourcePath) == false && AssetImporter.GetAtPath(animatorSourcePath) is ModelImporter)
						{
							modelAssetPath = animatorSourcePath;
							return true;
						}
					}
				}

				SkinnedMeshRenderer[] skinnedRenderers = prefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
				for (int i = 0; i < skinnedRenderers.Length; ++i)
				{
					Mesh mesh = skinnedRenderers[i].sharedMesh;
					if (mesh == null)
						continue;

					string meshPath = AssetDatabase.GetAssetPath(mesh);
					if (string.IsNullOrWhiteSpace(meshPath) == false && AssetImporter.GetAtPath(meshPath) is ModelImporter)
					{
						modelAssetPath = meshPath;
						return true;
					}
				}

				MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
				for (int i = 0; i < meshFilters.Length; ++i)
				{
					Mesh mesh = meshFilters[i].sharedMesh;
					if (mesh == null)
						continue;

					string meshPath = AssetDatabase.GetAssetPath(mesh);
					if (string.IsNullOrWhiteSpace(meshPath) == false && AssetImporter.GetAtPath(meshPath) is ModelImporter)
					{
						modelAssetPath = meshPath;
						return true;
					}
				}
			}
			finally
			{
				if (prefabRoot != null)
				{
					PrefabUtility.UnloadPrefabContents(prefabRoot);
				}
			}

			return false;
		}

		private static void AssignCharacterView(GameObject agentInstance, CharacterRig rig)
		{
			Character character = agentInstance.GetComponent<Character>();
			if (character == null)
				return;

			SerializedObject characterSO = new SerializedObject(character);
			SerializedProperty thirdPersonView = characterSO.FindProperty("_thirdPersonView");
			if (thirdPersonView == null)
				return;

			SetObjectReference(thirdPersonView.FindPropertyRelative("RootBone"), rig.Hips);
			SetObjectReference(thirdPersonView.FindPropertyRelative("HeadTransform"), rig.Head);
			SetObjectReference(thirdPersonView.FindPropertyRelative("LeftFoot"), rig.LeftFoot);
			SetObjectReference(thirdPersonView.FindPropertyRelative("RightFoot"), rig.RightFoot);
			SetObjectReference(thirdPersonView.FindPropertyRelative("LeftHand"), rig.LeftHand);
			SetObjectReference(thirdPersonView.FindPropertyRelative("RightHand"), rig.RightHand);

			Transform weaponHandlePistol = FindDeepChild(rig.CharacterRoot, "WeaponHandlePistol");
			if (weaponHandlePistol != null)
			{
				SetObjectReference(thirdPersonView.FindPropertyRelative("WeaponHandle"), weaponHandlePistol);
			}

			characterSO.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void AssignAnimationController(GameObject agentInstance, CharacterRig rig)
		{
			CharacterAnimationController animationController = agentInstance.GetComponent<CharacterAnimationController>();
			if (animationController == null)
				return;

			SerializedObject animationSO = new SerializedObject(animationController);
			SetObjectReference(animationSO.FindProperty("_animator"), rig.Animator);
			SetObjectReference(animationSO.FindProperty("_leftHand"), rig.LeftHand);
			SetObjectReference(animationSO.FindProperty("_leftLowerArm"), rig.LeftLowerArm);
			SetObjectReference(animationSO.FindProperty("_leftUpperArm"), rig.LeftUpperArm);

			SerializedProperty animationRootProperty = animationSO.FindProperty("_root");
			if (animationRootProperty != null)
			{
				Transform fusionAnimatorRoot = FindDeepChild(agentInstance.transform, "FusionAnimatorRoot");
				if (fusionAnimatorRoot == null)
				{
					GameObject rootObject = new GameObject("FusionAnimatorRoot");
					fusionAnimatorRoot = rootObject.transform;
					Transform parent = rig.CharacterRoot != null ? rig.CharacterRoot : agentInstance.transform;
					fusionAnimatorRoot.SetParent(parent, false);
					fusionAnimatorRoot.localPosition = Vector3.zero;
					fusionAnimatorRoot.localRotation = Quaternion.identity;
					fusionAnimatorRoot.localScale = Vector3.one;
				}

				animationRootProperty.objectReferenceValue = fusionAnimatorRoot;
			}

			SerializedProperty useFusionAnimatorGraph = animationSO.FindProperty("_useFusionAnimatorGraph");
			SerializedProperty fusionAnimatorGraph = animationSO.FindProperty("_fusionAnimatorGraph");
			SerializedProperty fusionControlShootLayer = animationSO.FindProperty("_fusionControlShootLayer");
			if (useFusionAnimatorGraph != null && fusionAnimatorGraph != null)
			{
				UnityEngine.Object fusionGraphAsset = AssetDatabase.LoadMainAssetAtPath(FUSION_ANIMATOR_GRAPH_PATH);
				fusionAnimatorGraph.objectReferenceValue = fusionGraphAsset;
				useFusionAnimatorGraph.boolValue = fusionGraphAsset != null;
				if (fusionControlShootLayer != null)
				{
					fusionControlShootLayer.boolValue = fusionGraphAsset != null;
				}
			}

			animationSO.ApplyModifiedPropertiesWithoutUndo();

			if (rig.Animator != null)
			{
				SerializedObject animatorSO = new SerializedObject(rig.Animator);
				SetObjectReference(animatorSO.FindProperty("m_Avatar"), rig.Animator.avatar);
				SerializedProperty applyRootMotion = animatorSO.FindProperty("m_ApplyRootMotion");
				if (applyRootMotion != null)
				{
					applyRootMotion.boolValue = false;
				}
				animatorSO.ApplyModifiedPropertiesWithoutUndo();
			}
		}

		private static void AssignWeaponSlots(GameObject agentInstance, CharacterRig rig)
		{
			Weapons weapons = agentInstance.GetComponent<Weapons>();
			if (weapons == null)
				return;

			Transform rightHand = rig.RightHand;
			Transform hips = rig.Hips;
			Transform spine = rig.Spine;

			Transform weaponHandlePistol = CreateOrGetHandle("WeaponHandlePistol", rightHand, new Vector3(0.015f, -0.067f, 0.028f), Quaternion.identity);
			Transform weaponHandleRifle = CreateOrGetHandle("WeaponHandleRifle", rightHand, new Vector3(0.0f, -0.067f, 0.045f), Quaternion.identity);
			Transform backHandle = CreateOrGetHandle("BackHandle", spine, new Vector3(-0.11f, 0.19f, -0.04f), Quaternion.identity);
			Transform beltHandle = CreateOrGetHandle("BeltHandle", hips, new Vector3(0.0f, 0.0f, 0.185f), Quaternion.identity);
			Transform grenadeHandle1 = CreateOrGetHandle("GrenadeHandle1", hips, new Vector3(-0.035f, -0.117f, 0.101f), Quaternion.identity);
			Transform grenadeHandle2 = CreateOrGetHandle("GrenadeHandle2", hips, new Vector3(-0.018f, -0.117f, 0.016f), Quaternion.identity);
			Transform grenadeHandle3 = CreateOrGetHandle("GrenadeHandle3", hips, new Vector3(0.002f, -0.117f, -0.087f), Quaternion.identity);

			SerializedObject weaponsSO = new SerializedObject(weapons);
			SerializedProperty slots = weaponsSO.FindProperty("_slots");
			if (slots == null)
				return;

			slots.arraySize = SLOT_COUNT;

			Transform[] active =
			{
				weaponHandlePistol,
				weaponHandlePistol,
				weaponHandleRifle,
				null,
				null,
				weaponHandlePistol,
				weaponHandlePistol,
				weaponHandlePistol
			};

			Transform[] inactive =
			{
				backHandle,
				beltHandle,
				backHandle,
				null,
				null,
				grenadeHandle1,
				grenadeHandle2,
				grenadeHandle3
			};

			for (int i = 0; i < SLOT_COUNT; ++i)
			{
				SerializedProperty slot = slots.GetArrayElementAtIndex(i);
				SetObjectReference(slot.FindPropertyRelative("Active"), active[i]);
				SetObjectReference(slot.FindPropertyRelative("Inactive"), inactive[i]);
			}

			weaponsSO.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void AssignBodyPartsAndHitboxes(GameObject agentInstance, CharacterRig rig)
		{
			HitboxRoot hitboxRoot = agentInstance.GetComponent<HitboxRoot>();
			if (hitboxRoot == null)
				return;

			List<BodyPart> bodyParts = new List<BodyPart>(16);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.Head), 2.0f, true, 0.18f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.Chest), 1.0f, false, 0.20f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.Spine), 1.0f, false, 0.20f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.Hips), 1.0f, false, 0.22f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), 1.0f, false, 0.14f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), 1.0f, false, 0.12f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm), 1.0f, false, 0.14f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm), 1.0f, false, 0.12f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), 1.0f, false, 0.16f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), 1.0f, false, 0.14f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), 1.0f, false, 0.16f);
			AddBodyPart(bodyParts, rig.Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), 1.0f, false, 0.14f);

			SerializedObject hitboxSO = new SerializedObject(hitboxRoot);
			SerializedProperty hitboxes = hitboxSO.FindProperty("Hitboxes");
			if (hitboxes != null)
			{
				hitboxes.arraySize = bodyParts.Count;
				for (int i = 0; i < bodyParts.Count; ++i)
				{
					hitboxes.GetArrayElementAtIndex(i).objectReferenceValue = bodyParts[i];
				}
			}

			Renderer[] renderers = rig.CharacterRoot.GetComponentsInChildren<Renderer>(true);
			Bounds bounds = default;
			bool hasBounds = false;
			for (int i = 0; i < renderers.Length; ++i)
			{
				if (hasBounds == false)
				{
					bounds = renderers[i].bounds;
					hasBounds = true;
				}
				else
				{
					bounds.Encapsulate(renderers[i].bounds);
				}
			}

			if (hasBounds == true)
			{
				SerializedProperty broadRadius = hitboxSO.FindProperty("BroadRadius");
				SerializedProperty offset = hitboxSO.FindProperty("Offset");
				if (broadRadius != null)
				{
					broadRadius.floatValue = Mathf.Max(1.0f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 2.0f);
				}
				if (offset != null)
				{
					Vector3 localCenter = agentInstance.transform.InverseTransformPoint(bounds.center);
					offset.vector3Value = new Vector3(0.0f, Mathf.Max(1.0f, localCenter.y), 0.0f);
				}
			}

			hitboxSO.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void ReplaceCharacterUnderVisualsRoot(Transform visualsRoot)
		{
			for (int i = visualsRoot.childCount - 1; i >= 0; --i)
			{
				Transform child = visualsRoot.GetChild(i);
				if (IsCharacterHierarchy(child) == true)
				{
					DestroyImmediate(child.gameObject);
				}
			}
		}

		private static bool IsCharacterHierarchy(Transform root)
		{
			if (root == null)
				return false;

			Animator animator = root.GetComponentInChildren<Animator>(true);
			if (animator == null)
				return false;

			if (animator.avatar != null && animator.avatar.isHuman == true)
				return true;

			SkinnedMeshRenderer skinnedMesh = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
			return skinnedMesh != null;
		}

		private static void AddBodyPart(List<BodyPart> bodyParts, Transform bone, float damageMultiplier, bool isCritical, float broadRadius)
		{
			if (bone == null)
				return;

			BodyPart bodyPart = bone.GetComponent<BodyPart>();
			if (bodyPart == null)
			{
				bodyPart = bone.gameObject.AddComponent<BodyPart>();
			}

			SerializedObject bodyPartSO = new SerializedObject(bodyPart);
			SerializedProperty damage = bodyPartSO.FindProperty("_damageMultiplier");
			if (damage != null) damage.floatValue = damageMultiplier;
			SerializedProperty critical = bodyPartSO.FindProperty("_isCritical");
			if (critical != null) critical.boolValue = isCritical;
			SerializedProperty radius = bodyPartSO.FindProperty("BroadRadius");
			if (radius != null) radius.floatValue = broadRadius;
			SerializedProperty offset = bodyPartSO.FindProperty("Offset");
			if (offset != null) offset.vector3Value = Vector3.zero;
			bodyPartSO.ApplyModifiedPropertiesWithoutUndo();
			bodyParts.Add(bodyPart);
		}

		private static Transform CreateOrGetHandle(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
		{
			if (parent == null) return null;
			Transform handle = parent.Find(name);
			if (handle == null)
			{
				GameObject handleObject = new GameObject(name);
				handle = handleObject.transform;
				handle.SetParent(parent, false);
			}
			handle.localPosition = localPosition;
			handle.localRotation = localRotation;
			handle.localScale = Vector3.one;
			handle.gameObject.layer = parent.gameObject.layer;
			handle.tag = parent.tag;
			return handle;
		}

		private static Transform FindDeepChild(Transform root, string name)
		{
			if (root == null) return null;
			Transform[] children = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < children.Length; ++i)
			{
				if (children[i].name == name) return children[i];
			}
			return null;
		}

		private static int FindAgentSetupIndex(SerializedProperty agents, string id)
		{
			for (int i = 0; i < agents.arraySize; ++i)
			{
				SerializedProperty setup = agents.GetArrayElementAtIndex(i);
				SerializedProperty idProperty = setup.FindPropertyRelative("_id");
				if (idProperty != null && idProperty.stringValue == id) return i;
			}
			return -1;
		}

		private static bool TrySetNetworkPrefabGuid(SerializedProperty prefabProperty, string guid)
		{
			if (prefabProperty == null || string.IsNullOrWhiteSpace(guid) == true)
				return false;

			try
			{
				Type guidDrawerType = Type.GetType("Fusion.Editor.NetworkObjectGuidDrawer, Fusion.Unity.Editor");
				MethodInfo setValueMethod = guidDrawerType != null ? guidDrawerType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static) : null;
				if (setValueMethod != null)
				{
					NetworkObjectGuid parsedGuid = NetworkObjectGuid.Parse(guid);
					setValueMethod.Invoke(null, new object[] { prefabProperty, parsedGuid });
					return true;
				}
			}
			catch
			{
				// Fall back to direct serialized assignment below.
			}

			SerializedProperty rawGuid = prefabProperty.FindPropertyRelative("RawGuidValue");
			if (rawGuid != null && rawGuid.propertyType == SerializedPropertyType.String)
			{
				rawGuid.stringValue = guid;
				return true;
			}

			SerializedProperty rawGuidAlt = prefabProperty.FindPropertyRelative("_rawGuidValue");
			if (rawGuidAlt != null && rawGuidAlt.propertyType == SerializedPropertyType.String)
			{
				rawGuidAlt.stringValue = guid;
				return true;
			}

			return false;
		}

		private static void SetObjectReference(SerializedProperty property, UnityEngine.Object value)
		{
			if (property != null) property.objectReferenceValue = value;
		}

		private static void SetString(SerializedProperty property, string value)
		{
			if (property != null) property.stringValue = value;
		}

		private static void SetLayerRecursively(Transform root, int layer)
		{
			if (root == null) return;
			root.gameObject.layer = layer;
			for (int i = 0; i < root.childCount; ++i) SetLayerRecursively(root.GetChild(i), layer);
		}

		private static int GetAgentLayer()
		{
			int layer = LayerMask.NameToLayer("Agent");
			return layer >= 0 ? layer : DEFAULT_AGENT_LAYER;
		}

		private static string GetMenuPrefabName(string agentName)
		{
			return agentName.StartsWith("Menu", StringComparison.Ordinal) ? agentName : $"Menu{agentName}";
		}

		private static string GetAgentId(string agentName)
		{
			string token = agentName.Replace(" ", string.Empty);
			return $"Agent.{token}";
		}

		private static string GetDisplayName(string agentName)
		{
			return ObjectNames.NicifyVariableName(agentName);
		}

		private static string SanitizeFileName(string value)
		{
			if (string.IsNullOrWhiteSpace(value) == true) return "Agent";
			char[] invalidChars = Path.GetInvalidFileNameChars();
			string sanitized = value.Trim();
			for (int i = 0; i < invalidChars.Length; ++i) sanitized = sanitized.Replace(invalidChars[i], '_');
			sanitized = sanitized.Replace(" ", string.Empty);
			return string.IsNullOrWhiteSpace(sanitized) ? "Agent" : sanitized;
		}

		private static void CreateFolderRecursively(string folderPath)
		{
			string normalized = folderPath.Replace("\\", "/");
			if (AssetDatabase.IsValidFolder(normalized) == true) return;
			string[] parts = normalized.Split('/');
			if (parts.Length < 2 || parts[0] != "Assets") throw new InvalidOperationException($"Output folder must be under Assets. Value: {folderPath}");
			string current = parts[0];
			for (int i = 1; i < parts.Length; ++i)
			{
				string next = $"{current}/{parts[i]}";
				if (AssetDatabase.IsValidFolder(next) == false) AssetDatabase.CreateFolder(current, parts[i]);
				current = next;
			}
		}

		private static Camera FindStageCaptureCamera(UnityScene stageScene)
		{
			if (stageScene.IsValid() == false || stageScene.isLoaded == false)
				return null;

			GameObject[] roots = stageScene.GetRootGameObjects();
			Camera fallbackCamera = null;

			for (int i = 0; i < roots.Length; ++i)
			{
				Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
				for (int j = 0; j < cameras.Length; ++j)
				{
					Camera camera = cameras[j];
					if (camera == null)
						continue;
					if (camera.enabled == false || camera.gameObject.activeInHierarchy == false)
						continue;

					if (camera.CompareTag("MainCamera") == true)
						return camera;

					if (fallbackCamera == null)
					{
						fallbackCamera = camera;
					}
				}
			}

			return fallbackCamera;
		}

		private static int GetPathTracingSampleCount(UnityScene stageScene)
		{
			int maxSamples = 0;
			bool foundPathTracing = false;

			if (stageScene.IsValid() == false || stageScene.isLoaded == false)
				return 0;

			GameObject[] roots = stageScene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; ++i)
			{
				Volume[] volumes = roots[i].GetComponentsInChildren<Volume>(true);
				for (int j = 0; j < volumes.Length; ++j)
				{
					Volume volume = volumes[j];
					if (volume == null || volume.enabled == false)
						continue;

					VolumeProfile profile = volume.sharedProfile != null ? volume.sharedProfile : volume.profile;
					if (profile == null)
						continue;

					List<VolumeComponent> components = profile.components;
					for (int c = 0; c < components.Count; ++c)
					{
						VolumeComponent component = components[c];
						if (component == null)
							continue;
						if (component.GetType().Name.Contains("PathTracing") == false)
							continue;

						PropertyInfo maxSamplesProperty = component.GetType().GetProperty("maximumSamples", BindingFlags.Public | BindingFlags.Instance);
						if (maxSamplesProperty == null)
							continue;

						object maxSamplesParameter = maxSamplesProperty.GetValue(component);
						if (maxSamplesParameter == null)
							continue;

						PropertyInfo valueProperty = maxSamplesParameter.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
						if (valueProperty == null)
							continue;

						object value = valueProperty.GetValue(maxSamplesParameter);
						if (value is int sampleCount)
						{
							foundPathTracing = true;
							if (sampleCount > maxSamples)
							{
								maxSamples = sampleCount;
							}
						}
					}
				}
			}

			if (foundPathTracing == false)
				return 0;

			return Mathf.Clamp(maxSamples, 1, 16384);
		}

		private sealed class IconGenerationJob
		{
			public string AgentName;
			public UnityScene StageScene;
			public bool OpenedStageScene;
			public Camera StageCamera;
			public RenderTexture StageCameraOriginalTargetTexture;
			public GameObject CharacterInstance;
			public RenderTexture IconRT;
			public Texture2D IconTexture;
			public bool UsesPathTracing;
			public int SampleCount;
			public int SamplesRendered;
		}

		private struct CharacterRig
		{
			public Transform CharacterRoot;
			public Animator Animator;
			public Transform Hips;
			public Transform Spine;
			public Transform Head;
			public Transform LeftHand;
			public Transform RightHand;
			public Transform LeftLowerArm;
			public Transform LeftUpperArm;
			public Transform LeftFoot;
			public Transform RightFoot;
		}
	}
}
