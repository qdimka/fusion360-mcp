// Attribute polyfills for netstandard2.0 (C# 10+ language features on an
// older runtime). Standard community polyfill; consumed by records,
// init accessors, and required members used throughout this project.
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    /// <summary>Marks an init-only accessor as externally visible.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    internal sealed class IsExternalInit : Attribute
    {
    }

    /// <summary>Requests a compiler feature (e.g. "RequiredMembers").</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }

        public bool IsOptional { get; set; }

        public string? MessageID { get; set; }

        public string? MessageFormat { get; set; }

        public object?[]? MessageArgs { get; set; }
    }

    /// <summary>
    /// Marks a type that must have all of its required members set
    /// before it can be used.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }
}
#endif
