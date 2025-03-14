using GherkinSync.Models;
using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;
using System.ComponentModel;

namespace GherkinSync
{
    public class OptionPageGrid : DialogPage
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
        public List<CustomField> CustomFields { get; set; } = new List<CustomField>();
    }
}
