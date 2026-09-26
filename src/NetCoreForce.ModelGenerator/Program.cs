using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using NetCoreForce.Client;
using NetCoreForce.Client.Models;
using Newtonsoft.Json;

namespace NetCoreForce.ModelGenerator
{
    public class Program
    {
        const string defaultConfigFilename = "modelgenerator_config.json";

        static void Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "modelgenerator";
            app.HelpOption("-?|-h|--help");

            app.OnExecute(() =>
            {
                app.ShowHint();
                return 0;
            });

            app.VersionOption("-v|--version", () =>
            {
                return string.Format("Version {0}", Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
            });

            app.Command("generate", (command) =>
            {
                command.Description = "Generate SObject models.";
                command.HelpOption("-?|-h|--help");

                command.ExtendedHelpText = Environment.NewLine +
                "You can supply the API credentials either in the config file, the command parameters, or wait to be prompted for that information." + Environment.NewLine +
                "If you choose to save the config file, be careful with it as it may contain your API credentials.";

                //Authentication
                var clientIdOption = command.Option("--client-id",
                    "API Client ID, a.k.a. Consumer Key",
                    CommandOptionType.SingleValue);

                var clientSecretOption = command.Option("--client-secret",
                    "API Client Secret, a.k.a. Consumer Secret",
                    CommandOptionType.SingleValue);

                //var usernameOption = command.Option("--username",
                //    "API Username",
                //    CommandOptionType.SingleValue);

                //var passwordOption = command.Option("--password",
                //    "API Password",
                //    CommandOptionType.SingleValue);

                var configFileOption = command.Option("--config-file",
                    "Config file path",
                    CommandOptionType.SingleValue);

                var saveConfigOption = command.Option("--save-config",
                    "Save options to config file, uses filename from --config-file option",
                    CommandOptionType.NoValue);

                //generation options
                var includeOption = command.Option("-o|--objects <objects>",
                    "Object models to generate, if omitted all objects will be generated",
                    CommandOptionType.MultipleValue);

                var outputDirectory = command.Option("-d|--output-directory <directory>",
                    "Destination directory for generated file(s)",
                    CommandOptionType.SingleValue);

                var suffixOption = command.Option("-s|--suffix <suffix>",
                    "Suffix to append to object names, e.g. 'Sf' for 'AccountSf'",
                    CommandOptionType.SingleValue);

                var prefixOption = command.Option("-p|--prefix <prefix>",
                    "Prefix to for object names, e.g. 'Sf' for 'SfAccount'",
                    CommandOptionType.SingleValue);

                var namespaceName = command.Option("-n|--namespace <namespace>",
                    "Namespace to use for generated classes",
                    CommandOptionType.SingleValue);

                var customOption = command.Option("-c|--include-custom",
                    "Include custom objects and fields",
                    CommandOptionType.NoValue);

                var includeReferences = command.Option("-r|--include-references",
                    "Include referenced objects as properties",
                    CommandOptionType.NoValue);

                var readOnlyPropertiesOption = command.Option("--readonly-properties",
                    "Generate properties as read-only (get; set; protected;) based on API field settings",
                    CommandOptionType.NoValue);

                var generateEnumPropertiesOption = command.Option("--generate-enum-properties",
                    "Emit JsonIgnore enum pass-through properties for picklist, combobox, and multipicklist fields",
                    CommandOptionType.NoValue);

                var enumNameMapOption = command.Option("--enum-name-map <mapping>",
                    "Optional. Rename a generated enum type. Each value is GeneratedName=ExplicitName. Repeat for multiple mappings. Match is case-sensitive. Omit to keep generated names.",
                    CommandOptionType.MultipleValue);

                command.OnExecute(() =>
                {
                    //load config file, if available
                    GenConfig config = LoadConfig(configFileOption.Value());

                    if (config == null)
                    {
                        config = new GenConfig();
                    }

                    //only override config file option if option is manually specified
                    if (clientIdOption.HasValue())
                    {
                        config.AuthInfo.ClientId = clientIdOption.Value();
                    }

                    if (clientSecretOption.HasValue())
                    {
                        config.AuthInfo.ClientSecret = clientSecretOption.Value();
                    }

                    //if (usernameOption.HasValue())
                    //{
                    //    config.AuthInfo.Username = usernameOption.Value();
                    //}
                    //
                    //if (passwordOption.HasValue())
                    //{
                    //    config.AuthInfo.Password = passwordOption.Value();
                    //}

                    if (customOption.HasValue())
                    {
                        config.IncludeCustom = customOption.HasValue();
                    }

                    if (includeOption.HasValue())
                    {
                        config.Objects = includeOption.Values;
                    }

                    if (prefixOption.HasValue())
                    {
                        config.ClassPrefix = prefixOption.Value();
                    }

                    if (suffixOption.HasValue())
                    {
                        config.ClassSuffix = suffixOption.Value();
                    }

                    if (suffixOption.HasValue())
                    {
                        config.ClassNamespace = namespaceName.Value();
                    }

                    if (outputDirectory.HasValue())
                    {
                        config.OutputDirectory = outputDirectory.Value();
                    }

                    if (includeReferences.HasValue())
                    {
                        config.IncludeReferences = includeReferences.HasValue();
                    }

                    if (readOnlyPropertiesOption.HasValue())
                    {
                        config.ReadonlyProperties = true;
                    }

                    if (generateEnumPropertiesOption.HasValue())
                    {
                        config.GenerateEnumProperties = true;
                    }

                    if (enumNameMapOption.HasValue())
                    {
                        if (config.EnumNameMap == null)
                        {
                            config.EnumNameMap = new Dictionary<string, string>(StringComparer.Ordinal);
                        }

                        foreach (string entry in enumNameMapOption.Values)
                        {
                            if (!TryParseEnumNameMapping(entry, out string generatedName, out string explicitName))
                            {
                                Console.WriteLine("Ignoring enum name mapping '{0}'. Expected GeneratedName=ExplicitName.", entry);
                                continue;
                            }

                            config.EnumNameMap[generatedName] = explicitName;
                        }
                    }

                    //check for minimum needed options and prompt if necessary
                    config = CheckOptions(config);

                    if (saveConfigOption.HasValue())
                    {
                        SaveConfig(config, configFileOption.Value());
                    }

                    Console.Write("Generate models for " + string.Join(", ", config.Objects));

                    if (customOption.HasValue())
                    {
                        Console.Write(" including custom objects and fields");
                    }

                    Console.WriteLine();

                    GenModels(config).Wait();

                    Console.WriteLine("Done.");
                    return 0;
                });
            });

            try
            {
                app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                Console.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to execute application: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Parses a GeneratedName=ExplicitName mapping. Comparison of the generated name is left to the planner.
        /// </summary>
        public static bool TryParseEnumNameMapping(string entry, out string generatedName, out string explicitName)
        {
            generatedName = null;
            explicitName = null;
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            int separator = entry.IndexOf('=');
            if (separator <= 0 || separator >= entry.Length - 1)
            {
                return false;
            }

            generatedName = entry.Substring(0, separator).Trim();
            explicitName = entry.Substring(separator + 1).Trim();
            return generatedName.Length > 0 && explicitName.Length > 0;
        }

        /// <summary>
        /// Checks that the minimum required options are supplied, otherwise prompts user to enter them immediately
        /// </summary>
        private static GenConfig CheckOptions(GenConfig config)
        {
            //check required auth options
            while (string.IsNullOrEmpty(config.AuthInfo.ClientId))
            {
                Console.WriteLine("Enter API Client ID:");
                config.AuthInfo.ClientId = Console.ReadLine();
                Console.WriteLine();
            }

            while (string.IsNullOrEmpty(config.AuthInfo.ClientSecret))
            {
                Console.WriteLine("Enter API Client Secret:");
                config.AuthInfo.ClientSecret = Console.ReadLine();
                Console.WriteLine();
            }

            //while (string.IsNullOrEmpty(config.AuthInfo.Username))
            //{
            //    Console.WriteLine("Enter API username:");
            //    config.AuthInfo.Username = Console.ReadLine();
            //    Console.WriteLine();
            //}
            //
            //while (string.IsNullOrEmpty(config.AuthInfo.Password))
            //{
            //    Console.WriteLine("Enter API password:");
            //    config.AuthInfo.Password = Console.ReadLine();
            //    Console.WriteLine();
            //}

            //object to generate
            if (config.Objects == null)
            {
                config.Objects = new List<string>();
            }

            while (config.Objects.Count == 0)
            {
                Console.WriteLine("Enter an object name to generate, or enter \"all\" to generate all objects");
                string objectName = Console.ReadLine();
                if (!string.IsNullOrEmpty(objectName))
                {
                    config.Objects.Add(objectName);
                    Console.WriteLine();
                }
            }

            while (string.IsNullOrEmpty(config.ClassNamespace))
            {
                Console.WriteLine("Enter namespace for generated class(es):");
                config.ClassNamespace = Console.ReadLine();
                Console.WriteLine();
            }

            return config;
        }

        private static bool SaveConfig(GenConfig config, string filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = defaultConfigFilename;
                }

                //if using the default filename, or just a filename was given, set the path to the current directory
                if (System.IO.Path.IsPathRooted(filePath))
                {
                    string executabledirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                    filePath = Path.Combine(executabledirectory, filePath);
                }

                Console.WriteLine($"Saving config file to {filePath}");

                string contents = JsonConvert.SerializeObject(config, Formatting.Indented);

                File.WriteAllText(filePath, contents, Encoding.Unicode);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving config file: " + ex.Message);
                return false;
            }
        }

        private static GenConfig LoadConfig(string filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = defaultConfigFilename;
                }

                //if using the default filename, or just a filename was given, set the path to the current directory
                if (System.IO.Path.IsPathRooted(filePath))
                {
                    string executabledirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                    filePath = Path.Combine(executabledirectory, filePath);
                }

                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"No config file found at {filePath}");
                    return null;
                }

                Console.WriteLine($"Loading config file {filePath}");

                string contents = File.ReadAllText(filePath);

                GenConfig config = JsonConvert.DeserializeObject<GenConfig>(contents);

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading config file: " + ex.Message);
                return null;
            }
        }

        private static async Task<ForceClient> Login(GenConfig config)
        {

            AuthenticationClient auth = new AuthenticationClient(config.AuthInfo.ApiVersion);
            try
            {
                await auth.ClientCredentialsAsync(config.AuthInfo.ClientId, config.AuthInfo.ClientSecret, config.AuthInfo.TokenRequestEndpoint);

                Console.WriteLine("Connected to Salesforce");
            }
            catch (ForceAuthException ex)
            {
                Console.WriteLine("Error authenticating: " + ex.Message);
                throw;
            }

            ForceClient client = new ForceClient(auth.AccessInfo.InstanceUrl, auth.ApiVersion, auth.AccessInfo.AccessToken);

            return client;
        }

        private static async Task GenModels(GenConfig config)
        {
            ForceClient client = await Login(config);

            if (config.Objects == null || config.Objects.Count == 0)
            {
                Console.WriteLine("Configured list of objects to generate is empty, nothing will be generated");
                return;
            }

            if(string.IsNullOrEmpty(config.AuthInfo.ApiVersion))
            {
                config.AuthInfo.ApiVersion = client.ApiVersion;
            }

            var global = await client.DescribeGlobal();

            if (string.IsNullOrEmpty(config.OutputDirectory))
            {
                config.OutputDirectory = Directory.GetCurrentDirectory();
            }

            Console.WriteLine("Output directory: " + config.OutputDirectory);

            GenInterface(config);

            bool generateAll = false;
            if (config.Objects != null && config.Objects.Count > 0)
            {
                if (config.Objects[0].ToLower() == "all")
                {
                    generateAll = true;
                    Console.WriteLine("Including all objects");
                }
                else
                {
                    Console.WriteLine("Included: " + string.Join(", ", config.Objects));
                }
            }

            var describedObjects = new List<DescribedObject>();

            foreach (var obj in global.SObjects)
            {
                //TODO: verify if we should skip all non queryable?
                if (!obj.Queryable)
                {
#if DEBUG
                    Console.WriteLine("Skipping non-queryable object " + obj.Name);
#endif
                    continue;
                }

                if (!generateAll)
                {
                    if (config.Objects != null && config.Objects.Count > 0)
                    {
                        bool incl = config.Objects.Where(o => o.ToLowerInvariant() == obj.Name.ToLowerInvariant()).Count() > 0;
                        if (!incl)
                        {
#if DEBUG
                            Console.WriteLine("Skipping " + obj.Name);
#endif
                            continue;
                        }
                    }
                }

                //TODO: verify Name and Domain non-queryable objects cause compiler errors due to name/member dupe

                Console.WriteLine("Describing {0}", obj.Name);

                string className = string.Format("{0}{1}{2}", config.ClassPrefix ?? string.Empty, obj.Name, config.ClassSuffix ?? string.Empty);

                describedObjects.Add(new DescribedObject
                {
                    ObjectApiName = obj.Name,
                    ClassName = className,
                    Describe = await client.GetObjectDescribe(obj.Name)
                });
            }

            PicklistEnumPlan enumPlan = PicklistEnumPlanner.Build(describedObjects, config);

            if (config.GenerateEnumProperties)
            {
                WriteEnumSupportFiles(config, enumPlan);
            }

            foreach (var described in describedObjects)
            {
                Console.Write("Generating model for {0} - ", described.ObjectApiName);
                CreateModel(described.Describe, described.ClassName, config, enumPlan);
            }
        }

        public static void WriteEnumSupportFiles(GenConfig config, PicklistEnumPlan enumPlan)
        {
            SerializationHelperGenerator.WriteHelpers(config);

            if (enumPlan.SharedEnums.Count == 0)
            {
                return;
            }

            string enumDirectory = Path.Combine(config.OutputDirectory, "Enum");
            Directory.CreateDirectory(enumDirectory);

            foreach (var shared in enumPlan.SharedEnums)
            {
                string filePath = Path.Combine(enumDirectory, shared.TypeName + ".cs");
                Console.WriteLine("Writing: " + filePath);
                File.WriteAllText(filePath, PicklistEnumCodeGen.SharedEnumFile(shared, config.ClassNamespace));
            }
        }

        public static async Task CreateModel(ForceClient client, string objectName, string className, GenConfig config)
        {
            SObjectDescribeFull data = await client.GetObjectDescribe(objectName);
            CreateModel(data, className, config, null);
        }

        public static void CreateModel(SObjectDescribeFull data, string className, GenConfig config, PicklistEnumPlan enumPlan)
        {
            string model = GenClass(data, className, config, enumPlan);

            string fileName = string.Format("{0}.cs", className);

            string filePath = Path.Combine(config.OutputDirectory, fileName);

            Console.WriteLine("Writing: " + filePath);

            File.WriteAllText(filePath, model);
        }

        public static void GenInterface(GenConfig config)
        {
            StringBuilder gen = new StringBuilder();

            if (!string.IsNullOrEmpty(config.ClassNamespace))
            {
                gen.AppendLine("namespace " + config.ClassNamespace);
                gen.AppendLine("{");
            }

            gen.AppendLine("\t/// <summary>");
            gen.AppendLine("\t/// Provides a common identity contract for all generated SObject classes.");
            gen.AppendLine("\t/// </summary>");
            gen.AppendLine("\tpublic interface ISObjectIdentity");
            gen.AppendLine("\t{");
            gen.AppendLine("\t\tstring? Id { get; set; }");
            gen.AppendLine("\t\tstatic abstract string SObjectTypeName { get; }");
            gen.AppendLine("\t\tstatic string GetTableName(System.Type type)");
            gen.AppendLine("\t\t{");
            gen.AppendLine("\t\t\tif (!typeof(ISObjectIdentity).IsAssignableFrom(type))");
            gen.AppendLine("\t\t\t{");
            gen.AppendLine("\t\t\t\tthrow new NotSupportedException($\"Type of {type.FullName} does not implement ISObjectIdentity\");");
            gen.AppendLine("\t\t\t}");
            gen.AppendLine("");
            gen.AppendLine("\t\t\treturn (string)type.GetProperty(nameof(ISObjectIdentity.SObjectTypeName), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.GetValue(null)!;");
            gen.AppendLine("\t\t}");
            gen.AppendLine("\t}");

            if (!string.IsNullOrEmpty(config.ClassNamespace))
            {
                gen.AppendLine("}");
            }

            string filePath = Path.Combine(config.OutputDirectory, "ISObjectIdentity.cs");

            Console.WriteLine("Writing: " + filePath);

            File.WriteAllText(filePath, gen.ToString());
        }

        public static async Task<string> GenClass(ForceClient client, string objectName, string className, GenConfig config)
        {
            SObjectDescribeFull data = await client.GetObjectDescribe(objectName);
            return GenClass(data, className, config, null);
        }

        public static string GenClass(SObjectDescribeFull data, string className, GenConfig config, PicklistEnumPlan enumPlan)
        {
            StringBuilder gen = new StringBuilder();

            //gen.AppendLine("// Model generated on " + DateTime.Now.ToString("yyyy-MM-dd"));
            gen.AppendLine("// SF API version " + config.AuthInfo.ApiVersion);
            gen.AppendLine("// Custom fields included: " + config.IncludeCustom.ToString());
            gen.AppendLine("// Relationship objects included: " + config.IncludeReferences.ToString());
            gen.AppendLine();

            //need rename of Task to Task_sf, Domain => Domain_sf, Name => Name_sf

            string newline = Environment.NewLine;

            List<PicklistEnumProperty> objectEnumProperties = null;
            List<NestedPicklistEnum> nestedEnums = null;

            bool hasEnumProperties = enumPlan != null
                && enumPlan.PropertiesByObject.TryGetValue(data.Name, out objectEnumProperties)
                && objectEnumProperties.Count > 0;
            if (!hasEnumProperties)
            {
                objectEnumProperties = null;
            }

            bool hasNestedEnums = enumPlan != null
                && enumPlan.NestedEnumsByObject.TryGetValue(data.Name, out nestedEnums)
                && nestedEnums.Count > 0;
            if (!hasNestedEnums)
            {
                nestedEnums = null;
            }

            bool hasMultipicklistEnums = objectEnumProperties != null
                && objectEnumProperties.Exists(p => p.IsMultipicklist);

            gen.AppendLine("using System;");
            if (hasMultipicklistEnums)
            {
                gen.AppendLine("using System.Collections.Generic;");
            }
            gen.AppendLine("using NetCoreForce.Client.Models;");
            gen.AppendLine("using NetCoreForce.Client.Attributes;");
            gen.AppendLine("using Newtonsoft.Json;");
            gen.AppendLine();
            if (!string.IsNullOrEmpty(config.ClassNamespace))
            {
                gen.AppendLine("namespace " + config.ClassNamespace);
                gen.AppendLine("{");
            }
            gen.AppendLine("\t///<summary>");
            gen.AppendLine($"\t/// {WebUtility.HtmlEncode(data.Label)}");
            gen.AppendLine($"\t///<para>SObject Name: {data.Name}</para>");
            gen.AppendLine($"\t///<para>Custom Object: {data.Custom.ToString()}</para>");
            gen.AppendLine("\t///</summary>");
            gen.AppendLine($"\tpublic partial class {className} : SObject, ISObjectIdentity");
            gen.AppendLine("\t{");

            gen.AppendLine("\t\t[JsonIgnore]");
            gen.AppendLine("\t\tpublic static string SObjectTypeName");
            gen.AppendLine("\t\t{");
            // gen.AppendLine("\t\t\tget { return \"" + data.Name + "\"; }");
            gen.AppendLine($"\t\t\tget {{ return \"{data.Name}\"; }}");
            gen.AppendLine("\t\t}");
            gen.AppendLine();

            // gen.AppendLine("\t\tpublic " + className + "() : base (\"" + objectName + "\")");
            // gen.AppendLine("\t\t{}");
            // gen.AppendLine();

            foreach (var field in data.Fields.OrderBy(f => f.Name?.ToLower()))
            {
                GenerateField(config, field, gen, objectEnumProperties);
            }

            gen.AppendLine("\t}");

            if (nestedEnums != null)
            {
                string enumIndent = string.IsNullOrEmpty(config.ClassNamespace) ? string.Empty : "\t";
                foreach (var nested in nestedEnums)
                {
                    gen.AppendLine();
                    PicklistEnumCodeGen.AppendEnumType(gen, nested.TypeName, nested.Members, enumIndent);
                }
            }

            if (!string.IsNullOrEmpty(config.ClassNamespace))
            {
                gen.AppendLine("}");
            }

            string result = gen.ToString();

            return result;
        }

        private static void GenerateField(GenConfig config, SObjectFieldMetadata field, StringBuilder gen, List<PicklistEnumProperty> objectEnumProperties)
        {
            if (field.Custom && !config.IncludeCustom)
            {
                return;
            }                    

            gen.AppendLine("\t\t///<summary>");
            gen.AppendLine("\t\t/// " + WebUtility.HtmlEncode(field.Label));
            gen.AppendLine("\t\t/// <para>Name: " + field.Name + "</para>");
            gen.AppendLine("\t\t/// <para>SF Type: " + field.Type + "</para>");
            if (field.AutoNumber)
            {
                gen.AppendLine("\t\t/// <para>AutoNumber field</para>");
            }
            //gen.AppendLine("\t\t/// <para>Custom: " + field.Custom.ToString() + "</para>");
            if (field.Custom)
            {
                gen.AppendLine("\t\t/// <para>Custom field</para>");
            }

            gen.AppendLine("\t\t/// <para>Nillable: " + field.Nillable.ToString() + "</para>");

            gen.AppendLine("\t\t///</summary>");

            gen.AppendLine(string.Format("\t\t[JsonProperty(PropertyName = \"{0}\")]", JsonName(field.Name)));

            if (!field.Creatable || !field.Updateable)
            {
                gen.AppendLine(string.Format("\t\t[Updateable({0}), Createable({1})]", field.Updateable.ToString().ToLower(), field.Creatable.ToString().ToLower()));
            }

            string csTypeName = SfTypeConverter.GetTypeName(field.Type);

            switch (csTypeName)
            {
                case "Boolean":
                    csTypeName = "bool";
                    break;
                case "String":
                    csTypeName = "string";
                    break;
                case "Double":
                    csTypeName = "double";
                    break;
                case "Int32":
                    csTypeName = "int";
                    break;
                case "Decimal":
                    csTypeName = "decimal";
                    break;
                default:
                    break;
            }

            //we want all nullable types in the model, so that they are not serialized/initialized with default values
            //if (csTypeName == "bool" || csTypeName == "DateTimeOffset" || csTypeName == "DateTime" || csTypeName == "int" || csTypeName == "double" || csTypeName == "decimal")
            //{
            //    csTypeName += "?";
            //}
            csTypeName += "?";

            var setter = config.ReadonlyProperties
                         && (!field.Updateable && !field.Creatable)
                         && field.Name != "Id"
                ? "protected set;"
                : "set;";

            gen.AppendLine($"\t\tpublic {csTypeName} {field.Name} {{ get; {setter} }}");
            gen.AppendLine();

            if (objectEnumProperties != null)
            {
                var enumProperty = objectEnumProperties.Find(p => p.FieldName == field.Name);
                if (enumProperty != null)
                {
                    PicklistEnumCodeGen.AppendEnumProperty(gen, enumProperty, "\t\t");
                    gen.AppendLine();
                }
            }

            if (field.Type == "reference" && config.IncludeReferences)
            {
                if (string.IsNullOrEmpty(field.RelationshipName) || field.ReferenceTo.Count > 1)
                {
                    //only do single-object relationships
                    return;
                }

                if(field.RelationshipName == "ContentBody")
                {
                    //exception for non-serializable type
                    return;
                }

                gen.AppendLine("\t\t///<summary>");
                gen.AppendLine("\t\t/// ReferenceTo: " + field.ReferenceTo[0]);
                gen.AppendLine("\t\t/// <para>RelationshipName: " + field.RelationshipName + "</para>");
                gen.AppendLine("\t\t///</summary>");
                gen.AppendLine(string.Format("\t\t[JsonProperty(PropertyName = \"{0}\")]", JsonName(field.RelationshipName)));
                gen.AppendLine("\t\t[Updateable(false), Createable(false)]");

                string referenceClass = GetPrefixedSuffixed(config, field.ReferenceTo[0]);

                gen.AppendLine($"\t\tpublic {referenceClass} {field.RelationshipName} {{ get; {setter} }}");
                gen.AppendLine();
            }
        }

        private static string GetPrefixedSuffixed(GenConfig config, string name)
        {
            return string.Format("{0}{1}{2}", config.ClassPrefix ?? string.Empty, name, config.ClassSuffix ?? string.Empty);
        }

        private static string JsonName(string fieldName)
        {
            string jsonName = fieldName;
            string first = jsonName.Substring(0, 1).ToLower();
            jsonName = first + jsonName.Substring(1, jsonName.Length - 1);
            return jsonName;
        }
    }
}