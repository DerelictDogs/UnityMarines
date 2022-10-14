using System;
using AddressableReferences;
using UnityEngine;
using Mirror;
using Systems.Electricity.NodeModules;

namespace UnityMarines.Objects.Engineering
{
	[RequireComponent(typeof(ModuleSupplyingDevice))]
	public class RepairableGenerator : NetworkBehaviour, ICheckedInteractable<HandApply>, INodeControl, IExaminable, IServerSpawn
	{
		protected enum GeneratorState
		{
			On = 0,
			Off = 1,
			PanelOff = 2,
			WireExposed = 3,
			GlassBroken = 4,
		}

		[SerializeField, SyncVar(hook = nameof(OnSyncState))]
		protected GeneratorState generatorState = GeneratorState.Off;

		[SerializeField]
		protected int SupplyWattage = 80000;

		private float powerGenPercent = 0;

		protected RegisterTile registerTile;
		protected Integrity integrity;
		protected SpriteHandler baseSpriteHandler;
		protected ModuleSupplyingDevice moduleSupplyingDevice;

		[SerializeField]
		protected AddressableAudioSource generatorRunSfx = null;
		[SerializeField]
		protected AddressableAudioSource generatorEndSfx = null;

		protected string runLoopGUID = "";

		private const float totalBarOffset = 0.344f; //How large is the power bar in units
		[SyncVar(hook = nameof(OnSyncVisual))]
		protected float currentBarOffset = 0; //How large is the power bar in units
		[SerializeField]
		protected GameObject barVisual = null;

		#region Lifecycle

		private void Awake()
		{
			registerTile = GetComponent<RegisterTile>();
			baseSpriteHandler = GetComponentInChildren<SpriteHandler>();
			integrity = GetComponent<Integrity>();
			moduleSupplyingDevice = GetComponent<ModuleSupplyingDevice>();
		}

		public virtual void OnSpawnServer(SpawnInfo info)
		{
			if (generatorState == GeneratorState.On) TryToggleOn();
			
			if (generatorState == GeneratorState.GlassBroken) integrity.ApplyDamage(0.75f * integrity.initialIntegrity, AttackType.Melee,DamageType.Brute);
			else if (generatorState == GeneratorState.WireExposed) integrity.ApplyDamage(0.5f * integrity.initialIntegrity, AttackType.Melee, DamageType.Brute);
			else if (generatorState == GeneratorState.PanelOff) integrity.ApplyDamage(0.25f * integrity.initialIntegrity, AttackType.Melee, DamageType.Brute); //Adjust health from starting state

			integrity.OnApplyDamage.AddListener(OnTakeDamage);
		}

		protected void OnDisable()
		{
			if (generatorState == GeneratorState.On)
			{
				ToggleOff();
			}

			OnSyncState(GeneratorState.Off,generatorState);
		
			integrity.OnApplyDamage.RemoveListener(OnTakeDamage);
		}

		#endregion

		protected void OnTakeDamage(DamageInfo damageInfo = null)
		{
			GeneratorState newState = generatorState;
	
			if (generatorState != GeneratorState.GlassBroken && integrity.PercentageDamaged <= 0.25f) newState = GeneratorState.GlassBroken;
			else if (generatorState != GeneratorState.WireExposed && integrity.PercentageDamaged <=0.5f && integrity.PercentageDamaged > 0.25f) newState = GeneratorState.WireExposed;
			else if ((generatorState == GeneratorState.On || generatorState == GeneratorState.Off) && integrity.PercentageDamaged <= 0.75f && integrity.PercentageDamaged > 0.5f) newState = GeneratorState.PanelOff;

			if(newState != generatorState)
			{
				if (generatorState == GeneratorState.On) ToggleOff();
				generatorState = newState;
			}
		}

		public void PowerNetworkUpdate() { }

		#region Interaction

		public virtual string Examine(Vector3 worldPos = default)
		{
			var stateList = new string[5] {"running", "turned off", "minorly damaged", "moderately damaged", "heavily damaged"};
			var repairText = new string[3] { "Use a wrench to repair it." , "Use a wirecutters, then wrench to repair it.", "Use a blowtorch, then wirecutters, then wrench to repair it." };

			var examineText = $"\nGenerator at: {MathF.Round(powerGenPercent,2)}% power.";

			examineText += $"\nThe generator is currently {stateList[(int)generatorState]}.";

			if (generatorState != GeneratorState.On && generatorState != GeneratorState.Off)
			{
				examineText += repairText[(int)generatorState - 2];
			}

			return examineText;
		}

		public virtual bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side) == false) return false;
			if (interaction.Intent == Intent.Harm) return false;
			if (interaction.TargetObject != gameObject) return false;

			if (interaction.HandObject == null && (generatorState == GeneratorState.On || generatorState == GeneratorState.Off)) return true; //Turning on and off

			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Wrench) && generatorState == GeneratorState.PanelOff) return true; //Repairing from minor damage
			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Wirecutter) && generatorState == GeneratorState.WireExposed) return true; //Repairing from moderate to minor damage
			if (Validations.HasUsedActiveWelder(interaction.HandObject) && generatorState == GeneratorState.GlassBroken) return true; //Repairing from heavy to moderate damage.

			return false;
		}

		public virtual void ServerPerformInteraction(HandApply interaction)
		{
			if (interaction.HandObject == null) //Toggle on/off interaction
			{
				if (generatorState == GeneratorState.Off)
				{
					if (TryToggleOn() == false) Chat.AddWarningMsgFromServer(interaction.Performer, $"The reactor requires a fuel cell before you can turn it on.");
					
				}
				else if (generatorState == GeneratorState.On) ToggleOff();

				return;
			}

			GeneratorState repairedState = GeneratorState.Off;

			string[] firstPersonMessages;
			string[] thirdPersonMessages;
			string targetName;

			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Wrench))
			{
				firstPersonMessages = new string[2]{"You start repairing","You repair"};
				thirdPersonMessages = new string[2] { "starts repairing", "repairs" };
				targetName = "tubing and plating";
				PerformRepair(interaction,repairedState,1f,firstPersonMessages,thirdPersonMessages,targetName);
			}
			else if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Wirecutter))
			{
				firstPersonMessages = new string[2] { "You start securing", "You secure" };
				thirdPersonMessages = new string[2] { "starts securing", "secures" };
				targetName = "wiring";
				repairedState = GeneratorState.PanelOff;

				PerformRepair(interaction, repairedState, 1/2f, firstPersonMessages, thirdPersonMessages, targetName);
			}
			else if (Validations.HasUsedActiveWelder(interaction.HandObject))
			{
				firstPersonMessages = new string[2] { "You start welding", "You weld" };
				thirdPersonMessages = new string[2] { "starts welding", "welds" };
				targetName = "internal damage";
				repairedState = GeneratorState.WireExposed;

				PerformRepair(interaction, repairedState, 1/4f, firstPersonMessages, thirdPersonMessages, targetName);
			}
		}

		protected void PerformRepair(HandApply interaction, GeneratorState repairState, float targetIntegrity, string[] firstPersonMessages, string[] thirdPersonMessages, string targetName)
		{
			//Dont need to do any state checks as already done in will interact for all repair interactions
			ToolUtils.ServerUseToolWithActionMessages(interaction, 5f,
				$"{firstPersonMessages[0]} {gameObject.ExpensiveName()}'s {targetName}...",
				$"{interaction.Performer.ExpensiveName()} {thirdPersonMessages[0]} {gameObject.ExpensiveName()}'s {targetName}...",
				$"{firstPersonMessages[1]} {gameObject.ExpensiveName()}'s {targetName}.",
				$"{interaction.Performer.ExpensiveName()} {thirdPersonMessages[1]} {gameObject.ExpensiveName()}'s {targetName}.",
				() =>
				{
					generatorState = repairState;
					float integrityToRestore = (integrity.initialIntegrity*targetIntegrity) - integrity.integrity;
					integrity.RestoreIntegrity(integrityToRestore); //Restores health;
				});
		}

		#endregion

		#region Power


		protected virtual bool TryToggleOn()
		{
			if(generatorState != GeneratorState.Off) return false; //Cannot be turned on if damaged or already on.

			ToggleOn();

			return true;
		}

		protected virtual void ToggleOn()
		{
			currentBarOffset = totalBarOffset * powerGenPercent / 100;

			moduleSupplyingDevice.ProducingWatts = Math.Clamp(SupplyWattage * 2 * powerGenPercent / 100, 0, SupplyWattage); //Loses wattage as fuel cell drains, but only after 50% fuelPercent. After which, every % of fuel lost results in 2% power loss

			UpdateManager.Add(UpdateMe, 5f); //Due to slow consumption of fuel we dont need to do this often

			moduleSupplyingDevice.TurnOnSupply();
			generatorState = GeneratorState.On;
		}

		protected virtual void ToggleOff()
		{
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateMe);
			currentBarOffset = totalBarOffset;
			moduleSupplyingDevice.TurnOffSupply();
			generatorState = GeneratorState.Off;
			powerGenPercent = 0;
		}

		protected virtual void UpdateMe()
		{				
			currentBarOffset = totalBarOffset * powerGenPercent / 100;

			if (powerGenPercent < 100 && CheckForFail() == false) powerGenPercent++;
			else ToggleOff();

			moduleSupplyingDevice.ProducingWatts = Math.Clamp(SupplyWattage * powerGenPercent / 100, 0, SupplyWattage); 
		}

		private bool CheckForFail()
		{
			int val = UnityEngine.Random.Range(0, (int)(300 * integrity.PercentageDamaged)); // 1/3% chance of failure + 1% per percentage health lost.

			return val == 0;
		}

		#endregion

		#region Syncs

		protected void OnSyncState(GeneratorState oldState, GeneratorState newState)
		{
			generatorState = newState;
			baseSpriteHandler.ChangeSprite((int)generatorState);

			if(generatorState == GeneratorState.On)
			{
				runLoopGUID = Guid.NewGuid().ToString();
				SoundManager.PlayAtPositionAttached(generatorRunSfx, registerTile.WorldPosition, gameObject, runLoopGUID);
			}
			else if (oldState == GeneratorState.On)
			{
				SoundManager.Stop(runLoopGUID);
				_ = SoundManager.PlayAtPosition(generatorEndSfx, registerTile.WorldPosition, gameObject);
			}
		}

		protected void OnSyncVisual(float oldPos, float newPos)
		{
			currentBarOffset = newPos;

			barVisual.transform.localPosition = new Vector3(0, currentBarOffset, 0);
		}

		#endregion
	}
}
