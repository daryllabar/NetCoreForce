using System;
using System.Collections.Generic;
using System.Text;

namespace NetCoreForce.ModelGenerator
{
    public static class CsIdentifier
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
            "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
            "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long",
            "namespace","new","null","object","operator","out","override","params","private","protected","public",
            "readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string","struct",
            "switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using",
            "virtual","void","volatile","while"
        };

        /// <summary>
        /// Strips a trailing __c custom-field suffix and all underscores.
        /// </summary>
        public static string StripFieldName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return "Field";
            }

            string name = fieldName;
            if (name.EndsWith("__c", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 3);
            }

            name = name.Replace("_", string.Empty);
            return string.IsNullOrEmpty(name) ? "Field" : name;
        }

        public static string SanitizeMemberName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Value";
            }

            var sb = new StringBuilder();
            var word = new StringBuilder();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    word.Append(c);
                }
                else
                {
                    AppendWord(sb, word);
                }
            }

            AppendWord(sb, word);

            if (sb.Length == 0)
            {
                return "Value";
            }

            if (char.IsDigit(sb[0]))
            {
                sb.Insert(0, '_');
            }

            string name = sb.ToString();
            if (Keywords.Contains(name))
            {
                name = "@" + name;
            }

            return name;
        }

        /// <summary>
        /// Appends a word in PascalCase. Words that are entirely upper-case (e.g. "XML") are
        /// normalized so only the first character is upper-case ("Xml"). Clears <paramref name="word"/>.
        /// </summary>
        private static void AppendWord(StringBuilder sb, StringBuilder word)
        {
            if (word.Length == 0)
            {
                return;
            }

            bool hasLetter = false;
            bool hasLower = false;
            for (int i = 0; i < word.Length; i++)
            {
                if (char.IsLetter(word[i]))
                {
                    hasLetter = true;
                    if (char.IsLower(word[i]))
                    {
                        hasLower = true;
                        break;
                    }
                }
            }

            bool allCaps = hasLetter && !hasLower;

            sb.Append(char.ToUpperInvariant(word[0]));
            for (int i = 1; i < word.Length; i++)
            {
                sb.Append(allCaps ? char.ToLowerInvariant(word[i]) : word[i]);
            }

            word.Clear();
        }

        /// <summary>
        /// Preferred name, then {preferred}Enum, then {preferred}Enum1, Enum2, ...
        /// </summary>
        public static string AllocateUnique(string preferred, ISet<string> used)
        {
            if (string.IsNullOrEmpty(preferred))
            {
                preferred = "Value";
            }

            if (!used.Contains(preferred))
            {
                used.Add(preferred);
                return preferred;
            }

            string withEnum = preferred.EndsWith("Enum", StringComparison.Ordinal) ? preferred : preferred + "Enum";
            if (!used.Contains(withEnum))
            {
                used.Add(withEnum);
                return withEnum;
            }

            int i = 1;
            while (true)
            {
                string candidate = withEnum + i.ToString();
                if (!used.Contains(candidate))
                {
                    used.Add(candidate);
                    return candidate;
                }

                i++;
            }
        }

        public static string AllocateUniqueMember(string preferred, ISet<string> used)
        {
            if (string.IsNullOrEmpty(preferred))
            {
                preferred = "Value";
            }

            string candidate = preferred;
            int i = 1;
            while (used.Contains(candidate) || string.Equals(candidate, "Undefined", StringComparison.Ordinal))
            {
                candidate = preferred + i.ToString();
                i++;
            }

            used.Add(candidate);
            return candidate;
        }

        public static string EscapeAttributeString(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
