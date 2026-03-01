namespace TPSBR
{
	using UnityEngine;

	public interface IMoveSpeedProvider
	{
		float GetBaseSpeed(Vector2 localNormalizedDirection, float multiplier);
	}
}
