using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NetCoreForce.ModelGenerator.Tests
{
    public class MetadataPicklistXmlTests
    {
        [Fact]
        public void ReadCustomFields_ReturnsInactiveLocalValuesAndGlobalReferences()
        {
            string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns=""http://soap.sforce.com/2006/04/metadata"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <soapenv:Body>
    <readMetadataResponse>
      <result>
        <records xsi:type=""CustomField"">
          <fullName>rto_Warranty__c.Distributor_Name__c</fullName>
          <valueSet>
            <valueSetDefinition>
              <value><fullName>Open</fullName><label>Open</label></value>
              <value><fullName>BEACON BUILDING PRODUCTS</fullName><isActive>false</isActive><label>BEACON BUILDING PRODUCTS</label></value>
            </valueSetDefinition>
          </valueSet>
        </records>
        <records xsi:type=""CustomField"">
          <fullName>rto_Warranty__c.rto_PropertyState__c</fullName>
          <valueSet>
            <restricted>true</restricted>
            <valueSetName>rto_WarrantyStates</valueSetName>
          </valueSet>
        </records>
      </result>
    </readMetadataResponse>
  </soapenv:Body>
</soapenv:Envelope>";

            var inactive = new List<InactivePicklistValue>();
            var globals = new List<GlobalValueSetReference>();
            MetadataPicklistXml.ReadCustomFields(xml, inactive, globals);

            Assert.Equal("Distributor_Name__c", inactive.Single().FieldName);
            Assert.Equal("BEACON BUILDING PRODUCTS", inactive.Single().Value);
            Assert.Equal("rto_PropertyState__c", globals.Single().FieldName);
            Assert.Equal("rto_WarrantyStates", globals.Single().ValueSetName);
        }

        [Fact]
        public void ReadGlobalValueSets_ReturnsOnlyInactiveEntries()
        {
            string xml = @"<readMetadataResponse xmlns=""http://soap.sforce.com/2006/04/metadata"">
              <result>
                <records>
                  <fullName>rto_WarrantyStates</fullName>
                  <customValue><fullName>AL</fullName><label>AL</label></customValue>
                  <customValue><fullName>ZZ</fullName><isActive>false</isActive><label>Obsolete</label></customValue>
                </records>
              </result>
            </readMetadataResponse>";

            var sets = MetadataPicklistXml.ReadGlobalValueSets(xml);

            Assert.Equal("ZZ", sets["rto_WarrantyStates"].Single().Value);
            Assert.Equal("Obsolete", sets["rto_WarrantyStates"].Single().Label);
        }

        [Fact]
        public void FaultMessage_ReadsSoapFault()
        {
            string xml = @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
              <soapenv:Body><soapenv:Fault><faultstring>INSUFFICIENT_ACCESS: metadata</faultstring></soapenv:Fault></soapenv:Body>
            </soapenv:Envelope>";

            Assert.Contains("INSUFFICIENT_ACCESS", MetadataPicklistXml.FaultMessage(xml));
            Assert.Null(MetadataPicklistXml.FaultMessage("<ok/>"));
        }
    }
}
