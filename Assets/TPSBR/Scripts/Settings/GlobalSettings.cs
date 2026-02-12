using UnityEngine;
using System;
using Fusion;
using UnityEngine.InputSystem;

namespace TPSBR
{
	[Serializable]
	[CreateAssetMenu(fileName = "GlobalSettings", menuName = "TPSBR/Global Settings")]
	public class GlobalSettings : ScriptableObject
	{
		public NetworkRunner        RunnerPrefab;
		public InputActionAsset     PlayerInputActions;
		public string               LoadingScene = "LoadingScene";
		public string               MenuScene = "Menu";
		public bool                 SimulateMobileInput;

		public AgentSettings        Agent;
		public MapSettings          Map;
		public NetworkSettings      Network;
		public OptionsData          DefaultOptions;
	}
}
