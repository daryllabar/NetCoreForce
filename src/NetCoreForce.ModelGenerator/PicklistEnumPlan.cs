using System.Collections.Generic;

namespace NetCoreForce.ModelGenerator
{
    public sealed class PicklistEnumPlan
    {
        public List<SharedPicklistEnum> SharedEnums { get; } = new List<SharedPicklistEnum>();

        /// <summary>
        /// Local (unshared) enums keyed by SObject API name. Emitted in the SObject .cs file after the class.
        /// </summary>
        public Dictionary<string, List<NestedPicklistEnum>> NestedEnumsByObject { get; } =
            new Dictionary<string, List<NestedPicklistEnum>>();

        /// <summary>
        /// Enum properties keyed by SObject API name, in field generation order.
        /// </summary>
        public Dictionary<string, List<PicklistEnumProperty>> PropertiesByObject { get; } =
            new Dictionary<string, List<PicklistEnumProperty>>();
    }

    public sealed class SharedPicklistEnum
    {
        public string TypeName { get; set; }
        public List<PicklistEnumMember> Members { get; set; }
    }

    public sealed class NestedPicklistEnum
    {
        public string TypeName { get; set; }
        public List<PicklistEnumMember> Members { get; set; }
    }

    public sealed class PicklistEnumProperty
    {
        public string FieldName { get; set; }
        public string PropertyName { get; set; }
        public string EnumTypeName { get; set; }
        public bool IsMultipicklist { get; set; }
    }

    public sealed class PicklistEnumMember
    {
        public string Identifier { get; set; }
        public string ApiValue { get; set; }
    }
}
