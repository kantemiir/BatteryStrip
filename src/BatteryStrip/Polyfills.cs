// Types the C# compiler expects but .NET Framework 4.8 does not ship.

namespace System.Runtime.CompilerServices
{
    /// <summary>Lets records and init-only setters compile for .NET Framework.</summary>
    internal static class IsExternalInit { }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
    {
        public bool ReturnValue { get; } = returnValue;
    }
}
