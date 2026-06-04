namespace TPSBR
{
	using System;
	using UnityEngine;
	using Fusion;

	[Serializable]
	public sealed class WeaponSlot
	{
		public Transform  Active;
		public Transform  Inactive;
		[NonSerialized]
		public Quaternion BaseRotation;
	}

	public sealed class Weapons : NetworkBehaviour, IBeforeTick
	{
		// PUBLIC MEMBERS

		public Weapon     PendingWeapon             { get; private set; }
		public Weapon     CurrentWeapon             { get; private set; }
		public Transform  CurrentWeaponHandle       { get; private set; }
		public Quaternion CurrentWeaponBaseRotation { get; private set; }

		public LayerMask  HitMask            => _hitMask;
		public int        CurrentWeaponSlot  => _currentWeaponSlot;
		public int        PendingWeaponSlot  => _pendingWeaponSlot;
		public int        PreviousWeaponSlot => _previousWeaponSlot;

		// PRIVATE MEMBERS

		[SerializeField]
		private WeaponSlot[] _slots;
		[SerializeField]
		private Weapon[]     _initialWeapons;
		[SerializeField]
		private Vector3      _dropWeaponImpulse = new Vector3(5, 5f, 10f);
		[SerializeField]
		private LayerMask    _hitMask;

		[Header("Audio")]
		[SerializeField]
		private Transform    _fireAudioEffectsRoot;

		[Networked, Capacity(8)]
		private NetworkArray<NetworkBehaviourId> _weaponReferences { get; }
		[Networked]
		private byte _currentWeaponSlot { get; set; }
		[Networked]
		private byte _pendingWeaponSlot { get; set; }
		[Networked]
		private byte _previousWeaponSlot { get; set; }

		private Health        _health;
		private Character     _character;
		private Aiming        _aiming;
		private AudioEffect[] _fireAudioEffects;
		private Weapon[]      _localWeapons = new Weapon[8];

		// PUBLIC METHODS

		public void DisarmCurrentWeapon()
		{
			int currentWeaponSlot = SanitizeWeaponSlot(_currentWeaponSlot);
			if (currentWeaponSlot == 0)
			{
				_currentWeaponSlot = 0;
				return;
			}

			if (CurrentWeapon != null)
			{
				CurrentWeapon.DisarmWeapon();
			}

			if (currentWeaponSlot > 0)
			{
				_previousWeaponSlot = (byte)currentWeaponSlot;
			}

			_currentWeaponSlot = 0;

			CurrentWeapon             = ResolveWeapon(_currentWeaponSlot);
			RefreshCurrentWeaponSlotData(_currentWeaponSlot);

			if (CurrentWeapon != null)
			{
				CurrentWeapon.ArmWeapon();
			}
		}

		public void SetPendingWeapon(int slot)
		{
			slot = SanitizeWeaponSlot(slot);

			if (_pendingWeaponSlot == slot)
				return;

			_pendingWeaponSlot = (byte)slot;
			PendingWeapon = ResolveWeapon(_pendingWeaponSlot);
		}

		public void ArmPendingWeapon()
		{
			int currentWeaponSlot = SanitizeWeaponSlot(_currentWeaponSlot);
			int pendingWeaponSlot = SanitizeWeaponSlot(_pendingWeaponSlot);

			_currentWeaponSlot = (byte)currentWeaponSlot;
			_pendingWeaponSlot = (byte)pendingWeaponSlot;

			if (currentWeaponSlot == pendingWeaponSlot)
				return;

			if (CurrentWeapon != null)
			{
				CurrentWeapon.DisarmWeapon();
			}

			if (currentWeaponSlot > 0)
			{
				_previousWeaponSlot = (byte)currentWeaponSlot;
			}

			_currentWeaponSlot = (byte)pendingWeaponSlot;

			CurrentWeapon             = ResolveWeapon(_currentWeaponSlot);
			RefreshCurrentWeaponSlotData(_currentWeaponSlot);

			if (CurrentWeapon != null)
			{
				CurrentWeapon.ArmWeapon();
			}
		}

		public void DropCurrentWeapon()
		{
			DropWeapon(_currentWeaponSlot);
		}

		public void Pickup(DynamicPickup dynamicPickup, Weapon pickupWeapon)
		{
			if (HasStateAuthority == false)
				return;

			var ownedWeapon = ResolveWeapon(pickupWeapon.WeaponSlot);
			if (ownedWeapon != null && ownedWeapon.WeaponID == pickupWeapon.WeaponID)
			{
				// We already have this weapon, try add at least the ammo
				var firearmWeapon = pickupWeapon as FirearmWeapon;
				bool consumed = firearmWeapon != null && ownedWeapon.AddAmmo(firearmWeapon.TotalAmmo);

				if (consumed == true)
				{
					dynamicPickup.UnassignObject();
					Runner.Despawn(pickupWeapon.Object);
				}
			}
			else
			{
				dynamicPickup.UnassignObject();
				PickupWeapon(pickupWeapon);
			}
		}

		public void Pickup(WeaponPickup weaponPickup)
		{
			if (HasStateAuthority == false)
				return;

			if (weaponPickup.Consumed == true || weaponPickup.IsDisabled == true)
				return;

			var ownedWeapon = ResolveWeapon(weaponPickup.WeaponPrefab.WeaponSlot);
			if (ownedWeapon != null && ownedWeapon.WeaponID == weaponPickup.WeaponPrefab.WeaponID)
			{
				// We already have this weapon, try add at least the ammo
				var firearmWeapon = weaponPickup.WeaponPrefab as FirearmWeapon;
				bool consumed = firearmWeapon != null && ownedWeapon.AddAmmo(firearmWeapon.InitialAmmo);

				if (consumed == true)
				{
					weaponPickup.TryConsume(gameObject, out string weaponPickupResult);
				}
			}
			else
			{
				weaponPickup.TryConsume(gameObject, out string weaponPickupResult2);

				var weapon = Runner.Spawn(weaponPickup.WeaponPrefab, inputAuthority: Object.InputAuthority);
				PickupWeapon(weapon);
			}
		}

		public override void Spawned()
		{
			if (HasStateAuthority == false)
			{
				for (int i = 0; i < _localWeapons.Length; i++)
				{
					_localWeapons[i] = null;
				}
				RefreshWeapons();
				return;
			}

			_currentWeaponSlot  = 0;
			_pendingWeaponSlot  = 0;
			_previousWeaponSlot = 0;
			PendingWeapon       = null;
			CurrentWeapon       = null;

			for (int i = 0; i < _weaponReferences.Length; i++)
			{
				_weaponReferences.Set(i, NetworkBehaviourId.None);
				_localWeapons[i] = null;
			}

			byte bestWeaponSlot = 0;

			// Spawn initial weapons
			for (byte i = 0; i < _initialWeapons.Length; i++)
			{
				var weaponPrefab = _initialWeapons[i];
				if (weaponPrefab == null)
					continue;

				var weapon = Runner.Spawn(weaponPrefab, inputAuthority: Object.InputAuthority);
				AddWeapon(weapon);

				if (weapon.WeaponSlot > bestWeaponSlot && weapon.WeaponSlot < 3)
				{
					bestWeaponSlot = (byte)weapon.WeaponSlot;
				}
			}

			_previousWeaponSlot = bestWeaponSlot;

			SetPendingWeapon(bestWeaponSlot);
			ArmPendingWeapon();
			RefreshWeapons();
		}

		public void OnDespawned()
		{
			// Cleanup weapons
			for (int i = 0; i < _weaponReferences.Length; i++)
			{
				Weapon weapon = ResolveWeapon(i);
				if (weapon != null)
				{
					weapon.Deinitialize(Object);
					if (HasStateAuthority == true)
					{
						Runner.Despawn(weapon.Object);
					}
				}

				if (HasStateAuthority == true)
				{
					_weaponReferences.Set(i, NetworkBehaviourId.None);
				}

				_localWeapons[i] = null;
			}

			for (int i = 0; i < _localWeapons.Length; i++)
			{
				Weapon weapon = _localWeapons[i];
				if (weapon != null)
				{
					weapon.Deinitialize(Object);
					_localWeapons[i] = null;
				}
			}

			_currentWeaponSlot  = 0;
			_pendingWeaponSlot  = 0;
			_previousWeaponSlot = 0;

			PendingWeapon             = default;
			CurrentWeapon             = default;
			CurrentWeaponHandle       = default;
			CurrentWeaponBaseRotation = default;
		}

		public void OnFixedUpdate()
		{
			if (HasStateAuthority == false)
				return;

			if (_health.IsAlive == false)
			{
				DropAllWeapons();
				return;
			}

			// Autoswitch to valid weapon if current is invalid
			if (CurrentWeapon != null && CurrentWeapon.ValidOnlyWithAmmo == true && CurrentWeapon.HasAmmo() == false)
			{
				byte bestWeaponSlot = _previousWeaponSlot;
				if (bestWeaponSlot == 0 || bestWeaponSlot == _currentWeaponSlot)
				{
					bestWeaponSlot = FindBestWeaponSlot(_currentWeaponSlot);
				}

				DisarmCurrentWeapon();
				SetPendingWeapon(bestWeaponSlot);

				_previousWeaponSlot = bestWeaponSlot;
			}
		}

		public override void Render()
		{
			RefreshWeapons();
		}

		public bool IsSwitchingWeapon()
		{
			return _pendingWeaponSlot != _currentWeaponSlot;
		}

		public bool CanFireWeapon(bool keyDown)
		{
			return IsSwitchingWeapon() == false && CurrentWeapon != null && CurrentWeapon.CanFire(keyDown) == true;
		}

		public bool CanReloadWeapon(bool autoReload)
		{
			return IsSwitchingWeapon() == false && CurrentWeapon != null && CurrentWeapon.CanReload(autoReload) == true;
		}

		public bool CanAim()
		{
			return IsSwitchingWeapon() == false && CurrentWeapon != null && CurrentWeapon.CanAim() == true;
		}

		public Vector2 GetRecoil()
		{
			var firearmWeapon = CurrentWeapon as FirearmWeapon;
			var recoil = firearmWeapon != null ? firearmWeapon.Recoil : Vector2.zero;
			return new Vector2(-recoil.y, recoil.x); // Convert to axis angles
		}

		public void SetRecoil(Vector2 axisRecoil)
		{
			var firearmWeapon = CurrentWeapon as FirearmWeapon;

			if (firearmWeapon == null)
				return;

			firearmWeapon.Recoil = new Vector2(axisRecoil.y, -axisRecoil.x);
		}

		public bool SwitchWeapon(int weaponSlot)
		{
			if (weaponSlot == _pendingWeaponSlot)
				return false;

			var weapon = ResolveWeapon(weaponSlot);
			if (weapon == null || (weapon.ValidOnlyWithAmmo == true && weapon.HasAmmo() == false))
				return false;

			SetPendingWeapon(weaponSlot);
			return true;
		}

		public bool HasWeapon(int slot, bool checkAmmo = false)
		{
			if (slot < 0 || slot >= _weaponReferences.Length)
				return false;

			var weapon = ResolveWeapon(slot);
			return weapon != null && (checkAmmo == false || (weapon.Object != null && weapon.HasAmmo() == true));
		}

		public Weapon GetWeapon(int slot)
		{
			return ResolveWeapon(slot);
		}

		public int GetNextWeaponSlot(int fromSlot, int minSlot = 0, bool checkAmmo = true)
		{
			int weaponCount = _weaponReferences.Length;

			for (int i = 0; i < weaponCount; i++)
			{
				int slot = (i + fromSlot + 1) % weaponCount;

				if (slot < minSlot)
					continue;

				var weapon = ResolveWeapon(slot);

				if (weapon == null)
					continue;

				if (checkAmmo == true && weapon.HasAmmo() == false)
					continue;

				return slot;
			}

			return 0;
		}

		public bool Fire()
		{
			if (CurrentWeapon == null)
				return false;
			if (_aiming == null)
			{
				_aiming = GetComponent<Aiming>();
			}

			Vector3       targetPoint   = _aiming != null ? _aiming.GetTargetPoint(false, true) : (transform.position + transform.forward * 500.0f);
			TransformData fireTransform = _character.GetFireTransform(true);

			CurrentWeapon.Fire(fireTransform.Position, targetPoint, _hitMask);

			return true;
		}

		public bool Reload()
		{
			if (CurrentWeapon == null)
				return false;

			CurrentWeapon.Reload();
			return true;
		}

		public bool AddAmmo(int weaponSlot, int amount, out string result)
		{
			if (weaponSlot < 0 || weaponSlot >= _weaponReferences.Length)
			{
				result = string.Empty;
				return false;
			}

			var weapon = ResolveWeapon(weaponSlot);
			if (weapon == null)
			{
				result = "No weapon with this type of ammo";
				return false;
			}

			bool ammoAdded = weapon.AddAmmo(amount);
			result = ammoAdded == true ? string.Empty : "Cannot add more ammo";

			return ammoAdded;
		}

		// IBeforeTick INTERFACE

		void IBeforeTick.BeforeTick()
		{
			RefreshWeapons();
		}

		// MONOBEHAVIOUR

		private void Awake()
		{
			_health = GetComponent<Health>();
			_character = GetComponent<Character>();
			_aiming = GetComponent<Aiming>();
			_fireAudioEffects = _fireAudioEffectsRoot != null ? _fireAudioEffectsRoot.GetComponentsInChildren<AudioEffect>() : Array.Empty<AudioEffect>();

			for (int i = 0, count = _slots != null ? _slots.Length : 0; i < count; ++i)
			{
				WeaponSlot slot = _slots[i];
				if (slot == null)
					continue;

				if (slot.Active != null)
				{
					slot.BaseRotation = slot.Active.localRotation;
				}
			}
		}

		// PRIVATE METHODS

		private int SanitizeWeaponSlot(int slot)
		{
			return IsValidWeaponSlot(slot) == true ? slot : 0;
		}

		private bool IsValidWeaponSlot(int slot)
		{
			return slot >= 0 && slot < _weaponReferences.Length && _localWeapons != null && slot < _localWeapons.Length;
		}

		private bool TryGetWeaponSlotData(int slot, out WeaponSlot slotData)
		{
			slotData = null;
			if (IsValidWeaponSlot(slot) == false)
				return false;
			if (_slots == null || slot >= _slots.Length)
				return false;

			slotData = _slots[slot];
			return slotData != null;
		}

		private void RefreshCurrentWeaponSlotData(int slot)
		{
			if (TryGetWeaponSlotData(slot, out WeaponSlot slotData) == true)
			{
				CurrentWeaponHandle = slotData.Active;
				CurrentWeaponBaseRotation = slotData.BaseRotation;
				return;
			}

			CurrentWeaponHandle = default;
			CurrentWeaponBaseRotation = default;
		}

		private void ClearLocalWeapon(int slot)
		{
			if (IsValidWeaponSlot(slot) == true)
			{
				_localWeapons[slot] = default;
			}
		}

		private void RefreshWeapons()
		{
			int pendingWeaponSlot = SanitizeWeaponSlot(_pendingWeaponSlot);
			int currentWeaponSlot = SanitizeWeaponSlot(_currentWeaponSlot);

			if (HasStateAuthority == true)
			{
				_pendingWeaponSlot = (byte)pendingWeaponSlot;
				_currentWeaponSlot = (byte)currentWeaponSlot;
			}

			PendingWeapon = ResolveWeapon(pendingWeaponSlot);

			Vector2 lastRecoil = Vector2.zero;

			for (int i = 0; i < _weaponReferences.Length; i++)
			{
				var weapon = ResolveWeapon(i);
				if (weapon == null)
					continue;

				if (weapon.IsInitialized == false)
				{
					if (IsValidWeaponSlot(weapon.WeaponSlot) == false)
						continue;

					TryGetWeaponSlotData(weapon.WeaponSlot, out WeaponSlot slotData);
					weapon.Initialize(Object, slotData != null ? slotData.Active : null, slotData != null ? slotData.Inactive : null);
					weapon.AssignFireAudioEffects(_fireAudioEffectsRoot, _fireAudioEffects);
					_localWeapons[weapon.WeaponSlot] = weapon;
				}

				if (weapon.IsArmed == true)
				{
					if (weapon.WeaponSlot != _currentWeaponSlot)
					{
						weapon.DisarmWeapon();
					}

					if (weapon is FirearmWeapon firearmWeapon)
					{
						lastRecoil = firearmWeapon.Recoil;
					}
				}
			}

			Weapon currentWeapon = ResolveWeapon(currentWeaponSlot);
			if (CurrentWeapon != currentWeapon)
			{
				if (currentWeapon == null)
				{
					if (CurrentWeapon != null)
					{
						CurrentWeapon.Deinitialize(Object);
						ClearLocalWeapon(CurrentWeapon.WeaponSlot);
					}
				}

				CurrentWeapon             = currentWeapon;
				RefreshCurrentWeaponSlotData(currentWeaponSlot);

				if (CurrentWeapon != null)
				{
					CurrentWeapon.ArmWeapon();

					if (CurrentWeapon is FirearmWeapon firearmWeapon)
					{
						// Recoil transfers to new weapon
						// (might be better to have recoil as an agent property instead of a weapon property)
						firearmWeapon.Recoil = lastRecoil;
					}
				}
			}
		}

		private void DropAllWeapons()
		{
			for (int i = 1; i < _weaponReferences.Length; i++)
			{
				DropWeapon(i);
			}
		}

		private void DropWeapon(int weaponSlot)
		{
			var weapon = ResolveWeapon(weaponSlot);
			if (weapon == null)
				return;

			var droppedObjectId = weapon.Object.Id;

			if (weapon.PickupPrefab == null)
			{
				Debug.LogWarning($"Cannot drop weapon {gameObject.name}, pickup prefab not assigned.");
				return;
			}

			weapon.Deinitialize(Object);

			if (weaponSlot == _currentWeaponSlot)
			{
				byte bestWeaponSlot = _previousWeaponSlot;
				if (bestWeaponSlot == 0 || bestWeaponSlot == _currentWeaponSlot)
				{
					bestWeaponSlot = FindBestWeaponSlot(_currentWeaponSlot);
				}

				SetPendingWeapon(bestWeaponSlot);
				ArmPendingWeapon();

				_previousWeaponSlot = bestWeaponSlot;
			}

			var weaponTransform = weapon.transform;

			var pickup = Runner.Spawn(weapon.PickupPrefab, weaponTransform.position, weaponTransform.rotation,
				PlayerRef.None, BeforePickupSpawned);

			RemoveWeapon(weaponSlot);

			var pickupRigidbody = pickup.GetComponent<Rigidbody>();
			if (pickupRigidbody != null)
			{
				var forcePosition = weaponTransform.TransformPoint(new Vector3(-0.005f, 0.005f, 0.015f) * weaponSlot);
				pickupRigidbody.AddForceAtPosition(weaponTransform.rotation * _dropWeaponImpulse, forcePosition, ForceMode.Impulse);
			}

			void BeforePickupSpawned(NetworkRunner runner, NetworkObject obj)
			{
				var dynamicPickup = obj.GetComponent<DynamicPickup>();
				dynamicPickup.AssignObject(droppedObjectId);
			}
		}

		private void PickupWeapon(Weapon weapon)
		{
			if (weapon == null)
				return;

			DropWeapon(weapon.WeaponSlot);
			AddWeapon(weapon);

			if (weapon.WeaponSlot >= _currentWeaponSlot && weapon.WeaponSlot < 5)
			{
				SetPendingWeapon(weapon.WeaponSlot);
				ArmPendingWeapon();
			}
		}

		private void AddWeapon(Weapon weapon)
		{
			if (weapon == null)
				return;
			if (IsValidWeaponSlot(weapon.WeaponSlot) == false)
				return;

			RemoveWeapon(weapon.WeaponSlot);

			weapon.Object.AssignInputAuthority(Object.InputAuthority);
			TryGetWeaponSlotData(weapon.WeaponSlot, out WeaponSlot slotData);
			weapon.Initialize(Object, slotData != null ? slotData.Active : null, slotData != null ? slotData.Inactive : null);
			weapon.AssignFireAudioEffects(_fireAudioEffectsRoot, _fireAudioEffects);

			var aoiProxy = weapon.GetComponent<NetworkAreaOfInterestProxy>();
			aoiProxy.SetPositionSource(transform);

			Runner.SetPlayerAlwaysInterested(Object.InputAuthority, weapon.Object, true);

			if (HasStateAuthority == true)
			{
				_weaponReferences.Set(weapon.WeaponSlot, weapon.Id);
			}
			_localWeapons[weapon.WeaponSlot] = weapon;
		}

		private void RemoveWeapon(int slot)
		{
			if (IsValidWeaponSlot(slot) == false)
				return;

			var weapon = ResolveWeapon(slot);
			if (weapon == null)
			{
				_localWeapons[slot] = null;
				if (HasStateAuthority == true)
				{
					_weaponReferences.Set(slot, NetworkBehaviourId.None);
				}
				return;
			}

			weapon.Deinitialize(Object);
			weapon.Object.RemoveInputAuthority();

			var aoiProxy = weapon.GetComponent<NetworkAreaOfInterestProxy>();
			aoiProxy.ResetPositionSource();

			Runner.SetPlayerAlwaysInterested(Object.InputAuthority, weapon.Object, false);

			if (HasStateAuthority == true)
			{
				_weaponReferences.Set(slot, NetworkBehaviourId.None);
			}
			_localWeapons[slot] = null;
		}

		private byte FindBestWeaponSlot(int ignoreSlot)
		{
			byte bestWeaponSlot = 0;

			for (int i = 0; i < _weaponReferences.Length; i++)
			{
				Weapon weapon = ResolveWeapon(i);
				if (weapon != null)
				{
					if (weapon.WeaponSlot == ignoreSlot)
						continue;

					if (weapon.WeaponSlot > bestWeaponSlot && weapon.WeaponSlot < 3)
					{
						bestWeaponSlot = (byte)weapon.WeaponSlot;
					}
				}
			}

			return bestWeaponSlot;
		}

		private Weapon ResolveWeapon(int slot)
		{
			if (IsValidWeaponSlot(slot) == false)
				return null;

			var localWeapon = _localWeapons[slot];
			if (localWeapon != null)
			{
				if (localWeapon.Object == null || localWeapon.Object.IsValid == false)
				{
					_localWeapons[slot] = null;
					localWeapon = null;
				}
			}

			NetworkBehaviourId weaponReference = _weaponReferences[slot];
			if (weaponReference == default || weaponReference == NetworkBehaviourId.None)
			{
				_localWeapons[slot] = null;
				return null;
			}

			if (localWeapon != null && localWeapon.Id == weaponReference)
			{
				return localWeapon;
			}

			if (TryResolveBehaviourSafe(Runner, weaponReference, out Weapon weapon) == true && weapon != null)
			{
				if (weapon.Object != null && weapon.Object.IsValid == true)
				{
					_localWeapons[slot] = weapon;
					return weapon;
				}
			}

			if (HasStateAuthority == true)
			{
				_weaponReferences.Set(slot, NetworkBehaviourId.None);
			}

			_localWeapons[slot] = null;
			return null;
		}

		private static bool TryResolveBehaviourSafe<T>(NetworkRunner runner, NetworkBehaviourId behaviourId, out T behaviour) where T : NetworkBehaviour
		{
			behaviour = null;
			if (runner == null || behaviourId == default || behaviourId == NetworkBehaviourId.None)
				return false;

			try
			{
				return runner.TryFindBehaviour(behaviourId, out behaviour) == true && behaviour != null;
			}
			catch (Exception ex) when (IsFusionAssertException(ex))
			{
				behaviour = null;
				return false;
			}
		}

		private static bool IsFusionAssertException(Exception ex)
		{
			return ex != null && ex.GetType().Name == "AssertException";
		}
	}
}
