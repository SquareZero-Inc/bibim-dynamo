# ApiIndex Schema

- `ApiIndex`
  - `AssemblyIdentity`
  - `RevitVersionHint`
  - `Types: Dictionary<string, TypeInfo>`
  - `BuiltInParameters`
  - `BuiltInCategories`
  - `UnitTypeIds`

- `TypeInfo`
  - `Name`
  - `Members`
  - `MethodSignatures: Dictionary<string, List<MethodSig>>`
  - `IsDeprecated`

- `MethodSig`
  - `Name`
  - `MinArgs`
  - `MaxArgs`
  - `HasParamArray`

Runtime source is loaded RevitAPI reflection in host process; XML is supplemental.
