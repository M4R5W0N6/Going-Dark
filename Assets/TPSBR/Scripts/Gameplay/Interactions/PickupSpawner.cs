using UnityEngine;
using Fusion;

namespace TPSBR
{
	public sealed class PickupSpawner : NetworkBehaviour
	{
		[SerializeField]
		private Transform _spawnPoint;
		[SerializeField]
		private StaticPickup[] _pickupPrefabs;
		[SerializeField]
		private float _refillTime = 30;
		private bool _didLogMissingSpawnPoint;
		private bool _didLogMissingPrefabArray;
		private bool _didLogNullPrefabEntry;

		[Networked]
		private TickTimer _refillCooldown { get; set; }
		[Networked]
		private StaticPickup _activePickup { get; set; }

		// NetworkBehaviour INTERFACE

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false)
				return;

			if (_activePickup != null)
			{
				if (_activePickup.Object.IsValid == true && _activePickup.Consumed == false)
					return;

				_activePickup = null;
				_refillCooldown = TickTimer.CreateFromSeconds(Runner, _refillTime);
				return;
			}

			if (_refillCooldown.ExpiredOrNotRunning(Runner) == false)
				return;

			if (_spawnPoint == null)
			{
				if (_didLogMissingSpawnPoint == false)
				{
					Debug.LogWarning("[PickupSpawner] Missing spawn point.", this);
					_didLogMissingSpawnPoint = true;
				}
				return;
			}

			if (_pickupPrefabs == null || _pickupPrefabs.Length == 0)
			{
				if (_didLogMissingPrefabArray == false)
				{
					Debug.LogWarning("[PickupSpawner] No pickup prefabs configured.", this);
					_didLogMissingPrefabArray = true;
				}
				return;
			}

			var prefab = _pickupPrefabs[Random.Range(0, _pickupPrefabs.Length)];
			if (prefab == null)
			{
				if (_didLogNullPrefabEntry == false)
				{
					Debug.LogWarning("[PickupSpawner] Pickup prefab entry is null.", this);
					_didLogNullPrefabEntry = true;
				}
				return;
			}

			_activePickup = Runner.Spawn(prefab, _spawnPoint.position, _spawnPoint.rotation);
		}
	}
}
