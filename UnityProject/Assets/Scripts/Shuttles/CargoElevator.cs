using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Objects;
using Items;

namespace Systems.Cargo
{
	public class CargoElevator : MonoBehaviour
	{
		public static CargoElevator Instance;

		private List<Vector3Int> availableSpawnSlots = new List<Vector3Int>();

		[SerializeField]
		private Vector2 Size = new Vector2();

		private static HashSet<UniversalObjectPhysics> objectsOnElevator  = new HashSet<UniversalObjectPhysics>();

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				Destroy(this);
			}
		}

		private void OnDrawGizmos() //Shows elevator bounds for mapping. Starting from bottom left
		{
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireCube(transform.position + new Vector3((Size.x/2) - 0.5f, (Size.y/2) - 0.5f,0), Size);
		}

		private void OnEnable()
		{
			if(CustomNetworkManager.IsServer == false) return;

			UpdateManager.Add(CallbackType.UPDATE, UpdateMe);
		}

		private void OnDisable()
		{
			if(CustomNetworkManager.IsServer == false) return;

			UpdateManager.Remove(CallbackType.UPDATE, UpdateMe);
		}


		//Server Side Only
		private void UpdateMe()
		{
			if (CargoManager.Instance.CurrentTravelTime <= 0f)
			{
				if (CargoManager.Instance.ElevatorStatus == ElevatorStatus.TravellingDown)
				{
					FindObjects();
					UnloadCargo();
				}
				if (CargoManager.Instance.ElevatorStatus == ElevatorStatus.TravellingDown || CargoManager.Instance.ElevatorStatus == ElevatorStatus.TravellingUp) CargoManager.Instance.OnElevatorArrival();
			}
		}

		public void FindObjects()
		{
			objectsOnElevator.Clear();

			Vector3Int currentPosition = transform.position.RoundToInt();
			
			for(int x = 0; x < Size.x; x++)
			{
				for(int y = 0; y < Size.y; y++)
				{				
					Vector3Int positionToCheck = currentPosition + new Vector3Int(x, y, 0);

					IEnumerable<UniversalObjectPhysics> objs = MatrixManager.GetAt<UniversalObjectPhysics>(positionToCheck, true).Distinct();

					objectsOnElevator = objectsOnElevator.Concat(objs).ToHashSet();
				}
			}
		}

		public void MoveDown()
		{
			//Add move down animation and rails
		}

		public void MoveUp()
		{
			//Add move up animation and rails
		}

		public static HashSet<UniversalObjectPhysics> FetchObjects()
		{
			return objectsOnElevator;
		}

		/// <summary>
		/// Calls CargoManager.DestroyItem() for all items on the shuttle.
		/// Server only.
		/// </summary>
		private void UnloadCargo()
		{
			//track what we've already sold so it's not sold twice.
			HashSet<GameObject> alreadySold = new HashSet<GameObject>();
			var seekingItemTraitsForBounties = new List<ItemTrait>();
			foreach(var bounty in CargoManager.Instance.ActiveBounties)
			{
				seekingItemTraitsForBounties.AddRange(bounty.Demands.Keys);
			}

			bool hasBountyTrait(Attributes attribute)
			{
				if (attribute is ItemAttributesV2 c)
				{
					return c.HasAnyTrait(seekingItemTraitsForBounties);
				}
				return false;
			}

			foreach (UniversalObjectPhysics item in objectsOnElevator)
			{
				//need VisibleState check because despawned objects still stick around on their matrix transform
				if (item.IsVisible)
				{
					if (item.TryGetComponent<Attributes>(out var attributes))
					{
						// Items that cannot be sold in cargo will be ignored unless they have a trait that is assoicated with a bounty
						if (attributes.CanBeSoldInCargo == false && hasBountyTrait(attributes) == false) continue;

						// Don't sell secured objects e.g. conveyors.
						if (attributes.CanBeSoldInCargo && item.IsNotPushable) continue;
					}
					CargoManager.Instance.ProcessCargo(item.gameObject, alreadySold);
				}
			}
		}

		/// <summary>
		/// Do some stuff you need to do before spawning orders.
		/// Called once.
		/// </summary>
		public void PrepareSpawnOrders()
		{
			GetAvailablePositions();
		}

		/// <summary>
		/// Spawns the order inside elevator.
		/// Server only.
		/// </summary>
		/// <param name="order">Order to spawn.</param>
		public bool SpawnOrder(CargoOrderSO order)
		{
			Vector3 pos = GetRandomFreePos();
			if (pos == TransformState.HiddenPos)
				return (false);

			var crate = Spawn.ServerPrefab(order.Crate, pos).GameObject;
			Dictionary<GameObject, Stackable> stackableItems = new Dictionary<GameObject, Stackable>();
			//error occurred trying to spawn, just ignore this order.
			if (crate == null) return true;
			if (crate.TryGetComponent<ObjectContainer>(out var container))
			{
				for (int i = 0; i < order.Items.Count; i++)
				{
					var entryPrefab = order.Items[i];
					if (entryPrefab == null)
					{
						Logger.Log($"Error with order fulfilment. Can't add items index: {i} for {order.OrderName} as the prefab is null. Skipping..", Category.Cargo);
						continue;
					}

					if (!stackableItems.ContainsKey(entryPrefab))
					{
						var orderedItem = Spawn.ServerPrefab(order.Items[i], pos).GameObject;
						if (orderedItem == null)
						{
							//let the shuttle still be able to complete the order empty otherwise it will be stuck permantly
							Logger.Log($"Can't add ordered item to create because it doesn't have a GameObject", Category.Cargo);
							continue;
						}

						var stackableItem = orderedItem.GetComponent<Stackable>();
						if (stackableItem != null)
						{
							stackableItems.Add(entryPrefab, stackableItem);
						}

						AddItemToCrate(container, orderedItem);
					}
					else
					{
						if (stackableItems[entryPrefab].Amount < stackableItems[entryPrefab].MaxAmount)
						{
							stackableItems[entryPrefab].ServerIncrease(1);
						}
						else
						{
							//Start a new one to start stacking
							var orderedItem = Spawn.ServerPrefab(entryPrefab, pos).GameObject;
							if (orderedItem == null)
							{
								//let the shuttle still be able to complete the order empty otherwise it will be stuck permantly
								Logger.Log($"Can't add ordered item to create because it doesn't have a GameObject", Category.Cargo);
								continue;
							}

							var stackableItem = orderedItem.GetComponent<Stackable>();
							stackableItems[entryPrefab] = stackableItem;

							AddItemToCrate(container, orderedItem);
						}
					}
				}
			}
			else
			{
				Logger.LogWarning($"{crate.ExpensiveName()} does not have {nameof(UniversalObjectPhysics)}. Please fix CargoData" +
								  $" to ensure that the crate prefab is actually a crate (with {nameof(UniversalObjectPhysics)} component)." +
								  $" This order will be ignored.", Category.Cargo);
				return true;
			}

			CargoManager.Instance.CentcomMessage += "Loaded " + order.OrderName + " onto elevator.\n";
			return (true);
		}

		private void AddItemToCrate(ObjectContainer container, GameObject obj)
		{
			//ensure it is added to crate
			if (obj.TryGetComponent<RandomItemSpot>(out var randomItem))
			{
				var registerTile = container.gameObject.RegisterTile();
				var items = registerTile.Matrix.Get<UniversalObjectPhysics>(registerTile.LocalPositionServer, ObjectType.Item, true)
						.Select(ob => ob.gameObject).Where(go => go != obj);

				container.StoreObjects(items);
			}
			else
			{
				container.StoreObject(obj);
			}
		}

		/// <summary>
		/// Get all unoccupied positions inside elevator.
		/// Needs to be called before starting to spawn orders.
		/// </summary>
		private void GetAvailablePositions()
		{
			Vector3Int pos = transform.position.RoundToInt();

			availableSpawnSlots = new List<Vector3Int>();

			for (int i = 0; i <= Size.y; i++)
			{
				for (int j = 0; j <= Size.x; j++)
				{		
					Vector3Int offset = new Vector3Int(j, i, 0);

					if ((MatrixManager.Instance.GetFirst<ClosetControl>(pos + offset, true) == null) &&
						MatrixManager.IsFloorAt(pos + offset, true))
					{
						availableSpawnSlots.Add(pos + offset);
					}
				}
			}
		}

		/// <summary>
		/// Gets random unoccupied position inside elevator.
		/// </summary>
		private Vector3 GetRandomFreePos()
		{
			Vector3Int spawnPos;

			if (availableSpawnSlots.Count > 0)
			{
				spawnPos = availableSpawnSlots[UnityEngine.Random.Range(0, availableSpawnSlots.Count)];
				availableSpawnSlots.Remove(spawnPos);
				return spawnPos;
			}

			return TransformState.HiddenPos;
		}
	}
}
