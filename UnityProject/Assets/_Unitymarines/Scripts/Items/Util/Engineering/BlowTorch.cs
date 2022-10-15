using System;
using System.Collections;
using UnityEngine;

namespace UnityMarines.Items.Engineering
{
	//Simplified version of Welder.cs to work with the TGMC blowtorch sprites, kept seperate to keep UnityMarines content sperate from base game
	[RequireComponent(typeof(Pickupable))]
	public class BlowTorch : WelderBase
	{
		private SpriteHandler spriteHandler;

		void Awake()
		{
			spriteHandler = GetComponentInChildren<SpriteHandler>();
		}

		protected override void SetSprites(bool on)
		{
			int index = on == true ? 1 : 0;

			spriteHandler.ChangeSpriteVariant(index);
		}
	}
}
