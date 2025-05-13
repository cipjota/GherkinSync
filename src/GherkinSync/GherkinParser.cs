using Gherkin.Ast;
using GherkinSync.Models;
using GherkinSync.Options;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GherkinSync
{
    internal static class GherkinParser
    {
        internal static List<TestCase> ConvertToTestCases(IEnumerable<Scenario> scenarios, List<string> backgroundSteps, string featureName, string featureDescription, string ruleName = "", string ruleDescription = "")
        {
            var testCasesList = new List<TestCase>();
            foreach (var scenario in scenarios)
            {
                var scenarioName = scenario.Name;
                var scenarioDescription = scenario.Description;
                var testCaseIds = new int[0];
                var testCaseReferenceTagLine = 0;
                var testCaseReferenceExists = false;

                var testCaseReferenceTag = scenario.Tags.Where(t => Regex.Match(t.Name, "@" + GherkinSyncOptions.Instance.TestCaseReferenceIdTag + "\\((.*)\\)").Success).FirstOrDefault();
                if (testCaseReferenceTag != null)
                {
                    testCaseReferenceExists = true;
                    testCaseReferenceTagLine = testCaseReferenceTag.Location.Line - 1;
                    testCaseIds = Regex.Match(testCaseReferenceTag.Name, "\\((.*)\\)")
                        .Groups[1]
                        .Value
                        .Split(',')
                        .Select(s => int.Parse(s))
                        .ToArray();
                }

                if (scenario.Examples.Any())
                {
                    var scenarioExample = scenario.Examples.ToArray()[0];
                    var scenarioRows = scenarioExample.TableBody.ToArray();
                    for (int i = 0; i < scenarioRows.Length; i++)
                    {
                        testCasesList.Add(new TestCase()
                        {
                            TestCaseFirstLine = scenario.Location.Line,
                            ReferenceTagLine = testCaseReferenceTagLine,
                            ReferenceTagExists = testCaseReferenceExists,
                            BackgroundSteps = backgroundSteps,
                            FeatureName = featureName,
                            FeatureDescription = featureDescription,
                            RuleName = ruleName,
                            RuleDescription = ruleDescription,
                            TestCaseDescription = scenarioDescription,
                            TestCaseName = scenarioName,
                            TestCaseId = testCaseIds.Length > i ? testCaseIds[i] : -1,
                            Steps = StepsToList(scenario.Steps).Select(s =>
                                DictionaryFromExample(scenarioExample.TableHeader, scenarioRows[i])
                                .Aggregate(s, (current, kvp) => current.Replace(kvp.Key, kvp.Value))
                            ).ToList()
                        });
                    }
                }
                else
                {
                    testCasesList.Add(new TestCase()
                    {
                        TestCaseFirstLine = scenario.Location.Line,
                        ReferenceTagLine = testCaseReferenceTagLine,
                        ReferenceTagExists = testCaseReferenceExists,
                        BackgroundSteps = backgroundSteps,
                        FeatureName = featureName,
                        FeatureDescription = featureDescription,
                        RuleName = ruleName,
                        RuleDescription = ruleDescription,
                        TestCaseDescription = scenarioDescription,
                        TestCaseName = scenarioName,
                        TestCaseId = testCaseIds.Length > 0 ? testCaseIds[0] : -1,
                        Steps = StepsToList(scenario.Steps)
                    });
                }
            }

            return testCasesList;
        }

        internal static List<string> StepsToList(IEnumerable<Step> steps)
        {
            return steps.Select(step =>
                step.Keyword +
                step.Text +
                ConvertGherkinDataTableToAsciiTable((DataTable)step.Argument)
            ).ToList();
        }

        internal static Dictionary<string, string> DictionaryFromExample(TableRow tableHeader, TableRow tableRow)
        {
            var dict = new Dictionary<string, string>();

            for (int i = 0; i < tableHeader.Cells.Count(); i++)
            {
                dict.Add("<" + tableHeader.Cells.ToArray()[i].Value + ">", tableRow.Cells.ToArray()[i].Value);
            }

            return dict;
        }

        internal static string ConvertGherkinDataTableToAsciiTable(DataTable gherkinDataTable)
        {
            if (gherkinDataTable == null || !gherkinDataTable.Rows.Any())
            {
                return "";
            }


            // Determine the maximum width for each column
            var columnWidths = new int[gherkinDataTable.Rows.ToArray()[0].Cells.Count()];
            foreach (var row in gherkinDataTable.Rows)
            {
                var rowCells = row.Cells.ToArray();
                for (int i = 0; i < rowCells.Length; i++)
                {
                    int cellWidth = rowCells[i].Value.Length;
                    columnWidths[i] = cellWidth > columnWidths[i] ? cellWidth : columnWidths[i];
                }
            }

            StringBuilder tableBuilder = new StringBuilder();
            tableBuilder.AppendLine(Environment.NewLine);

            // Build the header
            var headers = gherkinDataTable.Rows.ToArray()[0].Cells.Select(cell => cell.Value).ToArray();
            string headerSeparatorLine = string.Join("+", headers.Select((h, i) => new string('-', columnWidths[i] + 2)));
            tableBuilder.AppendLine($"+{headerSeparatorLine}+");
            tableBuilder.AppendLine("|" + string.Join("|", headers.Select((h, i) => $" {h.PadRight(columnWidths[i])} ")) + "|");

            // Build the separator line
            tableBuilder.AppendLine($"+{headerSeparatorLine}+");

            // Build each row (excluding the header)
            for (int i = 1; i < gherkinDataTable.Rows.Count(); i++)
            {
                var rowData = string.Join("|", gherkinDataTable.Rows.ToArray()[i].Cells.Select((cell, j) => $" {cell.Value.PadRight(columnWidths[j])} "));
                tableBuilder.AppendLine($"|{rowData}|");
            }

            // Add the bottom separator line
            tableBuilder.AppendLine($"+{headerSeparatorLine}+");

            return tableBuilder.ToString();
        }
    }
}
