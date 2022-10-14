using System;
using System.Collections;
using UnityEngine;
using Mirror;
using UnityEngine.Events;
using Chemistry.Components;
using Items;

namespace UnityMarines.Items.Engineering
{
	//Simplified version of Welder.cs to work with the TGMC blowtorch sprites, kept seperate to keep UnityMarines content sperate from base game
	[RequireComponent(typeof(Pickupable))]
	public class BlowTorch : NetworkBehaviour, ICheckedInteractable<HandActivate>, IServerSpawn
	{
		public Chemistry.Reagent fuel;

		/// <summary>
		/// Invoked server side when welder turns off for any reason.
		/// </summary>
		[NonSerialized]
		public UnityEvent OnWelderOffServer = new UnityEvent();

		public float damageOn;
		private float damageOff;

		private string currentHand;

		private ItemAttributesV2 itemAttributes;
		private Pickupable pickupable;
		private SpriteHandler spriteHandler;

		[SyncVar(hook = nameof(SyncIsOn))]
		private bool isOn;

		public bool IsOn => isOn;

		private bool isBurning = false;

		private Coroutine coBurnFuel;

		private ReagentContainer reagentContainer;
		private FireSource fireSource;

		private float FuelAmount => reagentContainer[fuel];

		void Awake()
		{
			EnsureInit();
		}

		private void EnsureInit()
		{
			if (pickupable != null) return;

			pickupable = GetComponent<Pickupable>();
			itemAttributes = GetComponent<ItemAttributesV2>();
			fireSource = GetComponent<FireSource>();
			spriteHandler = GetComponentInChildren<SpriteHandler>();

			reagentContainer = GetComponent<ReagentContainer>();
			if (reagentContainer != null)
			{
				reagentContainer.OnSpillAllContents.AddListener(ServerEmptyWelder);
			}

			damageOff = itemAttributes.ServerHitDamage;
		}

		public override void OnStartClient()
		{
			EnsureInit();
			SyncIsOn(isOn, isOn);
		}

		public void OnSpawnServer(SpawnInfo info)
		{
			SyncIsOn(isOn, false);
		}

		public bool WillInteract(HandActivate interaction, NetworkSide side)
		{
			return DefaultWillInteract.Default(interaction, side);
		}

		public void ServerPerformInteraction(HandActivate interaction)
		{
			ServerToggleWelder(interaction.Performer);
		}

		[Server]
		public void ServerEmptyWelder()
		{
			SyncIsOn(isOn, false);
		}

		[Server]
		public void ServerToggleWelder(GameObject originator)
		{
			SyncIsOn(isOn, !isOn);
		}

		private void SyncIsOn(bool _wasOn, bool _isOn)
		{
			EnsureInit();
			if (isServer)
			{
				if (FuelAmount <= 0f)
				{
					_isOn = false;
				}
			}

			isOn = _isOn;

			if (isServer)
			{
				//update damage stats when on / off
				if (isOn)
				{
					itemAttributes.ServerDamageType = DamageType.Burn;
					itemAttributes.ServerHitDamage = damageOn;
				}
				else
				{
					itemAttributes.ServerDamageType = DamageType.Brute;
					itemAttributes.ServerHitDamage = damageOff;
					//stop all progress
					OnWelderOffServer.Invoke();
				}
			}

			//update appearance / animation when on / off
			if (isOn)
			{
				isBurning = true;
				spriteHandler.ChangeSpriteVariant(1); //Change to burning state
				if (coBurnFuel == null) coBurnFuel = StartCoroutine(BurnFuel());
			}
			else
			{
				isBurning = false;
				spriteHandler.ChangeSpriteVariant(0); //Change to normal state
				if (coBurnFuel != null)
				{
					StopCoroutine(coBurnFuel);
					coBurnFuel = null;
				}
			}

			pickupable?.RefreshUISlotImage();

			// toogle firesource to burn things around
			if (fireSource)
			{
				fireSource.IsBurning = isOn;
			}
		}

		IEnumerator BurnFuel()
		{
			while (isBurning)
			{
				//Server fuel burning:
				if (isServer)
				{
					reagentContainer.TakeReagents(0.005f);

					//Ran out of fuel
					if (FuelAmount <= 0f)
					{
						SyncIsOn(isOn, false);
					}
				}

				yield return WaitFor.Seconds(.1f);
			}
		}
	}
}
