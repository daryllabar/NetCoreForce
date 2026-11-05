using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using NetCoreForce.Client;

namespace NetCoreForce.ModelGenerator
{
    /// <summary>
    /// An inactive picklist API value that SObject describe did not return.
    /// </summary>
    public sealed class InactivePicklistValue
    {
        /// <summary>Full field API name, for example Status__c.</summary>
        public string FieldName { get; set; }
        public string Value { get; set; }
        public string Label { get; set; }
    }

    /// <summary>
    /// A custom field whose picklist values come from a global value set.
    /// </summary>
    public sealed class GlobalValueSetReference
    {
        public string FieldName { get; set; }
        public string ValueSetName { get; set; }
    }

    /// <summary>
    /// Thrown when a Metadata API read fails.
    /// </summary>
    public sealed class MetadataApiException : Exception
    {
        public MetadataApiException(string message, bool accessDenied)
            : base(message)
        {
            AccessDenied = accessDenied;
        }

        public bool AccessDenied { get; }
    }

    /// <summary>
    /// Loads inactive picklist values for custom fields through the Metadata API.
    /// SObject describe returns active values only, and this login cannot query the Tooling API
    /// <c>CustomField</c> object. <c>readMetadata</c> returns the full value set, including inactive values,
    /// once the user has Modify Metadata Through Metadata API Functions. Standard fields stay active-only.
    /// </summary>
    public static class MetadataPicklistValueReader
    {
        private const int ReadBatchSize = 10;
        private static readonly HttpClient Http = new HttpClient();

        public static async Task<List<InactivePicklistValue>> GetInactiveValues(ForceClient client, DescribedObject described)
        {
            var results = new List<InactivePicklistValue>();
            if (client == null || described?.Describe?.Fields == null || string.IsNullOrEmpty(described.ObjectApiName))
            {
                return results;
            }

            var fullNames = new List<string>();
            foreach (var field in described.Describe.Fields)
            {
                if (field != null && field.Custom && PicklistEnumPlanner.IsEnumFieldType(field.Type) && !string.IsNullOrEmpty(field.Name))
                {
                    fullNames.Add(described.ObjectApiName + "." + field.Name);
                }
            }

            if (fullNames.Count == 0)
            {
                return results;
            }

            var globalReferences = new List<GlobalValueSetReference>();
            foreach (var batch in Batches(fullNames))
            {
                string xml = await ReadMetadata(client, "CustomField", batch).ConfigureAwait(false);
                MetadataPicklistXml.ReadCustomFields(xml, results, globalReferences);
            }

            var valueSets = new Dictionary<string, List<InactivePicklistValue>>(StringComparer.OrdinalIgnoreCase);
            var missingSets = globalReferences
                .Select(r => r.ValueSetName)
                .Where(name => !string.IsNullOrEmpty(name) && !valueSets.ContainsKey(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var batch in Batches(missingSets))
            {
                string xml = await ReadMetadata(client, "GlobalValueSet", batch).ConfigureAwait(false);
                foreach (var set in MetadataPicklistXml.ReadGlobalValueSets(xml))
                {
                    valueSets[set.Key] = set.Value;
                }
            }

            foreach (var reference in globalReferences)
            {
                if (reference == null || string.IsNullOrEmpty(reference.FieldName) || string.IsNullOrEmpty(reference.ValueSetName))
                {
                    continue;
                }

                if (!valueSets.TryGetValue(reference.ValueSetName, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    results.Add(new InactivePicklistValue
                    {
                        FieldName = reference.FieldName,
                        Value = entry.Value,
                        Label = entry.Label
                    });
                }
            }

            return results;
        }

        internal static async Task<string> ReadMetadata(ForceClient client, string metadataType, IReadOnlyList<string> fullNames)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrEmpty(client.InstanceUrl)) throw new ArgumentException("Salesforce instance URL is missing.", nameof(client));
            if (string.IsNullOrEmpty(client.AccessToken)) throw new ArgumentException("Salesforce access token is missing.", nameof(client));

            string version = (client.ApiVersion ?? string.Empty).Trim().TrimStart('v', 'V');
            if (string.IsNullOrEmpty(version))
            {
                version = "57.0";
            }

            string url = client.InstanceUrl.TrimEnd('/') + "/services/Soap/m/" + version;
            var envelope = new StringBuilder();
            envelope.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            envelope.Append("<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:met=\"http://soap.sforce.com/2006/04/metadata\">");
            envelope.Append("<soapenv:Header><met:SessionHeader><met:sessionId>");
            envelope.Append(XmlEscape(client.AccessToken));
            envelope.Append("</met:sessionId></met:SessionHeader></soapenv:Header>");
            envelope.Append("<soapenv:Body><met:readMetadata><met:type>");
            envelope.Append(XmlEscape(metadataType));
            envelope.Append("</met:type>");
            foreach (var fullName in fullNames)
            {
                envelope.Append("<met:fullNames>");
                envelope.Append(XmlEscape(fullName));
                envelope.Append("</met:fullNames>");
            }
            envelope.Append("</met:readMetadata></soapenv:Body></soapenv:Envelope>");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(envelope.ToString(), Encoding.UTF8, "text/xml");
            request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");

            HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string fault = MetadataPicklistXml.FaultMessage(body);
            if (!string.IsNullOrEmpty(fault) || !response.IsSuccessStatusCode)
            {
                string message = string.IsNullOrEmpty(fault)
                    ? "Metadata API returned " + (int)response.StatusCode + " " + response.ReasonPhrase
                    : fault;
                bool accessDenied = message.IndexOf("INSUFFICIENT_ACCESS", StringComparison.OrdinalIgnoreCase) >= 0;
                throw new MetadataApiException(message, accessDenied);
            }

            return body;
        }

        private static IEnumerable<List<string>> Batches(IReadOnlyList<string> names)
        {
            for (int index = 0; index < names.Count; index += ReadBatchSize)
            {
                int count = Math.Min(ReadBatchSize, names.Count - index);
                var batch = new List<string>(count);
                for (int offset = 0; offset < count; offset++)
                {
                    batch.Add(names[index + offset]);
                }

                yield return batch;
            }
        }

        private static string XmlEscape(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }

    /// <summary>
    /// Reads inactive picklist entries out of a Metadata API <c>readMetadata</c> response.
    /// </summary>
    public static class MetadataPicklistXml
    {
        public static void ReadCustomFields(string xml, IList<InactivePicklistValue> inactiveValues, IList<GlobalValueSetReference> globalReferences)
        {
            if (string.IsNullOrEmpty(xml) || inactiveValues == null)
            {
                return;
            }

            foreach (var record in XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "records"))
            {
                string fullName = Child(record, "fullName");
                int split = fullName == null ? -1 : fullName.IndexOf('.');
                if (split < 0 || split == fullName.Length - 1)
                {
                    continue;
                }

                string fieldName = fullName.Substring(split + 1);
                XElement valueSet = record.Elements().FirstOrDefault(e => e.Name.LocalName == "valueSet");
                if (valueSet == null)
                {
                    continue;
                }

                string valueSetName = Child(valueSet, "valueSetName");
                if (!string.IsNullOrEmpty(valueSetName))
                {
                    globalReferences?.Add(new GlobalValueSetReference
                    {
                        FieldName = fieldName,
                        ValueSetName = valueSetName
                    });
                    continue;
                }

                foreach (var value in valueSet.Descendants().Where(e => e.Name.LocalName == "value"))
                {
                    if (!IsInactive(value))
                    {
                        continue;
                    }

                    string apiValue = Child(value, "fullName");
                    if (string.IsNullOrEmpty(apiValue))
                    {
                        continue;
                    }

                    inactiveValues.Add(new InactivePicklistValue
                    {
                        FieldName = fieldName,
                        Value = apiValue,
                        Label = Child(value, "label")
                    });
                }
            }
        }

        public static Dictionary<string, List<InactivePicklistValue>> ReadGlobalValueSets(string xml)
        {
            var sets = new Dictionary<string, List<InactivePicklistValue>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(xml))
            {
                return sets;
            }

            foreach (var record in XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "records"))
            {
                string name = Child(record, "fullName");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var inactive = new List<InactivePicklistValue>();
                foreach (var value in record.Elements().Where(e => e.Name.LocalName == "customValue"))
                {
                    if (!IsInactive(value))
                    {
                        continue;
                    }

                    string apiValue = Child(value, "fullName");
                    if (string.IsNullOrEmpty(apiValue))
                    {
                        continue;
                    }

                    inactive.Add(new InactivePicklistValue
                    {
                        Value = apiValue,
                        Label = Child(value, "label")
                    });
                }

                sets[name] = inactive;
            }

            return sets;
        }

        public static string FaultMessage(string xml)
        {
            if (string.IsNullOrEmpty(xml) || xml.IndexOf("faultstring", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            try
            {
                XElement fault = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring");
                return fault == null ? null : fault.Value;
            }
            catch (System.Xml.XmlException)
            {
                return "Metadata API request failed.";
            }
        }

        private static bool IsInactive(XElement value)
        {
            string active = Child(value, "isActive");
            return string.Equals(active, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static string Child(XElement parent, string localName)
        {
            return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        }
    }
}
