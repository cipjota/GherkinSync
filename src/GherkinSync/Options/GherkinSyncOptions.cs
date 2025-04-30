using GherkinSync.Models;
using System.Collections.Generic;
using System.ComponentModel;

namespace GherkinSync.Options
{
    public class GherkinSyncOptions : BaseOptionModel<GherkinSyncOptions>
    {
        [Category("Azure DevOps")]
        [DisplayName("AzureDevops instance URL")]
        [Description(
            "The base url of the AzureDevops instance you want to connect to. e.g. https://foo.visualstudio.com")]
        public string AzureDevopsBaseUrl { get; set; } = "https://foo.visualstudio.com";

        [Category("Azure DevOps")]
        [DisplayName("Project name")]
        [Description(
            "The name of the project you want to add tests to, can be changed on the fly during Test Case creation")]
        public string ProjectName { get; set; } = "ProjectFoo";

        [Category("Azure DevOps")]
        [DisplayName("PAT Code")]
        [Description("An authorised PAT (unencrypted) to access Azure Devops")]
        public string PatToken { get; set; } = "LargePatTokenString";

        [Category("Test Cases")]
        [DisplayName("Custom fields")]
        [Description(
            "Custom fields added to the Test Case template.")]
        public List<CustomField> CustomFields { get; set; } = [];

        [Category("Test Cases")]
        [DisplayName("Description template")]
        [Description(
            "Template for the description field of the test cases.")]
        public string DescriptionTemplate { get; set; } = "";

        [Category("Test Cases")]
        [DisplayName("Add background to steps")]
        [Description(
            "Adds the background steps to the steps of the test case.")]
        public bool BackgroundAsSteps { get; set; } = false;

        [Category("Test Plan Management")]
        [DisplayName("Test plan reference id tag prefix")]
        [Description(
            "ID of the test plan")]
        public string TestPlanReferenceIdTag { get; set; } = "TestPlanReference";

        [Category("Test Plan Management")]
        [DisplayName("Test plan reference id tag prefix")]
        [Description(
            "ID of the test suite")]
        public string TestSuiteReferenceIdTag { get; set; } = "TestSuiteReference";

        [Category("Test Plan Management")]
        [DisplayName("Test case reference id tag prefix")]
        [Description(
            "ID of the test case")]
        public string TestCaseReferenceIdTag { get; set; } = "TestCaseReference";

        [Category("Test Plan Management")]
        [DisplayName("Remove test cases from suite")]
        [Description(
            "Removes test cases from test suite that do not exist on the feature file")]
        public bool RemoveTestCasesFromSuite { get; set; } = false;
    }
}
