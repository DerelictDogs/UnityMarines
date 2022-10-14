using UnityEngine;
using UnityMarines.Items.Engineering;
using Systems.Electricity;

namespace UnityMarines.Objects.Engineering
{
	public class Recycler : MonoBehaviour, ICheckedInteractable<PositionalHandApply>, IAPCPowerable
	{
		[SerializeField]
		private SpriteClickRegion leftRegion = default;
		[SerializeField]
		private SpriteHandler leftIndicator = null;
		[SerializeField]
		private SpriteHandler leftCell = null;

		[SerializeField]
		private SpriteClickRegion rightRegion = default;
		[SerializeField]
		private SpriteHandler rightIndicator = null;
		[SerializeField]
		private SpriteHandler rightCell = null;

		[SerializeField]
		private SpriteHandler mainSprite = null;

		[Tooltip("Fusion cell item trait")]
		[SerializeField]
		private ItemTrait cellTrait = null;

		private APCPoweredDevice poweredDevice;

		private ItemSlot leftSlot;
		private FusionCell leftCellItem;

		private ItemSlot rightSlot;
		private FusionCell rightCellItem;

		private bool isUpdating = false;


		private void Awake()
		{
			poweredDevice = GetComponent<APCPoweredDevice>();
			
			var itemStorage = GetComponent<ItemStorage>();

			leftSlot = itemStorage.GetIndexedItemSlot(0);
			rightSlot = itemStorage.GetIndexedItemSlot(1);
		}

		#region Interaction

		public virtual bool WillInteract(PositionalHandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side) == false) return false;
			if (interaction.Intent == Intent.Harm) return false;
			if (interaction.TargetObject != gameObject) return false;

			if (interaction.HandObject == null) return true; //Turning on and off

			if (Validations.HasItemTrait(interaction.HandObject, cellTrait)) return true; //Adding a new power cell

			return false;
		}

		public void ServerPerformInteraction(PositionalHandApply interaction)
		{
			ItemSlot targetSlot;

			if (leftRegion.Contains(interaction.WorldPositionTarget)) targetSlot = leftSlot;
			else if (rightRegion.Contains(interaction.WorldPositionTarget)) targetSlot= rightSlot;
			else return;

			if(interaction.UsedObject != null)
			{
				if (targetSlot.IsOccupied)
				{
					Chat.AddWarningMsgFromServer(interaction.Performer, "This slot is already occupied!");
					return;
				}
				if(interaction.UsedObject.TryGetComponent<FusionCell>(out var cell))
				{
					if(cell == null) return;
					Inventory.ServerTransfer(interaction.HandSlot, targetSlot);

					if (targetSlot == leftSlot) leftCellItem = cell;
					else rightCellItem = cell;

					UpdateCellSprites();
					UpdateMe();

					if (isUpdating == false)
					{
						UpdateManager.Add(UpdateMe, 2f);
					}
				}
				
			}
			else if (targetSlot.IsOccupied)
			{
				var cell = getCell(targetSlot);

				if(cell == null) return;
				cell.UpdateSprite();
				cell = null;

				Inventory.ServerTransfer(targetSlot, interaction.HandSlot);
				if(leftSlot.IsEmpty && rightSlot.IsEmpty)
				{
					isUpdating = false;
					UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateMe);
				}

				UpdateCellSprites();
				UpdateMe();
			}		
		}

		#endregion

		public FusionCell getCell(ItemSlot slot)
		{
			if (slot == leftSlot) return leftCellItem;

			return rightCellItem;
		}

		private void UpdateCellSprites()
		{
			if(leftSlot.IsOccupied) leftCell.ChangeSpriteVariant(0);
			else leftCell.ChangeSpriteVariant(2);

			if (rightSlot.IsOccupied) rightCell.ChangeSpriteVariant(1);
			else rightCell.ChangeSpriteVariant(2);

			if(leftSlot.IsOccupied || rightSlot.IsOccupied) mainSprite.ChangeSpriteVariant(1);
			else mainSprite.ChangeSpriteVariant(0);
		}

		private void UpdateMe()
		{
			int wattage = 0;

			if (leftCellItem != null)
			{
				leftCellItem.ReplenishFuel(5);

				if (leftCellItem.FuelPercent == 100) leftIndicator.ChangeSpriteVariant(1);
				else
				{
					wattage += 800;
					leftIndicator.ChangeSpriteVariant(0);
				}
			}
			else leftIndicator.ChangeSpriteVariant(2);

			if (rightCellItem != null)
			{
				rightCellItem.ReplenishFuel(5);

				if (rightCellItem.FuelPercent == 100) rightIndicator.ChangeSpriteVariant(1);
				else
				{
					wattage += 800;
					rightIndicator.ChangeSpriteVariant(0);
				}
			}
			else rightIndicator.ChangeSpriteVariant(2);

			SetWattage(wattage);
		}

		#region IAPCPowerable

		public void StateUpdate(PowerState state)
		{
			if (isUpdating && state == PowerState.Off)
			{
				isUpdating = false;
				UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateMe);
			}
			if (isUpdating == false && state == PowerState.On)
			{
				isUpdating = true;
				UpdateManager.Add(UpdateMe, 2f);
			}

			UpdateMe();
		}

		public void PowerNetworkUpdate(float voltage){ }

		private void SetWattage(float wattage)
		{
			poweredDevice.Wattusage = wattage;
		}

		#endregion

	}
}
