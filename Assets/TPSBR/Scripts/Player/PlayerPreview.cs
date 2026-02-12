using UnityEngine;

namespace TPSBR
{
	public class PlayerPreview : CoreBehaviour
	{
		// PUBLIC MEMBERS

		public string AgentID => _agentID;

		// PRIVATE MEMBERS

		[SerializeField]
		private Transform _agentParent;

		private string _agentID;
		private GameObject _agentInstance;

		// PUBLIC METHODS

		public void ShowAgent(string agentID, bool force = false)
		{
			if (agentID == _agentID && force == false)
				return;

			ClearAgent();
			InstantiateAgent(agentID);
		}

		public void ShowOutline(bool value)
		{
			// Outline plugin has been removed.
		}

		public void HideAgent()
		{
			ClearAgent();
		}

		// MONOBEHAVIOUR

		protected void Awake()
		{
		}

		// PRIVATE METHODS

		private void InstantiateAgent(string agentID)
		{
			if (agentID.HasValue() == false)
				return;

			var agentSetup = Global.Settings.Agent.GetAgentSetup(agentID);

			if (agentSetup == null)
				return;

			_agentInstance = Instantiate(agentSetup.MenuAgentPrefab, _agentParent);
			_agentID = agentID;
		}

		private void ClearAgent()
		{
			_agentID = null;

			if (_agentInstance == null)
				return;

			Destroy(_agentInstance);
			_agentInstance = null;
		}
	}
}
