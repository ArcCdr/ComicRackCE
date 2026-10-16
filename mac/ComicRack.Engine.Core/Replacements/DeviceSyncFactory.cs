using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using cYo.Common.Text;

namespace cYo.Projects.ComicRack.Engine.Sync
{
	// Replacement for ComicRack.Engine/Sync/DeviceSyncFactory.cs (excluded: it enumerates Windows portable devices).
	// Device sync is deferred (D4): no device is ever discovered.
	public static class DeviceSyncFactory
	{
		private static List<IPAddress> extraWifiDeviceAddresses = new List<IPAddress>();

		public static IEnumerable<IPAddress> ExtraWifiDeviceAddresses => extraWifiDeviceAddresses;

		public static IEnumerable<ISyncProvider> Discover()
		{
			return Enumerable.Empty<ISyncProvider>();
		}

		public static IEnumerable<ISyncProvider> Discover(IEnumerable<IPAddress> adresses)
		{
			return Enumerable.Empty<ISyncProvider>();
		}

		public static ISyncProvider Create(string deviceKey)
		{
			return null;
		}

		public static void SetExtraWifiDeviceAddresses(string addresses)
		{
			extraWifiDeviceAddresses = new List<IPAddress>(ParseWifiAddressList(addresses));
		}

		public static IEnumerable<IPAddress> ParseWifiAddressList(string addresses)
		{
			foreach (string item in addresses.Split(',', ';').TrimStrings().RemoveEmpty()
				.Distinct())
			{
				if (IPAddress.TryParse(item, out var address))
				{
					yield return address;
				}
			}
		}
	}
}
