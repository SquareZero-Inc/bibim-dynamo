using System.Collections.Generic;

namespace BIBIM_MVP
{
    internal sealed class ApiIndex
    {
        public string AssemblyIdentity { get; set; }
        public string RevitVersionHint { get; set; }
        public Dictionary<string, TypeInfo> Types { get; set; }
        public HashSet<string> BuiltInParameters { get; set; }
        public HashSet<string> BuiltInCategories { get; set; }
        public HashSet<string> UnitTypeIds { get; set; }
    }

    internal sealed class TypeInfo
    {
        public string Name { get; set; }
        public HashSet<string> Members { get; set; }
        public Dictionary<string, List<MethodSig>> MethodSignatures { get; set; }
        public bool IsDeprecated { get; set; }
    }

    internal sealed class MethodSig
    {
        public string Name { get; set; }
        public int MinArgs { get; set; }
        public int MaxArgs { get; set; }
        public bool HasParamArray { get; set; }
    }
}
