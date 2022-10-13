using UnityEngine;
using Mirror;

namespace UnityMarines.Items.Engineering
{
	public class FusionCell : NetworkBehaviour, IExaminable
	{
		[SyncVar,SerializeField]
		private float fuelCapacity = 9000;

		[SyncVar,SerializeField]
		private float fuelCurrent = 9000;

		private SpriteHandler spriteHandler;

		private int spriteCount = 0;

		public float FuelPercent
		{
			get
			{
				return (fuelCurrent / fuelCapacity) * 100;
			}
		}


		private void Awake()
		{
			spriteHandler = GetComponentInChildren<SpriteHandler>();
			spriteCount = spriteHandler.PresentSpritesSet.Variance.Count - 1;

			UpdateSprite();
		}

		public bool ConsumeFuel(float amount)
		{
			float newAmount = fuelCurrent -= amount;
			if (fuelCurrent < 0) return false;
			else
			{
				fuelCurrent = newAmount;
				return true;
			}
		}

		public void UpdateSprite()
		{
			int index = spriteCount - (int)(FuelPercent / 100 * spriteCount);
			spriteHandler.ChangeSpriteVariant(index);
		}

		public string Examine(Vector3 worldPos = default)
		{
			return $"{FuelPercent}% capacity remaining.";
		}
	}
}
