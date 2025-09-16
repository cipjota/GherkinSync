using EnvDTE;
using EnvDTE80;
using Gherkin.Ast;
using GherkinSync.Models;
using GherkinSync.Options;
using Microsoft.VisualStudio.Services.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GherkinSync
{
    internal static class GherkinParser
    {
        private static Dictionary<string, string> availableMethods = [];

        internal static void AvailableMethods(Window codeBehindFile)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread

            availableMethods =
                    codeBehindFile.ProjectItem.FileCodeModel.CodeElements
                        .OfType<CodeNamespace>() // Filter namespaces
                        .SelectMany(nsp => nsp.Children.OfType<CodeClass>()) // Filter classes in namespace
                        .SelectMany(c => c.Children.OfType<CodeFunction>()) // Filter methods in class
                        .SelectMany(cf => cf.Attributes
                            .OfType<CodeAttribute>()
                            .Where(attr => attr.Name.EndsWith("DescriptionAttribute")) // Match DescriptionAttribute
                            .SelectMany(attr => attr.Children
                                .OfType<CodeAttributeArgument>()
                                .Select(arg => new { arg.Value, MethodFullName = cf.FullName })
                            )
                        )
                        .ToDictionary(x => x.Value.Replace("\"", ""), x => x.MethodFullName);

#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread
        }

        internal static List<TestCase> ConvertToTestCases(IEnumerable<Scenario> scenarios, List<string> backgroundSteps, string featureName, string featureDescription, bool associateAutomation, string automatedTestStorage = "", string ruleName = "", string ruleDescription = "")
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

                var automatedTestName = associateAutomation ? availableMethods.GetValueOrDefault(scenarioName, string.Empty) : string.Empty;

                if (scenario.Examples.Any())
                {
                    if (!string.IsNullOrEmpty(automatedTestName))
                    {
                        var quotedRegex = new Regex("\"([^\"]*)\"");
                        var angleBracketRegex = new Regex("<[^>]+>");

                        var cleanMethodAttributes = scenario.Steps
                            .SelectMany(step => quotedRegex.Matches(step.Text).Cast<Match>())
                            .Select(match => match.Groups[1].Value) // Get quoted content
                            .SelectMany(quoted => angleBracketRegex.Matches(quoted).Cast<Match>()) // Extract <...> from quoted
                            .Select(m => m.Value)
                            .Distinct()
                            .ToList();

                        if (cleanMethodAttributes.Count > 0)
                        {
                            automatedTestName += "(\"" + string.Join("\",\"", cleanMethodAttributes) + "\")";
                        }
                    }

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
                            ).ToList(),
                            AutomatedTestName = DictionaryFromExample(scenarioExample.TableHeader, scenarioRows[i])
                            .Aggregate(automatedTestName, (current, kvp) => current.Replace(kvp.Key, kvp.Value)),
                            AutomatedTestStorage = associateAutomation ? automatedTestStorage : string.Empty,
                            AutomatedTestType = string.Empty,
                            AutomationStatus = associateAutomation,
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
                        Steps = StepsToList(scenario.Steps),
                        AutomatedTestName = automatedTestName,
                        AutomatedTestStorage = associateAutomation ? automatedTestStorage : string.Empty,
                        AutomatedTestType = string.Empty,
                        AutomationStatus = associateAutomation,
                    });
                }
            }

            return testCasesList;
        }

        private static string FormatStepArgument(object? argument)
        {
            return argument switch
            {
                DataTable dataTable => ConvertGherkinDataTableToAsciiTable(dataTable),
                DocString docString => Environment.NewLine + Environment.NewLine + docString.Content,
                _ => string.Empty
            };
        }

        internal static List<string> StepsToList(IEnumerable<Step> steps)
        {
            return steps.Select(step => step.Keyword + step.Text + FormatStepArgument(step.Argument)).ToList();
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

            StringBuilder tableBuilder = new();
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
