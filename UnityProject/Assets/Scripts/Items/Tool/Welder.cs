using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.Events;
using Chemistry.Components;
using Items;
public class Welder : WelderBase
{
	//TODO: Update the sprites from the array below based on how much fuel is left
	//TODO: gas readout in stats

	[Header("Place sprites in order from full gas to no gas 5 all up!")]
	public Sprite[] welderSprites;

	public Sprite[] flameSprites;

	public SpriteRenderer welderRenderer;

	public SpriteRenderer flameRenderer;

	private int spriteIndex = 0;

	protected override void SetSprites(bool on)
	{
		if(on)
		{
			flameRenderer.sprite = flameSprites[0];
		}
		else
		{
			flameRenderer.sprite = null;
		}
	}

	protected override void BurnAnimation()
	{
		flameRenderer.sprite = flameSprites[spriteIndex];

		spriteIndex++;
		if (spriteIndex == 2) spriteIndex = 0;		
	}
}