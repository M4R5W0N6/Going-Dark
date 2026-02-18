using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace TPSBR
{
	[Serializable]
	public class ShakeSetup
	{
		// Key mapped to a CinemachineImpulseSource in ShakeEffect.
		public string SourceKey;
	}

	public enum EShakeTarget
	{
		None,
		Position,
		Rotation,
	}

	public enum EShakeForce
	{
		None,
		ReplaceSame,
		Add,
	}

	[Serializable]
	public sealed class ShakeSourceDefinition
	{
		public string                   Key;
		public CinemachineImpulseSource Source;
	}

	/// <summary>
	/// Routes shake requests to configured CM3 impulse sources and queues emission.
	/// Impulse tuning is authored on the CinemachineImpulseSource components.
	/// </summary>
	public class ShakeEffect : CoreBehaviour
	{
		public bool IsPlaying => _activeLooping.Count > 0;

		[SerializeField]
		private List<ShakeSourceDefinition> _sources = new List<ShakeSourceDefinition>(8);

		private readonly List<PendingPulse> _pendingPulses = new List<PendingPulse>(32);
		private readonly List<LoopingShake> _activeLooping = new List<LoopingShake>(8);

		public void Play(ShakeSetup setup, EShakeForce force = EShakeForce.Add)
		{
			if (TryResolveSource(setup, out CinemachineImpulseSource source) == false)
				return;

			// Always emit an immediate pulse when requested.
			_pendingPulses.Add(new PendingPulse { Source = source });

			// ReplaceSame is used by one-shot events (e.g., weapons), so don't loop.
			if (force == EShakeForce.ReplaceSame)
				return;

			// Add/None are treated as sustained until Stop(...) is called.
			if (TryFindLooping(source, out _) == true)
				return;

			_activeLooping.Add(new LoopingShake
			{
				Source = source,
				NextEmitTime = Time.unscaledTime + GetLoopPeriod(source),
			});
		}

		// Intentionally no default setup fallback.
		public void Play(EShakeForce force = EShakeForce.Add)
		{
		}

		public void Stop(ShakeSetup setup, bool immediate = false)
		{
			if (TryResolveSource(setup, out CinemachineImpulseSource source) == false)
				return;

			for (int i = _activeLooping.Count - 1; i >= 0; --i)
			{
				if (_activeLooping[i].Source == source)
				{
					_activeLooping.RemoveAt(i);
				}
			}
		}

		public void Stop(bool immediate = false)
		{
			_activeLooping.Clear();
		}

		protected void Update()
		{
			float now = Time.unscaledTime;

			for (int i = 0; i < _activeLooping.Count; ++i)
			{
				LoopingShake looping = _activeLooping[i];
				CinemachineImpulseSource source = looping.Source;
				if (source == null)
					continue;

				float period = GetLoopPeriod(source);
				if (period <= 0.0001f)
					period = 0.05f;

				while (now >= looping.NextEmitTime)
				{
					_pendingPulses.Add(new PendingPulse { Source = source });
					looping.NextEmitTime += period;
				}

				_activeLooping[i] = looping;
			}

			FlushPulseQueue();
		}

		private void FlushPulseQueue()
		{
			for (int i = 0; i < _pendingPulses.Count; ++i)
			{
				CinemachineImpulseSource source = _pendingPulses[i].Source;
				if (source == null)
					continue;

				source.GenerateImpulse();
			}

			_pendingPulses.Clear();
		}

		private bool TryResolveSource(ShakeSetup setup, out CinemachineImpulseSource source)
		{
			source = null;
			if (setup == null || string.IsNullOrWhiteSpace(setup.SourceKey))
				return false;

			for (int i = 0; i < _sources.Count; ++i)
			{
				ShakeSourceDefinition definition = _sources[i];
				if (definition == null)
					continue;
				if (definition.Source == null)
					continue;
				if (string.IsNullOrWhiteSpace(definition.Key))
					continue;
				if (string.Equals(definition.Key, setup.SourceKey, StringComparison.OrdinalIgnoreCase) == false)
					continue;

				source = definition.Source;
				return true;
			}

			return false;
		}

		private bool TryFindLooping(CinemachineImpulseSource source, out int index)
		{
			for (int i = 0; i < _activeLooping.Count; ++i)
			{
				if (_activeLooping[i].Source == source)
				{
					index = i;
					return true;
				}
			}

			index = -1;
			return false;
		}

		private static float GetLoopPeriod(CinemachineImpulseSource source)
		{
			if (source == null || source.ImpulseDefinition == null)
				return 0.1f;

			float duration = Mathf.Max(0.01f, source.ImpulseDefinition.ImpulseDuration);
			return duration;
		}

		private struct PendingPulse
		{
			public CinemachineImpulseSource Source;
		}

		private struct LoopingShake
		{
			public CinemachineImpulseSource Source;
			public float                    NextEmitTime;
		}
	}
}
