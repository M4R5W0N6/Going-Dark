namespace TPSBR
{
	using System;
	using UnityEngine;
	using Fusion;

	[DefaultExecutionOrder(-8)]
	public sealed class Interactions : ContextBehaviour
	{
		public IInteraction InteractionTarget { get; private set; }
		public float        ItemDropTime      => _itemDropTime;

		[Networked, HideInInspector]
		public TickTimer DropItemTimer { get; private set; }

		public event Action<string> InteractionFailed;

		[SerializeField]
		private LayerMask _interactionMask;
		[SerializeField]
		private float _interactionDistance = 2f;
		[SerializeField]
		private float _interactionPrecisionRadius = 0.3f;
		[SerializeField]
		private float _itemDropTime;

		private Health _health;
		private Weapons _weapons;
		private Character _character;
		private Aiming _aiming;
		private RaycastHit[] _interactionHits = new RaycastHit[10];

		public void TryInteract(bool interact, bool hold)
		{
			if (hold == false)
			{
				DropItemTimer = default;
			}

			if (_weapons.IsSwitchingWeapon() == true)
			{
				DropItemTimer = default;
				return;
			}

			if (_weapons.CurrentWeapon != null && _weapons.CurrentWeapon.IsBusy() == true)
			{
				DropItemTimer = default;
				return;
			}

			if (HasStateAuthority == false)
				return;

			UpdateInteractionTarget();

			if (InteractionTarget == null)
			{
				if (hold == false)
					return;

				if (DropItemTimer.IsRunning == false && _weapons.CurrentWeaponSlot > 0 && interact == true)
				{
					DropItemTimer = TickTimer.CreateFromSeconds(Runner, _itemDropTime);
				}

				if (DropItemTimer.Expired(Runner) == true)
				{
					DropItemTimer = default;
					_weapons.DropCurrentWeapon();
				}

				return;
			}

			if (interact == false)
				return;

			if (InteractionTarget is DynamicPickup dynamicPickup && dynamicPickup.Provider is Weapon pickupWeapon)
			{
				_weapons.Pickup(dynamicPickup, pickupWeapon);
			}
			else if (InteractionTarget is WeaponPickup weaponPickup)
			{
				_weapons.Pickup(weaponPickup);
			}
			else if (InteractionTarget is ItemBox itemBox)
			{
				itemBox.Open();
			}
			else if (InteractionTarget is StaticPickup staticPickup)
			{
				bool success = staticPickup.TryConsume(gameObject, out string result);
				if (success == false && result.HasValue() == true)
				{
					RPC_InteractionFailed(result);
				}
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			InteractionFailed = null;
		}

		public override void Render()
		{
			if (_character.HasInputAuthority == false)
			{
				InteractionTarget = null;
				return;
			}

			if (_health.IsAlive == false)
			{
				InteractionTarget = null;
				return;
			}

			UpdateInteractionTarget();
		}

		private void Awake()
		{
			_health    = GetComponent<Health>();
			_weapons   = GetComponent<Weapons>();
			_character = GetComponent<Character>();
			_aiming    = GetComponent<Aiming>();
		}

		private void UpdateInteractionTarget()
		{
			if (_aiming == null)
			{
				_aiming = GetComponent<Aiming>();
			}

			InteractionTarget = null;

			if (TryGetInteractionRay(out Vector3 cameraPosition, out Vector3 cameraDirection) == false)
				return;

			var physicsScene = Runner.GetPhysicsScene();
			int hitCount = physicsScene.SphereCast(cameraPosition, _interactionPrecisionRadius, cameraDirection, _interactionHits, _interactionDistance, _interactionMask, QueryTriggerInteraction.Ignore);

			if (hitCount == 0)
				return;

			RaycastHit validHit = default;

			if (physicsScene.Raycast(cameraPosition, cameraDirection, out RaycastHit raycastHit, _interactionDistance, _interactionMask, QueryTriggerInteraction.Ignore) == true && raycastHit.collider.gameObject.layer == ObjectLayer.Interaction)
			{
				validHit = raycastHit;
			}
			else
			{
				RaycastUtility.Sort(_interactionHits, hitCount);

				for (int i = 0; i < hitCount; i++)
				{
					var hit = _interactionHits[i];

					if (hit.collider.gameObject.layer == ObjectLayer.Default)
						return;

					if (hit.collider.gameObject.layer == ObjectLayer.Interaction)
					{
						validHit = hit;
						break;
					}
				}
			}

			var collider = validHit.collider;
			if (collider == null)
				return;

			var interaction = collider.GetComponent<IInteraction>();
			if (interaction == null)
			{
				interaction = collider.GetComponentInParent<IInteraction>();
			}

			if (interaction != null && interaction.IsActive == true)
			{
				InteractionTarget = interaction;
			}
		}

		private bool TryGetInteractionRay(out Vector3 cameraPosition, out Vector3 cameraDirection)
		{
			cameraPosition = default;
			cameraDirection = default;

			bool useAuthoritativeInputRay = Runner != null && Runner.Stage != default && HasStateAuthority == true &&
				_character != null && _character.Agent != null && _character.Agent.AgentInput != null;

			if (useAuthoritativeInputRay == true)
			{
				GameplayInput fixedInput = _character.Agent.AgentInput.FixedInput;
				if (fixedInput.HasAimRay == true)
				{
					cameraPosition = fixedInput.AimRayOrigin;
					cameraDirection = fixedInput.AimRayDirection;

					bool invalidInputPosition = float.IsNaN(cameraPosition.x) || float.IsNaN(cameraPosition.y) || float.IsNaN(cameraPosition.z);
					if (invalidInputPosition == false && cameraDirection.sqrMagnitude > 0.0001f)
					{
						cameraDirection.Normalize();
						return true;
					}
				}
			}

			if (_aiming != null && _aiming.TryGetObservedCameraPose(false, out cameraPosition, out cameraDirection) == true)
				return true;

			if (_character == null)
				return false;

			var cameraTransform = _character.GetCameraTransform(false);
			cameraPosition = cameraTransform.Position;

			Transform cameraHandle = _character.GetCameraHandle();
			cameraDirection = cameraHandle != null ? cameraHandle.forward : transform.forward;

			bool invalidFallbackPosition = float.IsNaN(cameraPosition.x) || float.IsNaN(cameraPosition.y) || float.IsNaN(cameraPosition.z);
			if (invalidFallbackPosition == true || cameraDirection.sqrMagnitude <= 0.0001f)
			{
				cameraPosition = transform.position;
				cameraDirection = transform.forward;
			}

			if (cameraDirection.sqrMagnitude <= 0.0001f)
				return false;

			cameraDirection.Normalize();
			return true;
		}

		[Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable)]
		private void RPC_InteractionFailed(string reason)
		{
			InteractionFailed?.Invoke(reason);
		}
	}
}
