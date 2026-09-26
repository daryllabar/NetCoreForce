using System.Collections.Generic;
using System.ComponentModel;
using NetCoreForce.Client.Models;
using Newtonsoft.Json;

namespace NetCoreForce.ModelGenerator
{
    public class GenConfig
    {
        public AuthInfo AuthInfo { get; set; }

        public string OutputDirectory { get; set; }
        public List<string> Objects { get; set; }
        public string ClassPrefix { get; set; }
        public string ClassSuffix { get; set; }
        public string ClassNamespace { get; set; }
        public bool IncludeCustom { get; set; }
        public bool IncludeReferences { get; set; }
        public bool ReadonlyProperties { get; set; }
        public bool GenerateEnumProperties { get; set; }

        /// <summary>
        /// Optional case-sensitive map from a generated enum type name to an explicit name.
        /// Omit the property to keep generated names. Keys are the names the generator would emit,
        /// including any uniqueness suffix. Explicit names are used as given; naming collisions are not checked.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> EnumNameMap { get; set; }

        public GenConfig()
        {
            this.AuthInfo = new AuthInfo(){
                TokenRequestEndpoint = "https://login.salesforce.com/services/oauth2/token",
                ApiVersion = "v64.0"
            };
        }
    }
}