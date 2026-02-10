namespace TPSBR.UI
{
	using UnityEngine;
	using UnityEngine.InputSystem;
	using TMPro;

	public class UIDeathView : UIView
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private Transform       _respawnGroup;
		[SerializeField]
		private TextMeshProUGUI _respawnTime;

		[SerializeField]
		private Transform       _spectatorGroup;
		private InputActionAsset _actionsAsset;
		private InputAction _spectatePrevAction;
		private InputAction _spectateNextAction;

		// UIView INTERAFCE

		protected override void OnOpen()
		{
			base.OnOpen();

			Refresh();
		}

		protected override void OnTick()
		{
			base.OnTick();

			Refresh();
		}

		private void Refresh()
		{
			ResolveInputActions();

			if (Context.Runner == null || Context.Runner.Exists(Context.GameplayMode.Object) == false)
				return;

			var player = Context.NetworkGame.GetPlayer(Context.LocalPlayerRef);
			var statistics = player != null ? player.Statistics : default;

			if (statistics.IsEliminated == false)
			{
				_respawnGroup.SetActive(true);
				_respawnTime.text = $"{statistics.RespawnTimer.RemainingTime(Context.Runner):F1} s";

				_spectatorGroup.SetActive(false);
			}
			else
			{
				_respawnGroup.SetActive(false);
				_spectatorGroup.SetActive(true);

				if (_spectateNextAction != null && _spectateNextAction.WasPressedThisFrame())
				{
					Context.GameplayMode.ChangeSpectatorTarget(true);
				}
				else if (_spectatePrevAction != null && _spectatePrevAction.WasPressedThisFrame())
				{
					Context.GameplayMode.ChangeSpectatorTarget(false);
				}
			}
		}

		private void ResolveInputActions()
		{
			if (_actionsAsset == null)
			{
				_actionsAsset = InputActionsResolver.ResolveActionAsset();
			}

			if (_actionsAsset == null)
				return;

			_spectatePrevAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "SpectatePrev");
			_spectateNextAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "SpectateNext");
		}
	}
}
