using UnityEngine;
using System;
using Fusion;

namespace TPSBR
{
	[Serializable]
	[CreateAssetMenu(fileName = "AgentSettings", menuName = "TPSBR/Agent Settings")]
	public class AgentSettings : ScriptableObject
	{
		// PUBLIC MEMBERS

		public AgentSetup[] Agents => _agents;

		// PRIVATE MEMBERS

		[SerializeField]
		private AgentSetup[] _agents;

		// PUBLIC METHODS

		public AgentSetup GetAgentSetup(string agentID)
		{
			if (agentID.HasValue() == false)
				return null;

			if (_agents == null || _agents.Length == 0)
				return null;

			return _agents.Find(t => t != null && t.ID == agentID);
		}

		public AgentSetup GetAgentSetup(NetworkPrefabRef prefabId)
		{
			if (prefabId.IsValid == false)
				return null;

			if (_agents == null || _agents.Length == 0)
				return null;

			return _agents.Find(t => t != null && t.AgentPrefab == prefabId);
		}

		public AgentSetup GetRandomAgentSetup()
		{
			if (_agents == null || _agents.Length == 0)
				return null;

			int validCount = 0;
			for (int i = 0; i < _agents.Length; ++i)
			{
				if (IsSelectableAgentSetup(_agents[i]) == true)
				{
					++validCount;
				}
			}

			if (validCount <= 0)
				return null;

			int randomIndex = UnityEngine.Random.Range(0, validCount);
			for (int i = 0; i < _agents.Length; ++i)
			{
				AgentSetup setup = _agents[i];
				if (IsSelectableAgentSetup(setup) == false)
					continue;

				if (randomIndex == 0)
					return setup;

				--randomIndex;
			}

			return null;
		}

		public AgentSetup GetRandomSpawnableAgentSetup()
		{
			if (_agents == null || _agents.Length == 0)
				return null;

			int validCount = 0;
			for (int i = 0; i < _agents.Length; ++i)
			{
				if (IsSpawnableAgentSetup(_agents[i]) == true)
				{
					++validCount;
				}
			}

			if (validCount <= 0)
				return null;

			int randomIndex = UnityEngine.Random.Range(0, validCount);
			for (int i = 0; i < _agents.Length; ++i)
			{
				AgentSetup setup = _agents[i];
				if (IsSpawnableAgentSetup(setup) == false)
					continue;

				if (randomIndex == 0)
					return setup;

				--randomIndex;
			}

			return null;
		}

		public AgentSetup GetFirstSelectableAgentSetup()
		{
			if (_agents == null || _agents.Length == 0)
				return null;

			for (int i = 0; i < _agents.Length; ++i)
			{
				AgentSetup setup = _agents[i];
				if (IsSelectableAgentSetup(setup) == true)
					return setup;
			}

			return null;
		}

		public AgentSetup GetFirstSpawnableAgentSetup()
		{
			if (_agents == null || _agents.Length == 0)
				return null;

			for (int i = 0; i < _agents.Length; ++i)
			{
				AgentSetup setup = _agents[i];
				if (IsSpawnableAgentSetup(setup) == true)
					return setup;
			}

			return null;
		}

		public bool IsSelectableAgentSetup(AgentSetup setup)
		{
			return setup != null &&
				setup.ID.HasValue() == true;
		}

		public bool IsSpawnableAgentSetup(AgentSetup setup)
		{
			return IsSelectableAgentSetup(setup) == true &&
				setup.AgentPrefab.IsValid == true;
		}
	}

	[Serializable]
	public class AgentSetup
	{
		// PUBLIC MEMBERS

		public string               ID                => _id;
		public string               DisplayName       => _displayName;
		public string               Description       => _description;
		public Sprite               Icon              => _icon;
		public NetworkPrefabRef     AgentPrefab       => _agentPrefab;
		public GameObject           MenuAgentPrefab   => _menuAgentPrefab;

		// PRIVATE MEMBERS

		[SerializeField]
		private string _id;
		[SerializeField]
		private string _displayName;
		[SerializeField, TextArea(3, 6)]
		private string _description;
		[SerializeField]
		private Sprite _icon;
		[SerializeField]
		private NetworkPrefabRef _agentPrefab;
		[SerializeField]
		private GameObject _menuAgentPrefab;
	}
}
