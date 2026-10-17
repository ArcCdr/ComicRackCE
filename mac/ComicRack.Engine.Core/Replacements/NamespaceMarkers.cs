// Replacements: namespaces that Engine files import but that do not exist, where the import is unused (or only
// the types declared elsewhere in Replacements/ are used). The namespace only has to exist.

namespace System.IdentityModel.Tokens
{
	// ComicRack.Engine/Metadata/VirtualTags/VirtualTagsCollection.cs imports it without using it.
	internal static class NamespaceMarker
	{
	}
}

namespace cYo.Common.Presentation.Ceco
{
	// ComicRack.Engine/IO/Provider/XmlInfo/XmlInfoProviderFactory.cs imports it without using it.
	// The Ceco sources are not linked (nothing uses a Ceco type), so only the namespace exists.
	internal static class NamespaceMarker
	{
	}
}
