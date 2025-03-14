using System.Collections.Generic;

namespace GherkinSync.Models
{
    internal class TestCase
    {
        public int TestCaseId { get; set; }

        public int TestCaseFirstLine { get; set; }

        public int ReferenceTagLine { get; set; }

        public bool ReferenceTagExists { get; set; }

        public string FeatureName { get; set; } = string.Empty;

        public string FeatureDescription { get; set; } = string.Empty;

        public string RuleName { get; set; } = string.Empty;

        public string RuleDescription { get; set; } = string.Empty;

        public string TestCaseName { get; set; } = string.Empty;

        public string TestCaseDescription { get; set; } = string.Empty;

        public List<string> BackgroundSteps { get; set; } = new List<string>();

        public List<string> Steps { get; set; } = new List<string>();
    }
}
