using System;
using AddressableReferences;
using UnityEngine;
using Mirror;
using Systems.Electricity.NodeModules;
using UnityMarines.Items.Engineering;

namespace UnityMarines.Objects.Engineering
{
	/// <summary>
	/// Wanted to make this a subclass of PowerGenerator.cs, however shortly discovered it would be more complicated to do so then just have a standalone class
	/// </summary>
	[RequireComponent(typeof(ItemStorage)), RequireComponent(typeof(ModuleSupplyingDevice))]
	public class FusionGenerator : NetworkBehaviour, ICheckedInteractable<HandApply>, INodeControl, IExaminable, IServerSpawn
	{
		private enum GeneratorState
		{
			On = 0,
			Off = 1,
			PanelOff = 2,
			WireExposed = 3,
			GlassBroken = 4,
		}

		[SerializeField, SyncVar(hook = nameof(OnSyncState))]
		private GeneratorState generatorState = GeneratorState.Off;

		[Tooltip("Whether this generator should start running when spawned.")]
		[SerializeField]
		private bool startAsOn = false;

		[Tooltip("The cell item that will placed in this generator on spawn, leave blank for no starting cell.")]
		[SerializeField]
		private GameObject initialFusionCell = null;

		private FusionCell currentCell = null;

		[Tooltip("Fusion cell item trait")]
		[SerializeField]
		private ItemTrait cellTrait = null;

		[SerializeField]
		private int SupplyWattage = 80000;

		private RegisterTile registerTile;
		private Integrity integrity;
		private ItemSlot itemSlot;
		private SpriteHandler baseSpriteHandler;
		private ModuleSupplyingDevice moduleSupplyingDevice;

		[SerializeField]
		private AddressableAudioSource generatorRunSfx = null;
		[SerializeField]
		private AddressableAudioSource generatorEndSfx = null;

		private string runLoopGUID = "";

		private const float totalBarOffset = -0.344f; //How large is the power bar in units
		[SyncVar(hook = nameof(OnSyncVisual))]
		private float currentBarOffset = 0; //How large is the power bar in units
		[SerializeField]
		private GameObject barVisual = null;


		#region Lifecycle

		void Awake()
		{
			registerTile = GetComponent<RegisterTile>();
			baseSpriteHandler = GetComponentInChildren<SpriteHandler>();
			integrity = GetComponent<Integrity>();
			moduleSupplyingDevice = GetComponent<ModuleSupplyingDevice>();

			var itemStorage = GetComponent<ItemStorage>();
			itemSlot = itemStorage.GetIndexedItemSlot(0);
		}

		public void OnSpawnServer(SpawnInfo info)
		{
			if (startAsOn)
			{
				if (initialFusionCell == null) return;
	
				itemSlot._ServerSetItem(initialFusionCell.GetComponent<Pickupable>());

				UpdateCell();
				TryToggleOn();
			}

			integrity.OnApplyDamage.AddListener(OnTakeDamage);
		}

		private void OnDisable()
		{
			if (generatorState == GeneratorState.On)
			{
				ToggleOff();
			}

			OnSyncState(GeneratorState.Off,generatorState);
		
			integrity.OnApplyDamage.RemoveListener(OnTakeDamage);
		}

		#endregion

		private void OnTakeDamage(DamageInfo damageInfo = null)
		{
			GeneratorState newState = generatorState;
	
			if (generatorState != GeneratorState.GlassBroken && integrity.PercentageDamaged <= 0.25f) newState = GeneratorState.GlassBroken;
			else if (generatorState != GeneratorState.WireExposed && integrity.PercentageDamaged <=0.5f) newState = GeneratorState.WireExposed;
			else if ((generatorState == GeneratorState.On || generatorState == GeneratorState.Off) && integrity.PercentageDamaged <= 0.75f) newState = GeneratorState.PanelOff;

			if(newState != generatorState)
			{
				if (generatorState == GeneratorState.On) ToggleOff();
				generatorState = newState;
			}
		}

		public void PowerNetworkUpdate() { }

		#region Interaction

		public string Examine(Vector3 worldPos = default)
		{
			var stateList = new string[5] {"running", "turned off", "minorly damaged", "moderately damaged", "heavily damaged"};

			var examineText = $"The generator is currently {stateList[(int)generatorState]}.";

			if(currentCell == null)
			{
				examineText += "\nNo fusion cell in generator.";
			}
			else
			{
				examineText += $"\nInternal fusion cell at: {MathF.Round(currentCell.FuelPercent,2)}% capacity.";
			}

			return examineText;
		}

		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side) == false) return false;
			if (interaction.TargetObject != gameObject) return false;

			if (interaction.HandObject == null && (generatorState == GeneratorState.On || generatorState == GeneratorState.Off)) return true; //Turning on and off

			if (Validations.HasItemTrait(interaction.HandObject, cellTrait)) return true; //Adding a new power cell
			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Crowbar)) return true; //Removing a power cell
			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Wrench) && generatorState == GeneratorState.PanelOff) return true; //Repairing from minor damage
			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Wirecutter) && generatorState == GeneratorState.WireExposed) return true; //Repairing from moderate to minor damage
			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Welder) && generatorState == GeneratorState.GlassBroken) return true; //Repairing from heavy to moderate damage.

			return false;
		}

		public void ServerPerformInteraction(HandApply interaction)
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

			if(Validations.HasItemTrait(interaction.HandObject, cellTrait)) //Add fustion cell interaction
			{
				if(currentCell != null)
				{
					Chat.AddWarningMsgFromServer(interaction.Performer, $"You need to remove the fuel cell from the reactor first.");
					return;
				}
				if(generatorState == GeneratorState.Off)
				{
					Inventory.ServerTransfer(interaction.HandSlot, itemSlot, ReplacementStrategy.DropOther);
					UpdateCell();
					Chat.AddExamineMsg(interaction.Performer,"You load the reactor with the fusion cell.");
				}
				else
				{
					if(generatorState == GeneratorState.On) Chat.AddWarningMsgFromServer(interaction.Performer, "The reactor needs to be turned off first.");
					else Chat.AddWarningMsgFromServer(interaction.Performer, "Fusion cell can not be loaded in current reactor state, please seek repairs.");
				}
			}

			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Crowbar)) PerformCrowbarInteraction(interaction);

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
			else if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Welder))
			{
				firstPersonMessages = new string[2] { "You start welding", "You weld" };
				thirdPersonMessages = new string[2] { "starts welding", "welds" };
				targetName = "internal damage";
				repairedState = GeneratorState.WireExposed;

				PerformRepair(interaction, repairedState, 1/4f, firstPersonMessages, thirdPersonMessages, targetName);
			}
		}

		public void PerformCrowbarInteraction(HandApply interaction)
		{
			if (currentCell == null) return;

			if(generatorState == GeneratorState.On)
			{
				Chat.AddWarningMsgFromServer(interaction.Performer, "The reactor needs to be turned off first.");
				return;
			}
			if(generatorState != GeneratorState.Off)
			{
				Chat.AddWarningMsgFromServer(interaction.Performer, "The fusion cell can not be removed in current reactor state, please seek repairs.");
				return;
			}
			currentCell.UpdateSprite();
			itemSlot.ItemStorage.ServerDropAll();
			currentCell = null;
			Chat.AddExamineMsg(interaction.Performer, "You remove the fusion cell from the reactor.");

			return;
		}

		private void PerformRepair(HandApply interaction, GeneratorState repairState, float targetIntegrity, string[] firstPersonMessages, string[] thirdPersonMessages, string targetName)
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

		private bool UpdateCell()
		{
			if (itemSlot.Item == null) return false;

			if (itemSlot.Item.TryGetComponent<FusionCell>(out var newCell))
			{
				currentCell = newCell;
				return true;
			}
			else
			{
				Debug.LogError($"Fusion cell for {gameObject.name} has no FusionCell component!");
			}

			return false;
		}

		private bool TryToggleOn()
		{
			if(currentCell == null || currentCell.FuelPercent <= 0) return false;
			if(generatorState != GeneratorState.Off) return false; //Cannot be turned on if damaged or already on.

			ToggleOn();

			return true;
		}

		private void ToggleOn()
		{
			UpdateManager.Add(UpdateMe, 5f); //Due to slow consumption of fuel we dont need to do this often
			UpdateMe();
			currentBarOffset = totalBarOffset * (100-currentCell.FuelPercent) / 100;
			moduleSupplyingDevice.TurnOnSupply();
			generatorState = GeneratorState.On;
		}

		private void ToggleOff()
		{
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateMe);
			currentBarOffset = totalBarOffset;
			moduleSupplyingDevice.TurnOffSupply();
			generatorState = GeneratorState.Off;
		}

		private void UpdateMe()
		{
			if(currentCell == null || currentCell.FuelPercent <= 0)
			{
				ToggleOff();
				return;
			}

			currentCell.ConsumeFuel(5*currentCell.FuelPercent/100);

			currentBarOffset = totalBarOffset * (100-currentCell.FuelPercent) / 100;

			moduleSupplyingDevice.ProducingWatts = Math.Clamp(SupplyWattage * 2 * currentCell.FuelPercent / 100, 0, SupplyWattage); //Loses wattage as fuel cell drains, but only after 50% fuelPercent. After which, every % of fuel lost results in 2% power loss
		}

	

		#endregion

		#region Syncs

		private void OnSyncState(GeneratorState oldState, GeneratorState newState)
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

		private void OnSyncVisual(float oldPos, float newPos)
		{
			currentBarOffset = newPos;

			barVisual.transform.localPosition = new Vector3(0, currentBarOffset, 0);
		}

		#endregion
	}
}
