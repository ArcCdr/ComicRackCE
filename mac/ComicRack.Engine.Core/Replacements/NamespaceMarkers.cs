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
	// Removed when M1.3 links the Ceco sources (if needed).
	internal static class NamespaceMarker
	{
	}
}
