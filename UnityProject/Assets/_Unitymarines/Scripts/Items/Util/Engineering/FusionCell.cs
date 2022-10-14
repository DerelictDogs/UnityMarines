using UnityEngine;
using Mirror;
using System;

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

		public void ConsumeFuel(float amount)
		{
			fuelCurrent = Math.Clamp(fuelCurrent - amount,0,fuelCapacity);
		}

		public void ReplenishFuel(float amount)
		{
			fuelCurrent = Math.Clamp(fuelCurrent + amount, 0, fuelCapacity);
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
