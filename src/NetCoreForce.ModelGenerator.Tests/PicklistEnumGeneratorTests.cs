using System.Collections.Generic;
using System.Linq;
using NetCoreForce.Client.Models;
using Xunit;

namespace NetCoreForce.ModelGenerator.Tests
{
    public class CsIdentifierTests
    {
        [Fact]
        public void StripFieldName_RemovesCustomSuffixAndUnderscores()
        {
            Assert.Equal("RewardStatus", CsIdentifier.StripFieldName("Reward_Status__c"));
        }

        [Fact]
        public void AllocateUnique_PostfixesEnumThenNumbers()
        {
            var used = new HashSet<string> { "Status", "StatusEnum" };

            Assert.Equal("StatusEnum1", CsIdentifier.AllocateUnique("Status", used));
            Assert.Equal("StatusEnum2", CsIdentifier.AllocateUnique("Status", used));
        }

        [Fact]
        public void SanitizeMemberName_PascalCasesAndSkipsPunctuation()
        {
            Assert.Equal("ClosedWon", CsIdentifier.SanitizeMemberName("Closed - Won"));
            Assert.Equal("_1Hot", CsIdentifier.SanitizeMemberName("1 - Hot"));
        }

        [Theory]
        [InlineData("XML File", "XmlFile")]
        [InlineData("XML", "Xml")]
        [InlineData("USA Today", "UsaToday")]
        [InlineData("HTML5 Page", "Html5Page")]
        [InlineData("iPhone", "IPhone")]
        [InlineData("ABCdef", "ABCdef")]
        [InlineData("Closed - Won", "ClosedWon")]
        [InlineData("A B", "AB")]
        public void SanitizeMemberName_NormalizesAllCapsWords(string input, string expected)
        {
            Assert.Equal(expected, CsIdentifier.SanitizeMemberName(input));
        }

        [Fact]
        public void AllocateUniqueMember_AvoidsUndefined()
        {
            var used = new HashSet<string>();
            Assert.Equal("Undefined1", CsIdentifier.AllocateUniqueMember("Undefined", used));
        }
    }

    public class PicklistEnumPlannerTests
    {
        [Fact]
        public void Build_ReturnsEmpty_WhenFlagOff()
        {
            var config = TestData.Config(generateEnums: false);
            var account = TestData.Describe("Account", TestData.Field("Type", "picklist", "Prospect", "Other"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Empty(plan.SharedEnums);
            Assert.Empty(plan.PropertiesByObject);
            Assert.Empty(plan.NestedEnumsByObject);
        }

        [Fact]
        public void Build_NestsUniqueValueSets()
        {
            var config = TestData.Config(generateEnums: true);
            var account = TestData.Describe("Account", TestData.Field("Type", "picklist", "Prospect", "Other"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Empty(plan.SharedEnums);
            Assert.Single(plan.NestedEnumsByObject["Account"]);
            Assert.Equal("Account_Type", plan.NestedEnumsByObject["Account"][0].TypeName);
            Assert.Equal("TypeEnum", plan.PropertiesByObject["Account"][0].PropertyName);
            Assert.Equal("Account_Type", plan.PropertiesByObject["Account"][0].EnumTypeName);
        }

        [Fact]
        public void Build_SharesIdenticalValueSets_UsingCommonCapitalizedSuffix()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var lead = TestData.Describe("Lead", TestData.Field("Industry", "picklist", "Agriculture", "Banking"));
            var account = TestData.Describe("Account", TestData.Field("Industry", "picklist", "Banking", "Agriculture"));
            var contact = TestData.Describe("Contact", TestData.Field("Industry", "picklist", "Agriculture", "Banking"));

            var plan = PicklistEnumPlanner.Build(new[] { lead, account, contact }, config);

            Assert.Single(plan.SharedEnums);
            Assert.Equal("Industry", plan.SharedEnums[0].TypeName);
            Assert.Empty(plan.NestedEnumsByObject);
            Assert.Equal("Industry", plan.PropertiesByObject["Lead"][0].EnumTypeName);
            Assert.Equal("Industry", plan.PropertiesByObject["Account"][0].EnumTypeName);
            Assert.Equal("Industry", plan.PropertiesByObject["Contact"][0].EnumTypeName);
        }

        [Fact]
        public void Build_NormalizesAllCapsWords_InSharedEnumMembersAndTypeName()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var lead = TestData.Describe("Lead", TestData.Field("XML__c", "picklist", "XML File", "USA"));
            var account = TestData.Describe("Account", TestData.Field("XML__c", "picklist", "USA", "XML File"));

            var plan = PicklistEnumPlanner.Build(new[] { lead, account }, config);

            Assert.Single(plan.SharedEnums);
            Assert.Equal("Xml", plan.SharedEnums[0].TypeName);

            var members = plan.SharedEnums[0].Members;
            Assert.Equal("Usa", members[0].Identifier);
            Assert.Equal("USA", members[0].ApiValue);
            Assert.Equal("XmlFile", members[1].Identifier);
            Assert.Equal("XML File", members[1].ApiValue);

            Assert.Equal("Xml", plan.PropertiesByObject["Account"][0].EnumTypeName);
            Assert.Equal("Xml", plan.PropertiesByObject["Account"][0].PropertyName);
        }

        [Fact]
        public void Build_NormalizesAllCapsWords_InNestedEnumTypeName()
        {
            var config = TestData.Config(generateEnums: true);
            var account = TestData.Describe("Account", TestData.Field("XML__c", "picklist", "USA"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Empty(plan.SharedEnums);
            Assert.Equal("Account_Xml", plan.NestedEnumsByObject["Account"][0].TypeName);
            Assert.Equal("Usa", plan.NestedEnumsByObject["Account"][0].Members[0].Identifier);
            Assert.Equal("Xml", plan.PropertiesByObject["Account"][0].PropertyName);
        }

        [Fact]
        public void Build_RenamesGeneratedEnum_WhenMapMatches()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            config.EnumNameMap = new Dictionary<string, string>
            {
                { "LeadSource", "Source" }
            };
            var contact = TestData.Describe("Contact", TestData.Field("LeadSource", "picklist", "Web", "Phone"));
            var opportunity = TestData.Describe("Opportunity", TestData.Field("LeadSource", "picklist", "Phone", "Web"));
            opportunity.ClassName = "Source";

            var plan = PicklistEnumPlanner.Build(new[] { contact, opportunity }, config);

            Assert.Single(plan.SharedEnums);
            Assert.Equal("Source", plan.SharedEnums[0].TypeName);
            Assert.Equal("Source", plan.PropertiesByObject["Contact"][0].EnumTypeName);
            Assert.Equal("Source", plan.PropertiesByObject["Opportunity"][0].EnumTypeName);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Build_KeepsGeneratedName_WhenEnumNameMapOmitted(bool emptyMap)
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            if (emptyMap)
            {
                config.EnumNameMap = new Dictionary<string, string>();
            }

            var contact = TestData.Describe("Contact", TestData.Field("LeadSource", "picklist", "Web", "Phone"));
            var opportunity = TestData.Describe("Opportunity", TestData.Field("LeadSource", "picklist", "Phone", "Web"));

            var plan = PicklistEnumPlanner.Build(new[] { contact, opportunity }, config);

            Assert.Equal("LeadSource", plan.SharedEnums[0].TypeName);
        }

        [Fact]
        public void Build_EnumNameMap_IsCaseSensitive()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            config.EnumNameMap = new Dictionary<string, string>
            {
                { "leadsource", "Source" }
            };
            var contact = TestData.Describe("Contact", TestData.Field("LeadSource", "picklist", "Web", "Phone"));
            var opportunity = TestData.Describe("Opportunity", TestData.Field("LeadSource", "picklist", "Phone", "Web"));

            var plan = PicklistEnumPlanner.Build(new[] { contact, opportunity }, config);

            Assert.Equal("LeadSource", plan.SharedEnums[0].TypeName);
        }

        [Fact]
        public void Build_RenamesNestedEnum_WhenMapMatches()
        {
            var config = TestData.Config(generateEnums: true);
            config.EnumNameMap = new Dictionary<string, string>
            {
                { "Account_Type", "AccountType" }
            };
            var account = TestData.Describe("Account", TestData.Field("Type", "picklist", "Prospect", "Other"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Equal("AccountType", plan.NestedEnumsByObject["Account"][0].TypeName);
            Assert.Equal("AccountType", plan.PropertiesByObject["Account"][0].EnumTypeName);
        }

        [Theory]
        [InlineData("ContactLeadSource=LeadSource", true, "ContactLeadSource", "LeadSource")]
        [InlineData(" contactleadsource = Other ", true, "contactleadsource", "Other")]
        [InlineData("ContactLeadSource", false, null, null)]
        [InlineData("=LeadSource", false, null, null)]
        [InlineData("ContactLeadSource=", false, null, null)]
        public void TryParseEnumNameMapping_SplitsOnFirstEquals(string entry, bool expected, string generated, string explicitName)
        {
            bool parsed = Program.TryParseEnumNameMapping(entry, out string actualGenerated, out string actualExplicit);

            Assert.Equal(expected, parsed);
            Assert.Equal(generated, actualGenerated);
            Assert.Equal(explicitName, actualExplicit);
        }

        [Fact]
        public void Build_NamesYesNoPicklist_YesNo()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var account = TestData.Describe("Account", TestData.Field("Active__c", "picklist", "No", "Yes"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Equal("YesNo", plan.NestedEnumsByObject["Account"][0].TypeName);
            Assert.Equal("YesNo", plan.PropertiesByObject["Account"][0].EnumTypeName);
        }

        [Fact]
        public void Build_SharesYesNoPicklist_AsYesNo()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var account = TestData.Describe("Account", TestData.Field("Active__c", "picklist", "Yes", "No"));
            var contact = TestData.Describe("Contact", TestData.Field("Active__c", "picklist", "No", "Yes"));

            var plan = PicklistEnumPlanner.Build(new[] { account, contact }, config);

            Assert.Single(plan.SharedEnums);
            Assert.Equal("YesNo", plan.SharedEnums[0].TypeName);
            Assert.Equal("YesNo", plan.PropertiesByObject["Account"][0].EnumTypeName);
            Assert.Equal("YesNo", plan.PropertiesByObject["Contact"][0].EnumTypeName);
        }

        [Fact]
        public void Build_RemapsYesNo_WhenConfigured()
        {
            var config = TestData.Config(generateEnums: true);
            config.EnumNameMap = new Dictionary<string, string>
            {
                { "YesNo", "BooleanChoice" }
            };
            var account = TestData.Describe("Account", TestData.Field("Active__c", "picklist", "Yes", "No"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Equal("BooleanChoice", plan.NestedEnumsByObject["Account"][0].TypeName);
            Assert.Equal("BooleanChoice", plan.PropertiesByObject["Account"][0].EnumTypeName);
        }

        [Fact]
        public void Build_DropsTablePrefix_WhenPicklistIsReusedOnOneObject()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var account = TestData.Describe(
                "Account",
                TestData.Field("AddressType", "picklist", "Billing", "Shipping"),
                TestData.Field("Address_Type__c", "picklist", "Shipping", "Billing"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Single(plan.SharedEnums);
            Assert.Equal("AddressType", plan.SharedEnums[0].TypeName);
            Assert.Equal("AddressType", plan.PropertiesByObject["Account"][0].EnumTypeName);
            Assert.Equal("AddressType", plan.PropertiesByObject["Account"][1].EnumTypeName);
        }

        [Fact]
        public void Build_KeepsTablePrefix_WhenShorterNameAlreadyExists()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var contact = TestData.Describe("Contact", TestData.Field("AddressType", "picklist", "Home", "Work"));
            var lead = TestData.Describe("Lead", TestData.Field("AddressType", "picklist", "Work", "Home"));
            var account = TestData.Describe(
                "Account",
                TestData.Field("AddressType", "picklist", "Billing", "Shipping"),
                TestData.Field("Address_Type__c", "picklist", "Shipping", "Billing"));

            var plan = PicklistEnumPlanner.Build(new[] { contact, lead, account }, config);

            Assert.Equal(2, plan.SharedEnums.Count);
            Assert.Contains(plan.SharedEnums, e => e.TypeName == "AddressType");
            Assert.Contains(plan.SharedEnums, e => e.TypeName == "AccountAddressType");
            Assert.Equal("AccountAddressType", plan.PropertiesByObject["Account"][0].EnumTypeName);
        }

        [Fact]
        public void Build_DropsTablePrefix_KeepsClassPrefix()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var account = TestData.Describe(
                "Account",
                TestData.Field("AddressType", "picklist", "Billing", "Shipping"),
                TestData.Field("Address_Type__c", "picklist", "Shipping", "Billing"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Equal("SfAddressType", plan.SharedEnums[0].TypeName);
        }

        [Fact]
        public void Build_DoesNotNameYesNo_WhenOtherValuesArePresent()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var account = TestData.Describe("Account", TestData.Field("Active__c", "picklist", "Yes", "No", "Maybe"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Equal("Account_Active", plan.NestedEnumsByObject["Account"][0].TypeName);
        }

        [Fact]
        public void Build_DoesNotNameYesNo_WhenCasingDiffers()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var account = TestData.Describe("Account", TestData.Field("Active__c", "picklist", "yes", "no"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Equal("Account_Active", plan.NestedEnumsByObject["Account"][0].TypeName);
        }

        [Fact]
        public void Build_SharesIdenticalValueSets_UsesStatusNotShortestObject()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var account = TestData.Describe("Account", TestData.Field("Status", "picklist", "Open", "Closed"));
            var contact = TestData.Describe("Contact", TestData.Field("Status", "picklist", "Closed", "Open"));
            var lead = TestData.Describe("Lead", TestData.Field("Status", "picklist", "Open", "Closed"));

            var plan = PicklistEnumPlanner.Build(new[] { account, contact, lead }, config);

            Assert.Equal("Status", plan.SharedEnums[0].TypeName);
        }

        [Fact]
        public void Build_SharesIdenticalValueSets_KeepsUserStatus()
        {
            var config = TestData.Config(generateEnums: true, prefix: "");
            var account = TestData.Describe("Account", TestData.Field("UserStatus", "picklist", "Active", "Idle"));
            var contact = TestData.Describe("Contact", TestData.Field("UserStatus", "picklist", "Idle", "Active"));

            var plan = PicklistEnumPlanner.Build(new[] { account, contact }, config);

            Assert.Equal("UserStatus", plan.SharedEnums[0].TypeName);
        }

        [Fact]
        public void Build_SharedEnum_FallsBackToShortestName_WhenSuffixHasNoCapital()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var account = TestData.Describe("Account", TestData.Field("123", "picklist", "A", "B"));
            var contact = TestData.Describe("Contact", TestData.Field("123", "picklist", "B", "A"));

            var plan = PicklistEnumPlanner.Build(new[] { contact, account }, config);

            Assert.Equal("SfAccount_123", plan.SharedEnums[0].TypeName);
        }

        [Theory]
        [InlineData("Status", "AccountStatus", "ContactStatus", "LeadStatus")]
        [InlineData("UserStatus", "AccountUserStatus", "ContactUserStatus")]
        [InlineData("Status", "SfAccountStatus", "SfContactStatus")]
        [InlineData("StatusEnum", "AccountStatusEnum", "ContactStatusEnum")]
        public void CommonCapitalizedSuffix_TrimsToFirstCapital(string expected, params string[] names)
        {
            Assert.Equal(expected, PicklistEnumPlanner.CommonCapitalizedSuffix(names));
        }

        [Fact]
        public void CommonCapitalizedSuffix_ReturnsNull_WhenNoCapital()
        {
            Assert.Null(PicklistEnumPlanner.CommonCapitalizedSuffix(new[] { "abcd", "xyzd" }));
        }

        [Fact]
        public void Build_DoesNotShareDifferentValueSets()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var account = TestData.Describe("Account", TestData.Field("Type", "picklist", "Prospect"));
            var lead = TestData.Describe("Lead", TestData.Field("Type", "picklist", "Open"));

            var plan = PicklistEnumPlanner.Build(new[] { account, lead }, config);

            Assert.Empty(plan.SharedEnums);
            Assert.Equal("Account_Type", plan.NestedEnumsByObject["Account"][0].TypeName);
            Assert.Equal("Lead_Type", plan.NestedEnumsByObject["Lead"][0].TypeName);
        }

        [Fact]
        public void Build_SkipsEmptyPicklists()
        {
            var config = TestData.Config(generateEnums: true);
            var account = TestData.Describe("Account", TestData.Field("Type", "picklist"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.Empty(plan.PropertiesByObject);
        }

        [Fact]
        public void Build_CustomFieldPropertyName_StripsSuffixAndUnderscores()
        {
            var config = TestData.Config(generateEnums: true, includeCustom: true);
            var field = TestData.Field("Reward_Status__c", "picklist", "Open", "Closed");
            field.Custom = true;
            var obj = TestData.Describe("Invoice__c", field);
            obj.ClassName = "Invoice__c";

            var plan = PicklistEnumPlanner.Build(new[] { obj }, config);

            Assert.Equal("RewardStatus", plan.PropertiesByObject["Invoice__c"][0].PropertyName);
            Assert.Equal("Invoice__c_RewardStatus", plan.NestedEnumsByObject["Invoice__c"][0].TypeName);
        }

        [Fact]
        public void Build_MarksMultipicklistProperties()
        {
            var config = TestData.Config(generateEnums: true);
            var account = TestData.Describe("Account", TestData.Field("Skills", "multipicklist", "A", "B"));

            var plan = PicklistEnumPlanner.Build(new[] { account }, config);

            Assert.True(plan.PropertiesByObject["Account"][0].IsMultipicklist);
        }

        [Fact]
        public void Fingerprint_IgnoresOrderAndDuplicates()
        {
            var a = new[]
            {
                new PickListValue { Value = "B" },
                new PickListValue { Value = "A" },
                new PickListValue { Value = "A" }
            };
            var b = new[]
            {
                new PickListValue { Value = "A" },
                new PickListValue { Value = "B" }
            };

            Assert.Equal(PicklistEnumPlanner.Fingerprint(a), PicklistEnumPlanner.Fingerprint(b));
        }
    }

    public class PicklistEnumCodeGenTests
    {
        [Fact]
        public void GenClass_FlagOff_DoesNotEmitEnumMembersOrUsings()
        {
            var config = TestData.Config(generateEnums: false);
            var describe = TestData.Describe("Account", TestData.Field("Type", "picklist", "Prospect")).Describe;
            describe.Label = "Account";

            string source = Program.GenClass(describe, "SfAccount", config, null);

            Assert.DoesNotContain("ParseEnum", source);
            Assert.DoesNotContain("public enum", source);
            Assert.DoesNotContain(".Serialization", source);
            Assert.Contains("public string? Type { get; set; }", source);
        }

        [Fact]
        public void GenClass_EmitsNestedEnumAndPassThrough()
        {
            var config = TestData.Config(generateEnums: true, prefix: "Sf");
            var described = TestData.Describe("Account", TestData.Field("Type", "picklist", "Closed - Won"));
            described.Describe.Label = "Account";
            var plan = PicklistEnumPlanner.Build(new[] { described }, config);

            string source = Program.GenClass(described.Describe, "SfAccount", config, plan);

            Assert.Contains("[JsonIgnore]", source);
            Assert.Contains("public Account_Type? TypeEnum", source);
            Assert.Contains("get => Type.ParseEnum<Account_Type>();", source);
            Assert.Contains("public enum Account_Type", source);
            Assert.Contains("[System.Text.Json.Serialization.JsonStringEnumMemberName(\"Closed - Won\")]", source);
            Assert.Contains("ClosedWon,", source);
            Assert.Contains("Undefined = -1", source);
            Assert.DoesNotContain("\t\tpublic enum Account_Type", source);
        }

        [Fact]
        public void GenClass_EmitsMultipicklistListProperty()
        {
            var config = TestData.Config(generateEnums: true);
            var described = TestData.Describe("Account", TestData.Field("Skills", "multipicklist", "A"));
            described.Describe.Label = "Account";
            var plan = PicklistEnumPlanner.Build(new[] { described }, config);

            string source = Program.GenClass(described.Describe, "SfAccount", config, plan);

            Assert.Contains("using System.Collections.Generic;", source);
            Assert.Contains("public List<Account_Skills>? SkillsEnum", source);
            Assert.Contains("ParseEnumList<Account_Skills>()", source);
            Assert.Contains("ToSerializedPicklist()", source);
        }

        [Fact]
        public void SharedEnumFile_UsesNamespaceAndUndefined()
        {
            var shared = new SharedPicklistEnum
            {
                TypeName = "SfAccountIndustry",
                Members = new List<PicklistEnumMember>
                {
                    new PicklistEnumMember { Identifier = "Agriculture", ApiValue = "Agriculture" }
                }
            };

            string source = PicklistEnumCodeGen.SharedEnumFile(shared, "NetCoreForce.Models");

            Assert.Contains("namespace NetCoreForce.Models", source);
            Assert.Contains("public enum SfAccountIndustry", source);
            Assert.Contains("Undefined = -1", source);
        }
    }

    internal static class TestData
    {
        public static GenConfig Config(bool generateEnums, bool includeCustom = false, string prefix = "Sf")
        {
            return new GenConfig
            {
                GenerateEnumProperties = generateEnums,
                IncludeCustom = includeCustom,
                ClassPrefix = prefix,
                ClassNamespace = "NetCoreForce.Models",
                AuthInfo = new AuthInfo { ApiVersion = "v64.0" }
            };
        }

        public static DescribedObject Describe(string objectName, params SObjectFieldMetadata[] fields)
        {
            return new DescribedObject
            {
                ObjectApiName = objectName,
                ClassName = "Sf" + objectName.Replace("__c", string.Empty),
                Describe = new SObjectDescribeFull
                {
                    Name = objectName,
                    Label = objectName,
                    Fields = fields.ToList()
                }
            };
        }

        public static SObjectFieldMetadata Field(string name, string type, params string[] values)
        {
            return new SObjectFieldMetadata
            {
                Name = name,
                Type = type,
                Label = name,
                Nillable = true,
                Creatable = true,
                Updateable = true,
                PicklistValues = values.Select(v => new PickListValue { Value = v, Active = true }).ToList()
            };
        }
    }
}
