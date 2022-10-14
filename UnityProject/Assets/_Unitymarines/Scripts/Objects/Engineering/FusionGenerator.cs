using System;
using UnityEngine;
using Systems.Electricity.NodeModules;
using UnityMarines.Items.Engineering;

namespace UnityMarines.Objects.Engineering
{
	/// <summary>
	/// Wanted to make this a subclass of PowerGenerator.cs, however shortly discovered it would be more complicated to do so then just have a standalone class
	/// </summary>
	[RequireComponent(typeof(ItemStorage))]
	public class FusionGenerator : RepairableGenerator
	{
		[Tooltip("The cell item that will placed in this generator on spawn, leave blank for no starting cell.")]
		[SerializeField]
		private GameObject initialFusionCell = null;

		private FusionCell currentCell = null;

		[Tooltip("Fusion cell item trait")]
		[SerializeField]
		private ItemTrait cellTrait = null;

		private ItemSlot itemSlot;

		private const float totalBarOffset = -0.344f; //How large is the power bar in units

		#region Lifecycle

		private void Awake()
		{
			registerTile = GetComponent<RegisterTile>();
			baseSpriteHandler = GetComponentInChildren<SpriteHandler>();
			integrity = GetComponent<Integrity>();
			moduleSupplyingDevice = GetComponent<ModuleSupplyingDevice>();

			var itemStorage = GetComponent<ItemStorage>();
			itemSlot = itemStorage.GetIndexedItemSlot(0);
		}

		public override void OnSpawnServer(SpawnInfo info)
		{
			if (generatorState == GeneratorState.On)
			{
				if (initialFusionCell == null) return;
	
				itemSlot._ServerSetItem(initialFusionCell.GetComponent<Pickupable>());

				UpdateCell();
				TryToggleOn();
			}

			if (generatorState == GeneratorState.GlassBroken) integrity.ApplyDamage(0.75f * integrity.initialIntegrity, AttackType.Melee,DamageType.Brute);
			else if (generatorState == GeneratorState.WireExposed) integrity.ApplyDamage(0.5f * integrity.initialIntegrity, AttackType.Melee, DamageType.Brute);
			else if (generatorState == GeneratorState.PanelOff) integrity.ApplyDamage(0.25f * integrity.initialIntegrity, AttackType.Melee, DamageType.Brute); //Adjust health from starting state

			integrity.OnApplyDamage.AddListener(OnTakeDamage);
		}

		#endregion

		#region Interaction

		public override string Examine(Vector3 worldPos = default)
		{
			var stateList = new string[5] {"running", "turned off", "minorly damaged", "moderately damaged", "heavily damaged"};
			var repairText = new string[3] { "Use a wrench to repair it." , "Use a wirecutters, then wrench to repair it.", "Use a blowtorch, then wirecutters, then wrench to repair it." };

			var examineText = "";
			if(currentCell == null)
			{
				examineText += "\nNo fusion cell in generator.";
			}
			else
			{
				examineText += $"\nInternal fusion cell at: {MathF.Round(currentCell.FuelPercent,2)}% capacity.";
			}

			examineText += $"\nThe generator is currently {stateList[(int)generatorState]}.";

			if (generatorState != GeneratorState.On && generatorState != GeneratorState.Off)
			{
				examineText += repairText[(int)generatorState - 2];
			}

			return examineText;
		}

		public override bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side) == false) return false;

			if (Validations.HasItemTrait(interaction.HandObject, cellTrait)) return true; //Adding a new power cell
			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Crowbar)) return true; //Removing a power cell

			return base.WillInteract(interaction, side);
		}

		public override void ServerPerformInteraction(HandApply interaction)
		{
			if (Validations.HasItemTrait(interaction.HandObject, cellTrait)) //Add fustion cell interaction
			{
				if (currentCell != null)
				{
					Chat.AddWarningMsgFromServer(interaction.Performer, $"You need to remove the fuel cell from the reactor first.");
					return;
				}
				if (generatorState == GeneratorState.Off)
				{
					Inventory.ServerTransfer(interaction.HandSlot, itemSlot, ReplacementStrategy.DropOther);
					UpdateCell();
					Chat.AddExamineMsg(interaction.Performer, "You load the reactor with the fusion cell.");
				}
				else
				{
					if (generatorState == GeneratorState.On) Chat.AddWarningMsgFromServer(interaction.Performer, "The reactor needs to be turned off first.");
					else Chat.AddWarningMsgFromServer(interaction.Performer, "Fusion cell can not be loaded in current reactor state, please seek repairs.");
				}
			}

			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Crowbar)) PerformCrowbarInteraction(interaction);

			base.ServerPerformInteraction(interaction);
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

		protected override bool TryToggleOn()
		{
			if(currentCell == null || currentCell.FuelPercent <= 0) return false;
			if(generatorState != GeneratorState.Off) return false; //Cannot be turned on if damaged or already on.

			ToggleOn();

			return true;
		}

		protected override void ToggleOn()
		{
			UpdateMe();
			UpdateManager.Add(UpdateMe, 5f); //Due to slow consumption of fuel we dont need to do this often

			moduleSupplyingDevice.TurnOnSupply();
			generatorState = GeneratorState.On;
		}

		protected override void ToggleOff()
		{
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateMe);
			currentBarOffset = totalBarOffset;
			moduleSupplyingDevice.TurnOffSupply();
			generatorState = GeneratorState.Off;
		}

		protected override void UpdateMe()
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
	}
}
