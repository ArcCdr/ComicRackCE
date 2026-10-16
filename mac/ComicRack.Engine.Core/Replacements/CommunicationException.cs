using System;

namespace System.ServiceModel
{
	// Replacement: WCF is not part of .NET 10. Sync/WirelessSyncProvider.cs throws and catches it for Wi-Fi sync,
	// which is deferred (D4).
	public class CommunicationException : Exception
	{
		public CommunicationException()
		{
		}

		public CommunicationException(string message)
			: base(message)
		{
		}

		public CommunicationException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
