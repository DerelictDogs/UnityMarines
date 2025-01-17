﻿using System.Collections;
using UnityEngine;
using UI.Core.NetUI;
using Systems.Cargo;
using Objects.Cargo;

namespace UI.Objects.Cargo
{
	public class GUI_Cargo : NetTab
	{
		public NetText_label СreditsText;
		public NetText_label StatusText;
		public NetPageSwitcher NestedSwitcher;

		[SerializeField]
		private NetText_label raiseButtonText;

		public CargoConsole cargoConsole;

		public GUI_CargoPageCart pageCart;
		public GUI_CargoPageSupplies pageSupplies;
		public GUI_CargoOfflinePage OfflinePage;
		public GUI_CargoPageStatus statusPage;

		[SerializeField]
		private CargoCategory[] categories;

		protected override void InitServer()
		{
			CargoManager.Instance.OnCreditsUpdate.AddListener(UpdateCreditsText);
			CargoManager.Instance.OnConnectionChangeToCentComm.AddListener(SwitchToOfflinePage);
			CargoManager.Instance.OnElevatorUpdate.AddListener(UpdateStatusText);

			foreach (var page in NestedSwitcher.Pages)
			{
				page.GetComponent<GUI_CargoPage>().cargoGUI = this;
			}

			UpdateCreditsText();
			UpdateStatusText();
			StartCoroutine(WaitForProvider());
		}

		private IEnumerator WaitForProvider()
		{
			while (Provider == null)
			{
				yield return WaitFor.EndOfFrame;
			}
			cargoConsole = Provider.GetComponent<CargoConsole>();
			cargoConsole.cargoGUI = this;
			pageCart.SetUpTab();
		}

		public void OpenTab(NetPage pageToOpen)
		{
			NestedSwitcher.SetActivePage(CargoManager.Instance.CargoOffline ? OfflinePage : pageToOpen);
			//(Max) : NetUI shinangins where pages would randomly be null and kick players on headless servers.
			//This is a workaround to stop people from getting kicked. In-game reason would be this : Solar winds obstruct communications between CC and the station.
			if (pageToOpen == null) pageToOpen = OfflinePage;
			var cargopage = pageToOpen.GetComponent<GUI_CargoPage>();
			cargopage.OpenTab();
			cargopage.UpdateTab();
		}

		public void OpenCategory(int category)
		{
			pageSupplies.cargoCategory = categories[category];
			OpenTab(pageSupplies);
		}

		private void UpdateCreditsText()
		{
			if(CargoManager.Instance.CargoOffline)
			{
				СreditsText.SetValueServer("OFFLINE");
				return;
			}
			СreditsText.SetValueServer($"Credits: {CargoManager.Instance.Credits}");
			if (cargoConsole != null) { cargoConsole.PlayBudgetUpdateSound(); }
		}

		private void UpdateStatusText()
		{
			string[] statusText = new string[4] { "Raising", "Raised", "Lowering", "Lowered" };

			if (CargoManager.Instance.CargoOffline)
			{
				StatusText.SetValueServer("OFFLINE");
				return;
			}
			if(CargoManager.Instance.ElevatorStatus == ElevatorStatus.IsUp)
			{
				raiseButtonText.SetValueServer("    Lower");
			}
			if (CargoManager.Instance.ElevatorStatus == ElevatorStatus.IsDown)
			{
				raiseButtonText.SetValueServer("    Raise");
			}

			statusPage.UpdateTab();
			StatusText.SetValueServer($"Status: {statusText[(int)CargoManager.Instance.ElevatorStatus]}");
		}

		public void CallElevator()
		{
			if(CargoManager.Instance.CargoOffline) return;
			CargoManager.Instance.CallElevator();
		}

		public void ResetId()
		{
			cargoConsole.ResetID();
		}

		private void SwitchToOfflinePage()
		{
			//If the event has been invoked and cargo is online, ignore.
			if (CargoManager.Instance.CargoOffline == false)
			{
				pageCart.SetUpTab();
				return;
			}
			OpenTab(OfflinePage);
			ResetId();
		}
	}
}
