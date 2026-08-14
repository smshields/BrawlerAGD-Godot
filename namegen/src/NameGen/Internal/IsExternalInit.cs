// Shim so C# records/init-only setters compile on netstandard2.1.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
