using System;
using TMPro;
using UnityEngine;
using Unity.Cinemachine;

namespace TPSBR.UI
{
	public class UIAgentSelectionView : UICloseView
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private CinemachineCamera _camera;
		[SerializeField]
		private UIList _agentList;
		[SerializeField]
		private UIButton _selectButton;
		[SerializeField]
		private TextMeshProUGUI _agentName;
		[SerializeField]
		private TextMeshProUGUI _agentDescription;
		[SerializeField]
		private string _agentNameFormat = "{0}";
		[SerializeField]
		private UIBehaviour _selectedAgentGroup;
		[SerializeField]
		private UIBehaviour _selectedEffect;
		[SerializeField]
		private AudioSetup _selectedSound;
		[SerializeField]
		private float _closeDelayAfterSelection = 0.5f;

		private string _previewAgent;

		// UIView INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			if (_agentList != null)
			{
				_agentList.SelectionChanged += OnSelectionChanged;
				_agentList.UpdateContent += OnListUpdateContent;
			}

			if (_selectButton != null)
			{
				_selectButton.onClick.AddListener(OnSelectButton);
			}
		}

		private void OnListUpdateContent(int index, MonoBehaviour content)
		{
			var behaviour = content as UIBehaviour;
			var setup = GetSetupAt(index);
			if (behaviour == null)
				return;

			behaviour.Image.sprite = setup != null ? setup.Icon : null;
		}

		protected override void OnOpen()
		{
			base.OnOpen();

			CancelInvoke(nameof(CloseWithBack));

			if (_camera != null)
			{
				_camera.enabled = true;
			}
			if (_selectedEffect != null)
			{
				_selectedEffect.SetActive(false);
			}

			RepairSelectionState();

			_previewAgent = Context.PlayerData.AgentID;

			if (_agentList != null)
			{
				int setupCount = Context.Settings.Agent.Agents != null ? Context.Settings.Agent.Agents.Length : 0;
				_agentList.Refresh(setupCount, false);
			}
			
			UpdateAgent();
		}

		protected override void OnClose()
		{
			CancelInvoke(nameof(CloseWithBack));

			if (_camera != null)
			{
				_camera.enabled = false;
			}
			if (Context != null && Context.PlayerPreview != null && Context.PlayerData != null)
			{
				Context.PlayerPreview.ShowAgent(Context.PlayerData.AgentID);
			}

			base.OnClose();
		}

		protected override void OnDeinitialize()
		{
			if (_agentList != null)
			{
				_agentList.SelectionChanged -= OnSelectionChanged;
				_agentList.UpdateContent -= OnListUpdateContent;
			}

			if (_selectButton != null)
			{
				_selectButton.onClick.RemoveListener(OnSelectButton);
			}

			base.OnDeinitialize();
		}

		// PRIVATE METHODS

		private void OnSelectionChanged(int index)
		{
			AgentSetup setup = GetSetupAt(index);
			if (Context.Settings.Agent.IsSelectableAgentSetup(setup) == false)
				return;

			_previewAgent = setup.ID;
			UpdateAgent();
		}

		private void OnSelectButton()
		{
			AgentSetup setup = ResolvePreviewAgentSetup();
			if (setup == null)
			{
				UpdateAgent();
				return;
			}

			bool isSame = Context.PlayerData.AgentID == setup.ID;

			if (isSame == false)
			{
				Context.PlayerData.AgentID = setup.ID;
				_previewAgent = setup.ID;

				_selectedEffect.SetActive(false);
				_selectedEffect.SetActive(true);

				PlaySound(_selectedSound);

				UpdateAgent();
				Invoke("CloseWithBack", _closeDelayAfterSelection);
			}
			else
			{
				CloseWithBack();
			}
		}

		private void UpdateAgent()
		{
			if (Context == null || Context.Settings == null || Context.Settings.Agent == null || Context.PlayerData == null)
			{
				if (_agentName != null) _agentName.text = string.Empty;
				if (_agentDescription != null) _agentDescription.text = string.Empty;
				if (_selectedAgentGroup != null) _selectedAgentGroup.SetActive(false);
				if (_selectButton != null) _selectButton.interactable = false;
				return;
			}

			var agentSetups = Context.Settings.Agent.Agents;
			if (agentSetups == null || agentSetups.Length == 0)
			{
				if (Context.PlayerPreview != null) Context.PlayerPreview.HideAgent();
				if (_agentName != null) _agentName.text = string.Empty;
				if (_agentDescription != null) _agentDescription.text = string.Empty;
				if (_selectedAgentGroup != null) _selectedAgentGroup.SetActive(false);
				if (_selectButton != null) _selectButton.interactable = false;
				return;
			}

			AgentSetup selectedSetup = ResolvePreviewAgentSetup();
			if (selectedSetup == null || _previewAgent.HasValue() == false)
			{
				if (Context.PlayerPreview != null) Context.PlayerPreview.HideAgent();
				if (_agentName != null) _agentName.text = string.Empty;
				if (_agentDescription != null) _agentDescription.text = string.Empty;
				if (_selectedAgentGroup != null) _selectedAgentGroup.SetActive(false);
				if (_selectButton != null) _selectButton.interactable = false;
				return;
			}

			if (_agentList != null)
			{
				_agentList.Selection = Array.FindIndex(agentSetups, t => t != null && t.ID == _previewAgent);
				if (_agentList.Selection < 0)
				{
					_agentList.Selection = GetFirstSelectableSetupIndex();
				}
			}

			if (selectedSetup.MenuAgentPrefab != null)
			{
				Context.PlayerPreview.ShowAgent(_previewAgent);
			}
			else
			{
				Context.PlayerPreview.HideAgent();
			}

			if (_agentName != null) _agentName.text = string.Format(_agentNameFormat, selectedSetup.DisplayName);
			if (_agentDescription != null) _agentDescription.text = selectedSetup.Description;
			if (_selectedAgentGroup != null) _selectedAgentGroup.SetActive(_previewAgent == Context.PlayerData.AgentID);
			if (_selectButton != null) _selectButton.interactable = true;
		}

		private AgentSetup ResolvePreviewAgentSetup()
		{
			AgentSettings settings = Context.Settings.Agent;
			AgentSetup setup = settings.GetAgentSetup(_previewAgent);
			if (settings.IsSelectableAgentSetup(setup) == true)
				return setup;

			setup = settings.GetFirstSelectableAgentSetup();
			_previewAgent = setup != null ? setup.ID : default;
			return setup;
		}

		private int GetFirstSelectableSetupIndex()
		{
			AgentSetup[] setups = Context.Settings.Agent.Agents;
			if (setups == null)
				return -1;

			for (int i = 0; i < setups.Length; ++i)
			{
				if (Context.Settings.Agent.IsSelectableAgentSetup(setups[i]) == true)
					return i;
			}

			return -1;
		}

		private AgentSetup GetSetupAt(int index)
		{
			AgentSetup[] setups = Context.Settings.Agent.Agents;
			if (setups == null || index < 0 || index >= setups.Length)
				return null;

			return setups[index];
		}

		private void RepairSelectionState()
		{
			if (Context == null || Context.Settings == null || Context.Settings.Agent == null || Context.PlayerData == null)
				return;

			AgentSettings settings = Context.Settings.Agent;
			AgentSetup selectedSetup = settings.GetAgentSetup(Context.PlayerData.AgentID);
			if (settings.IsSelectableAgentSetup(selectedSetup) == true)
				return;

			AgentSetup fallbackSetup = settings.GetFirstSelectableAgentSetup();
			if (fallbackSetup != null)
			{
				Context.PlayerData.AgentID = fallbackSetup.ID;
			}
		}
	}
}
