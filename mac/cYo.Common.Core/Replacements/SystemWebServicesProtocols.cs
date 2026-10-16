// Replacement: System.Web.Services does not exist on .NET 10. cYo.Common/Net/HttpAccess.cs imports
// System.Web.Services.Protocols but only uses it in commented-out SOAP code, so the namespace only has to exist.
namespace System.Web.Services.Protocols
{
	internal static class NamespaceMarker
	{
	}
}
