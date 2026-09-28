using System;
using System.IO;
using System.Text;

namespace NetCoreForce.ModelGenerator
{
    public static class SerializationHelperGenerator
    {
        public static void WriteHelpers(GenConfig config)
        {
            Directory.CreateDirectory(config.OutputDirectory);

            Console.WriteLine("Writing: " + Path.Combine(config.OutputDirectory, "EnumJsonMemberNames.cs"));
            File.WriteAllText(Path.Combine(config.OutputDirectory, "EnumJsonMemberNames.cs"), EnumJsonMemberNamesSource(config.ClassNamespace));
            Console.WriteLine("Writing: " + Path.Combine(config.OutputDirectory, "StringExtensions.cs"));
            File.WriteAllText(Path.Combine(config.OutputDirectory, "StringExtensions.cs"), StringExtensionsSource(config.ClassNamespace));
        }

        public static string EnumJsonMemberNamesSource(string ns)
        {
            var gen = new StringBuilder();
            gen.AppendLine("using System.Collections.Concurrent;");
            gen.AppendLine("using System.Reflection;");
            gen.AppendLine("using System.Text.Json.Serialization;");
            gen.AppendLine();
            gen.AppendLine("namespace " + ns);
            gen.AppendLine("{");
            gen.AppendLine("    /// <summary>");
            gen.AppendLine("    /// Resolves enum JSON names from <see cref=\"JsonStringEnumMemberNameAttribute\"/> when present, otherwise the CLR member name.");
            gen.AppendLine("    /// </summary>");
            gen.AppendLine("    public static class EnumJsonMemberNames");
            gen.AppendLine("    {");
            gen.AppendLine("        private static readonly ConcurrentDictionary<System.Type, IReadOnlyList<string>> SerializedNames = new ConcurrentDictionary<System.Type, IReadOnlyList<string>>();");
            gen.AppendLine();
            gen.AppendLine("        public static string GetSerializedName(Enum value)");
            gen.AppendLine("        {");
            gen.AppendLine("            var enumType = value.GetType();");
            gen.AppendLine("            var memberName = Enum.GetName(enumType, value);");
            gen.AppendLine("            if (memberName == null)");
            gen.AppendLine("            {");
            gen.AppendLine("                return value.ToString();");
            gen.AppendLine("            }");
            gen.AppendLine();
            gen.AppendLine("            var field = enumType.GetField(memberName);");
            gen.AppendLine("            return field == null ? memberName : GetSerializedName(field);");
            gen.AppendLine("        }");
            gen.AppendLine();
            gen.AppendLine("        public static IReadOnlyList<string> GetSerializedNames(System.Type enumType)");
            gen.AppendLine("        {");
            gen.AppendLine("            return SerializedNames.GetOrAdd(enumType, type =>");
            gen.AppendLine("                type.GetFields(BindingFlags.Public | BindingFlags.Static)");
            gen.AppendLine("                    .Select(GetSerializedName)");
            gen.AppendLine("                    .ToArray());");
            gen.AppendLine("        }");
            gen.AppendLine();
            gen.AppendLine("        public static bool TryParse<TEnum>(string text, out TEnum value)");
            gen.AppendLine("            where TEnum : struct, Enum");
            gen.AppendLine("        {");
            gen.AppendLine("            if (!string.IsNullOrEmpty(text) && EnumNameCache<TEnum>.ByName.TryGetValue(text, out value))");
            gen.AppendLine("            {");
            gen.AppendLine("                return Enum.IsDefined(typeof(TEnum), value);");
            gen.AppendLine("            }");
            gen.AppendLine();
            gen.AppendLine("            value = default(TEnum);");
            gen.AppendLine("            return false;");
            gen.AppendLine("        }");
            gen.AppendLine();
            gen.AppendLine("        private static string GetSerializedName(FieldInfo field)");
            gen.AppendLine("        {");
            gen.AppendLine("            var attribute = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();");
            gen.AppendLine("            return attribute != null ? attribute.Name : field.Name;");
            gen.AppendLine("        }");
            gen.AppendLine();
            gen.AppendLine("        private static class EnumNameCache<TEnum> where TEnum : struct, Enum");
            gen.AppendLine("        {");
            gen.AppendLine("            public static readonly Dictionary<string, TEnum> ByName = Build();");
            gen.AppendLine();
            gen.AppendLine("            private static Dictionary<string, TEnum> Build()");
            gen.AppendLine("            {");
            gen.AppendLine("                var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);");
            gen.AppendLine("                foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))");
            gen.AppendLine("                {");
            gen.AppendLine("                    var enumValue = (TEnum)(field.GetValue(null) ?? default(TEnum));");
            gen.AppendLine("                    map[field.Name] = enumValue;");
            gen.AppendLine("                    var wireName = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;");
            gen.AppendLine("                    if (!string.IsNullOrEmpty(wireName))");
            gen.AppendLine("                    {");
            gen.AppendLine("                        map[wireName] = enumValue;");
            gen.AppendLine("                    }");
            gen.AppendLine("                }");
            gen.AppendLine();
            gen.AppendLine("                return map;");
            gen.AppendLine("            }");
            gen.AppendLine("        }");
            gen.AppendLine("    }");
            gen.AppendLine("}");
            return gen.ToString();
        }

        public static string StringExtensionsSource(string ns)
        {
            var gen = new StringBuilder();
            gen.Append("namespace ");
            gen.AppendLine(ns);
            gen.AppendLine("{");
            gen.AppendLine("    public static class StringExtensions");
            gen.AppendLine("    {");
            gen.AppendLine("        /// <summary>");
            gen.AppendLine("        /// Parses a string value into an enum of type TEnum, using the -1 value if parsing fails or null if the input is null/whitespace.");
            gen.AppendLine("        /// </summary>");
            gen.AppendLine("        public static TEnum? ParseEnum<TEnum>(this string? value)");
            gen.AppendLine("            where TEnum : struct, Enum");
            gen.AppendLine("        {");
            gen.AppendLine("            if (string.IsNullOrWhiteSpace(value))");
            gen.AppendLine("            {");
            gen.AppendLine("                return null;");
            gen.AppendLine("            }");
            gen.AppendLine();
            gen.AppendLine("            return EnumJsonMemberNames.TryParse(value.Trim(), out TEnum parsed)");
            gen.AppendLine("                ? parsed");
            gen.AppendLine("                : (TEnum)(object)(-1);");
            gen.AppendLine("        }");
            gen.AppendLine();
            gen.AppendLine("        private static readonly char[] SemicolonSplitChars = [';'];");
            gen.AppendLine("        /// <summary>");
            gen.AppendLine("        /// Parses a Salesforce semicolon-separated multipicklist into enum values.");
            gen.AppendLine("        /// Unknown tokens map to <paramref name=\"undefined\"/>.");
            gen.AppendLine("        /// </summary>");
            gen.AppendLine("        public static List<TEnum> ParseEnumList<TEnum>(this string? value)");
            gen.AppendLine("            where TEnum : struct, Enum");
            gen.AppendLine("        {");
            gen.AppendLine("            if (string.IsNullOrWhiteSpace(value))");
            gen.AppendLine("            {");
            gen.AppendLine("                return null;");
            gen.AppendLine("            }");
            gen.AppendLine();
            gen.AppendLine("            return value");
            gen.AppendLine("                .Split(SemicolonSplitChars, StringSplitOptions.RemoveEmptyEntries)");
            gen.AppendLine("                .Select(v => v.Trim())");
            gen.AppendLine("                .Where(v => v.Length > 0)");
            gen.AppendLine("                .Select(v => v.ParseEnum<TEnum>() ?? (TEnum)(object)(-1))");
            gen.AppendLine("                .ToList();");
            gen.AppendLine("        }");
            gen.AppendLine();
            gen.AppendLine("        /// <summary>");
            gen.AppendLine("        /// Joins enum values into a Salesforce multipicklist string. Undefined values are omitted.");
            gen.AppendLine("        /// </summary>");
            gen.AppendLine("        public static string? ToSerializedPicklist<TEnum>(this IEnumerable<TEnum>? values)");
            gen.AppendLine("            where TEnum : struct, Enum");
            gen.AppendLine("        {");
            gen.AppendLine("            if (values == null)");
            gen.AppendLine("            {");
            gen.AppendLine("                return null;");
            gen.AppendLine("            }");
            gen.AppendLine();
            gen.AppendLine("            var names = values");
            gen.AppendLine("                .Where(v => !EqualityComparer<TEnum>.Default.Equals(v, (TEnum)(object)(-1)))");
            gen.AppendLine("                .Select(v => EnumJsonMemberNames.GetSerializedName(v))");
            gen.AppendLine("                .ToList();");
            gen.AppendLine();
            gen.AppendLine("            return names.Count == 0 ? null : string.Join(\";\", names);");
            gen.AppendLine("        }");
            gen.AppendLine("    }");
            gen.AppendLine("}");
            return gen.ToString();
        }
    }
}
