using System.Collections.Generic;
using System.Text;

namespace NetCoreForce.ModelGenerator
{
    public static class PicklistEnumCodeGen
    {
        public static void AppendEnumType(StringBuilder gen, string typeName, IList<PicklistEnumMember> members, string indent)
        {
            gen.Append(indent);
            gen.Append("public enum ");
            gen.AppendLine(typeName);
            gen.Append(indent);
            gen.AppendLine("{");

            string memberIndent = indent + "\t";
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                gen.Append(memberIndent);
                gen.Append("[System.Text.Json.Serialization.JsonStringEnumMemberName(\"");
                gen.Append(CsIdentifier.EscapeAttributeString(member.ApiValue));
                gen.AppendLine("\")]");
                gen.Append(memberIndent);
                gen.Append(member.Identifier);
                gen.AppendLine(",");
                gen.AppendLine();
            }

            gen.Append(memberIndent);
            gen.AppendLine("Undefined = -1");
            gen.Append(indent);
            gen.AppendLine("}");
        }

        public static void AppendEnumProperty(StringBuilder gen, PicklistEnumProperty property, string indent)
        {
            gen.Append(indent);
            gen.AppendLine("[JsonIgnore]");

            if (property.IsMultipicklist)
            {
                gen.Append(indent);
                gen.Append("public List<");
                gen.Append(property.EnumTypeName);
                gen.Append(">? ");
                gen.Append(property.PropertyName);
                gen.AppendLine();
                gen.Append(indent);
                gen.AppendLine("{");
                gen.Append(indent);
                gen.Append("\tget => ");
                gen.Append(property.FieldName);
                gen.AppendLine($".ParseEnumList<{property.EnumTypeName}>();");
                gen.Append(indent);
                gen.Append("\tset => ");
                gen.Append(property.FieldName);
                gen.AppendLine(" = value.ToSerializedPicklist();");
                gen.Append(indent);
                gen.AppendLine("}");
            }
            else
            {
                gen.Append(indent);
                gen.Append("public ");
                gen.Append(property.EnumTypeName);
                gen.Append("? ");
                gen.Append(property.PropertyName);
                gen.AppendLine();
                gen.Append(indent);
                gen.AppendLine("{");
                gen.Append(indent);
                gen.Append("\tget => ");
                gen.Append(property.FieldName);
                gen.AppendLine($".ParseEnum<{property.EnumTypeName}>();");
                gen.Append(indent);
                gen.Append("\tset => ");
                gen.Append(property.FieldName);
                gen.Append(" = value == null ? null : EnumJsonMemberNames.GetSerializedName(value.Value);");
                gen.AppendLine();
                gen.Append(indent);
                gen.AppendLine("}");
            }
        }

        public static string SharedEnumFile(SharedPicklistEnum sharedEnum, string classNamespace)
        {
            var gen = new StringBuilder();
            gen.AppendLine();
            if (!string.IsNullOrEmpty(classNamespace))
            {
                gen.Append("namespace ");
                gen.AppendLine(classNamespace);
                gen.AppendLine("{");
                AppendEnumType(gen, sharedEnum.TypeName, sharedEnum.Members, "\t");
                gen.AppendLine("}");
            }
            else
            {
                AppendEnumType(gen, sharedEnum.TypeName, sharedEnum.Members, string.Empty);
            }

            return gen.ToString();
        }
    }
}
