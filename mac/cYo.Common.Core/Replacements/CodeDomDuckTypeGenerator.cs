using System;

namespace cYo.Common.Reflection
{
	// Replacement for cYo.Common/Reflection/CodeDomDuckTypeGenerator.cs (excluded: CodeDom compilation is not used
	// on the .NET 10 path). Duck typing keeps its public surface; creating duck types throws until a
	// Reflection.Emit generator replaces this class.
	internal class CodeDomDuckTypeGenerator : IDuckTypeGenerator
	{
		public Type[] CreateDuckTypes(Type interfaceType, Type[] duckedTypes)
		{
			throw new NotSupportedException("cYo.Common.Reflection.CodeDomDuckTypeGenerator is not yet supported on macOS");
		}
	}
}
