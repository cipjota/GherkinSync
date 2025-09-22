using Gherkin.Ast;
using GherkinSync.Models;
using GherkinSync.Options;
using GherkinSync.ToolWindows;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GherkinSync.Core.Gherkin
{
    public static class FeatureFileUpdater
    {
        /// <summary>
        /// Updates the feature file with the latest test case, suite, and plan tags.
        /// </summary>
        public static void Update(string filePath, IEnumerable<TestCase> testCases, Feature feature, SyncOptionsDialogViewModel options)
        {
            var lines = File.ReadAllLines(filePath).ToList();

            var groupedTestCaseIds = testCases
                .Select(tc => new
                {
                    tc.ReferenceTagLine,
                    tc.TestCaseFirstLine,
                    tc.TestCaseId,
                    InsertLine = tc.ReferenceTagExists
                        ? tc.ReferenceTagLine
                        : tc.TestCaseFirstLine - 1
                })
                .GroupBy(tc => tc.InsertLine)
                .Select(g => new { Line = g.Key, Ids = string.Join(",", g.Select(x => x.TestCaseId)) })
                .OrderByDescending(x => x.Line);

            foreach (var group in groupedTestCaseIds)
            {
                if (lines[group.Line].Contains(GherkinSyncOptions.Instance.TestCaseReferenceIdTag))
                {
                    lines[group.Line] = UpdateReferenceTagLine(
                        lines[group.Line],
                        GherkinSyncOptions.Instance.TestCaseReferenceIdTag,
                        group.Ids);
                }
                else
                {
                    lines.Insert(group.Line, $"@{GherkinSyncOptions.Instance.TestCaseReferenceIdTag}({group.Ids})");
                }
            }

            var suiteTag = feature.Tags.FirstOrDefault(t =>
                Regex.IsMatch(t.Name, $"@{GherkinSyncOptions.Instance.TestSuiteReferenceIdTag}\\((.*)\\)"));

            if (suiteTag != null)
            {
                var index = suiteTag.Location.Line - 1;
                lines[index] = UpdateReferenceTagLine(
                    lines[index],
                    GherkinSyncOptions.Instance.TestSuiteReferenceIdTag,
                    options.TestSuiteId.ToString());
            }
            else
            {
                lines.Insert(feature.Location.Line - 1,
                    $"@{GherkinSyncOptions.Instance.TestSuiteReferenceIdTag}({options.TestSuiteId})");
            }

            var planTag = feature.Tags.FirstOrDefault(t =>
                Regex.IsMatch(t.Name, $"@{GherkinSyncOptions.Instance.TestPlanReferenceIdTag}\\((.*)\\)"));

            if (planTag != null)
            {
                var index = planTag.Location.Line - 1;
                lines[index] = UpdateReferenceTagLine(
                    lines[index],
                    GherkinSyncOptions.Instance.TestPlanReferenceIdTag,
                    options.TestPlanId.ToString());
            }
            else
            {
                lines.Insert(feature.Location.Line - 1,
                    $"@{GherkinSyncOptions.Instance.TestPlanReferenceIdTag}({options.TestPlanId})");
            }

            File.WriteAllLines(filePath, lines);
        }

        /// <summary>
        /// Replaces an existing tag line with a new value.
        /// </summary>
        private static string UpdateReferenceTagLine(string currentLine, string tagPattern, string newValue)
        {
            string pattern = $@"@{Regex.Escape(tagPattern)}\(([\d,]*)\)";
            return Regex.Replace(currentLine, pattern, $"@{tagPattern}({newValue})");
        }
    }
}
