using System.ComponentModel.DataAnnotations;

namespace GherkinSync.Models
{
    public class CustomField
    {
        [Display(Name = "Custom Name", Description = "Azure DevOps field full name eg.: Microsoft.VSTS.Common.AreaPath")]
        public string FieldName { get; set; } = string.Empty;

        [Display(Name = "Default value", Description = "The default value to be set on all new test cases.")]
        public string DefaultValue { get; set; } = string.Empty;
    }
}
