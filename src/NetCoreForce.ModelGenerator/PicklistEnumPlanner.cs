using System;
using System.Collections.Generic;
using System.Linq;
using NetCoreForce.Client.Models;

namespace NetCoreForce.ModelGenerator
{
    public static class PicklistEnumPlanner
    {
        public static bool IsEnumFieldType(string fieldType)
        {
            if (string.IsNullOrEmpty(fieldType))
            {
                return false;
            }

            return fieldType.Equals("picklist", StringComparison.OrdinalIgnoreCase)
                || fieldType.Equals("combobox", StringComparison.OrdinalIgnoreCase)
                || fieldType.Equals("multipicklist", StringComparison.OrdinalIgnoreCase);
        }

        public static string Fingerprint(IEnumerable<PickListValue> values)
        {
            var apiValues = DistinctApiValues(values);
            if (apiValues.Count == 0)
            {
                return null;
            }

            return string.Join("\n", apiValues);
        }

        public static List<string> DistinctApiValues(IEnumerable<PickListValue> values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values
                .Where(v => v != null && !string.IsNullOrEmpty(v.Value))
                .Select(v => v.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
        }

        public static PicklistEnumPlan Build(
            IReadOnlyList<DescribedObject> objects,
            GenConfig config)
        {
            var plan = new PicklistEnumPlan();
            if (objects == null || objects.Count == 0 || config == null || !config.GenerateEnumProperties)
            {
                return plan;
            }

            var fields = new List<PicklistFieldCandidate>();
            foreach (var obj in objects)
            {
                if (obj?.Describe?.Fields == null)
                {
                    continue;
                }

                foreach (var field in IncludedFields(obj.Describe, config))
                {
                    if (!IsEnumFieldType(field.Type))
                    {
                        continue;
                    }

                    var apiValues = DistinctApiValues(field.PicklistValues);
                    if (apiValues.Count == 0)
                    {
                        continue;
                    }

                    fields.Add(new PicklistFieldCandidate
                    {
                        ObjectApiName = obj.ObjectApiName,
                        ClassName = obj.ClassName,
                        Field = field,
                        Fingerprint = string.Join("\n", apiValues),
                        ApiValues = apiValues
                    });
                }
            }

            var usedTypeNames = new HashSet<string>(objects.Select(o => o.ClassName), StringComparer.Ordinal);
            var groups = fields
                .GroupBy(f => f.Fingerprint, StringComparer.Ordinal)
                .Select(g => g.OrderBy(f => f.ObjectApiName + "." + f.Field.Name, StringComparer.Ordinal).ToList())
                .ToList();
            var preferredNames = groups.Select(g => PreferredNameForGroup(g, config)).ToList();
            DropSingleTablePrefix(groups, preferredNames, usedTypeNames, config.ClassPrefix);

            var assignmentByField = new Dictionary<string, EnumTypeAssignment>(StringComparer.Ordinal);

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var groupFields = groups[groupIndex];

                var members = BuildMembers(groupFields[0].ApiValues);
                string enumTypeName;
                bool nested;

                if (groupFields.Count == 1)
                {
                    nested = true;
                    var owner = groupFields[0];
                    enumTypeName = ApplyEnumNameMapping(CsIdentifier.AllocateUnique(preferredNames[groupIndex], usedTypeNames), config.EnumNameMap);

                    if (!plan.NestedEnumsByObject.TryGetValue(owner.ObjectApiName, out var nestedList))
                    {
                        nestedList = new List<NestedPicklistEnum>();
                        plan.NestedEnumsByObject[owner.ObjectApiName] = nestedList;
                    }

                    nestedList.Add(new NestedPicklistEnum
                    {
                        TypeName = enumTypeName,
                        Members = members
                    });
                }
                else
                {
                    nested = false;
                    enumTypeName = ApplyEnumNameMapping(CsIdentifier.AllocateUnique(preferredNames[groupIndex], usedTypeNames), config.EnumNameMap);

                    plan.SharedEnums.Add(new SharedPicklistEnum
                    {
                        TypeName = enumTypeName,
                        Members = members
                    });
                }

                foreach (var field in groupFields)
                {
                    assignmentByField[FieldKey(field.ObjectApiName, field.Field.Name)] = new EnumTypeAssignment
                    {
                        EnumTypeName = enumTypeName,
                        IsNested = nested
                    };
                }
            }

            foreach (var obj in objects)
            {
                if (obj?.Describe?.Fields == null)
                {
                    continue;
                }

                var usedPropertyNames = CollectUsedPropertyNames(obj, config);
                var properties = new List<PicklistEnumProperty>();

                foreach (var field in IncludedFields(obj.Describe, config))
                {
                    if (!assignmentByField.TryGetValue(FieldKey(obj.ObjectApiName, field.Name), out var assignment))
                    {
                        continue;
                    }

                    string preferred = CsIdentifier.SanitizeMemberName(CsIdentifier.StripFieldName(field.Name));
                    string propertyName = CsIdentifier.AllocateUnique(preferred, usedPropertyNames);

                    properties.Add(new PicklistEnumProperty
                    {
                        FieldName = field.Name,
                        PropertyName = propertyName,
                        EnumTypeName = assignment.EnumTypeName,
                        IsMultipicklist = field.Type.Equals("multipicklist", StringComparison.OrdinalIgnoreCase)
                    });
                }

                if (properties.Count > 0)
                {
                    plan.PropertiesByObject[obj.ObjectApiName] = properties;
                }
            }

            plan.SharedEnums.Sort((a, b) => string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));
            return plan;
        }

        public static IEnumerable<SObjectFieldMetadata> IncludedFields(SObjectDescribeFull describe, GenConfig config)
        {
            if (describe?.Fields == null)
            {
                return Array.Empty<SObjectFieldMetadata>();
            }

            return describe.Fields
                .Where(f => f != null && (!f.Custom || config.IncludeCustom))
                .OrderBy(f => f.Name?.ToLowerInvariant());
        }

        private static List<PicklistEnumMember> BuildMembers(IReadOnlyList<string> apiValues)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<PicklistEnumMember>();
            foreach (var apiValue in apiValues)
            {
                string identifier = CsIdentifier.AllocateUniqueMember(CsIdentifier.SanitizeMemberName(apiValue), used);
                members.Add(new PicklistEnumMember
                {
                    Identifier = identifier,
                    ApiValue = apiValue
                });
            }

            return members;
        }

        private static HashSet<string> CollectUsedPropertyNames(DescribedObject obj, GenConfig config)
        {
            var used = new HashSet<string>(StringComparer.Ordinal) { "SObjectTypeName" };
            foreach (var field in IncludedFields(obj.Describe, config))
            {
                used.Add(field.Name);
                if (config.IncludeReferences
                    && field.Type == "reference"
                    && !string.IsNullOrEmpty(field.RelationshipName)
                    && field.ReferenceTo != null
                    && field.ReferenceTo.Count == 1
                    && field.RelationshipName != "ContentBody")
                {
                    used.Add(field.RelationshipName);
                }
            }

            return used;
        }

        private static string PreferredNameForGroup(IReadOnlyList<PicklistFieldCandidate> groupFields, GenConfig config)
        {
            if (groupFields.Count == 1)
            {
                var owner = groupFields[0];
                return IsOnlyYesAndNo(owner.ApiValues)
                    ? YesNoEnumName
                    : owner.ObjectApiName + "_" + CsIdentifier.SanitizeMemberName(CsIdentifier.StripFieldName(owner.Field.Name));
            }

            var candidateNames = groupFields
                .Select(f => (config.ClassPrefix ?? string.Empty)
                    + f.ObjectApiName
                    + CsIdentifier.SanitizeMemberName(CsIdentifier.StripFieldName(f.Field.Name))
                    + (config.ClassSuffix ?? string.Empty))
                .ToList();
            return IsOnlyYesAndNo(groupFields[0].ApiValues)
                ? YesNoEnumName
                : CommonCapitalizedSuffix(candidateNames) ?? ShortestName(candidateNames);
        }

        /// <summary>
        /// When one value set is reused only on a single SObject, drop that object's name from the
        /// generated enum name if the shorter name is not already a class or enum name.
        /// </summary>
        private static void DropSingleTablePrefix(
            IReadOnlyList<List<PicklistFieldCandidate>> groups,
            List<string> preferredNames,
            ISet<string> classNames,
            string classPrefix)
        {
            var reserved = new HashSet<string>(classNames, StringComparer.Ordinal);
            foreach (string name in preferredNames)
            {
                reserved.Add(name);
            }

            var stripOrder = Enumerable.Range(0, groups.Count)
                .Where(i => IsSingleTableReuse(groups[i]))
                .OrderBy(i => preferredNames[i], StringComparer.Ordinal)
                .ToList();

            foreach (int index in stripOrder)
            {
                string stripped = StripTablePrefix(preferredNames[index], groups[index][0].ObjectApiName, classPrefix);
                if (stripped != null && !reserved.Contains(stripped))
                {
                    preferredNames[index] = stripped;
                    reserved.Add(stripped);
                }
            }
        }

        private static bool IsSingleTableReuse(IReadOnlyList<PicklistFieldCandidate> groupFields)
        {
            if (groupFields == null || groupFields.Count < 2)
            {
                return false;
            }

            string objectApiName = groupFields[0].ObjectApiName;
            for (int i = 1; i < groupFields.Count; i++)
            {
                if (!string.Equals(groupFields[i].ObjectApiName, objectApiName, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Removes <paramref name="objectApiName"/> from the front of <paramref name="generatedName"/>,
        /// after <paramref name="classPrefix"/>. Returns null when the name does not have that prefix
        /// or the remainder would not start with an uppercase letter.
        /// </summary>
        public static string StripTablePrefix(string generatedName, string objectApiName, string classPrefix)
        {
            if (string.IsNullOrEmpty(generatedName) || string.IsNullOrEmpty(objectApiName))
            {
                return null;
            }

            classPrefix = classPrefix ?? string.Empty;
            if (!generatedName.StartsWith(classPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string afterPrefix = generatedName.Substring(classPrefix.Length);
            if (!afterPrefix.StartsWith(objectApiName, StringComparison.Ordinal)
                || afterPrefix.Length == objectApiName.Length)
            {
                return null;
            }

            string remainder = afterPrefix.Substring(objectApiName.Length);
            if (!char.IsUpper(remainder[0]))
            {
                return null;
            }

            return classPrefix + remainder;
        }

        public const string YesNoEnumName = "YesNo";

        /// <summary>
        /// True when the picklist API values are exactly "Yes" and "No". Match is case-sensitive.
        /// </summary>
        public static bool IsOnlyYesAndNo(IReadOnlyList<string> apiValues)
        {
            if (apiValues == null || apiValues.Count != 2)
            {
                return false;
            }

            bool hasYes = false;
            bool hasNo = false;
            foreach (string value in apiValues)
            {
                if (string.Equals(value, "Yes", StringComparison.Ordinal))
                {
                    hasYes = true;
                }
                else if (string.Equals(value, "No", StringComparison.Ordinal))
                {
                    hasNo = true;
                }
                else
                {
                    return false;
                }
            }

            return hasYes && hasNo;
        }

        /// <summary>
        /// Longest common suffix of <paramref name="names"/> that starts with an uppercase letter.
        /// A shared lowercase run at the front of the suffix (the matching tail of the object names)
        /// is discarded. Returns null when no such suffix exists.
        /// </summary>
        public static string CommonCapitalizedSuffix(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return null;
            }

            string suffix = names[0] ?? string.Empty;
            for (int i = 1; i < names.Count; i++)
            {
                suffix = CommonSuffix(suffix, names[i] ?? string.Empty);
                if (suffix.Length == 0)
                {
                    return null;
                }
            }

            int start = 0;
            while (start < suffix.Length && !char.IsUpper(suffix[start]))
            {
                start++;
            }

            if (start >= suffix.Length)
            {
                return null;
            }

            return suffix.Substring(start);
        }

        private static string CommonSuffix(string left, string right)
        {
            int i = left.Length - 1;
            int j = right.Length - 1;
            while (i >= 0 && j >= 0 && left[i] == right[j])
            {
                i--;
                j--;
            }

            return left.Substring(i + 1);
        }

        private static string ShortestName(IReadOnlyList<string> names)
        {
            return names
                .OrderBy(n => n.Length)
                .ThenBy(n => n, StringComparer.Ordinal)
                .First();
        }

        /// <summary>
        /// Replaces <paramref name="generatedName"/> when <paramref name="enumNameMap"/> contains that key.
        /// Comparison is ordinal (case-sensitive). The explicit name is returned as given.
        /// </summary>
        public static string ApplyEnumNameMapping(string generatedName, IEnumerable<KeyValuePair<string, string>> enumNameMap)
        {
            if (string.IsNullOrEmpty(generatedName) || enumNameMap == null || !enumNameMap.Any())
            {
                return generatedName;
            }

            foreach (var entry in enumNameMap)
            {
                if (string.Equals(entry.Key, generatedName, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    return entry.Value;
                }
            }

            return generatedName;
        }

        private static string FieldKey(string objectApiName, string fieldName)
        {
            return objectApiName + "\0" + fieldName;
        }

        private sealed class PicklistFieldCandidate
        {
            public string ObjectApiName { get; set; }
            public string ClassName { get; set; }
            public SObjectFieldMetadata Field { get; set; }
            public string Fingerprint { get; set; }
            public List<string> ApiValues { get; set; }
        }

        private sealed class EnumTypeAssignment
        {
            public string EnumTypeName { get; set; }
            public bool IsNested { get; set; }
        }
    }

    public sealed class DescribedObject
    {
        public string ObjectApiName { get; set; }
        public string ClassName { get; set; }
        public SObjectDescribeFull Describe { get; set; }
    }
}
