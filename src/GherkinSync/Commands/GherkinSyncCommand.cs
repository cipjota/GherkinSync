using EnvDTE;
using Gherkin;
using Gherkin.Ast;
using GherkinSync.Options;
using GherkinSync.ToolWindows;
using Microsoft.CodeAnalysis;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WorkItem = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem;

namespace GherkinSync
{
    [Command(PackageIds.GherkinSyncCommand)]
    internal sealed class GherkinSyncCommand : BaseCommand<GherkinSyncCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DTE dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            var currentFilePath = dte.ActiveDocument.FullName;

            if (currentFilePath.EndsWith(".feature"))
            {
                try
                {
                    var syncOptionsDialog = new SyncOptionsDialog();
                    syncOptionsDialog.SyncOptions.ProjectName = GherkinSyncOptions.Instance.ProjectName;
                    syncOptionsDialog.SyncOptions.DescriptionTemplate = GherkinSyncOptions.Instance.DescriptionTemplate;
                    syncOptionsDialog.SyncOptions.CustomFields = GherkinSyncOptions.Instance.CustomFields;
                    syncOptionsDialog.SyncOptions.RemoveCasesFromSuite = GherkinSyncOptions.Instance.RemoveTestCasesFromSuite;
                    syncOptionsDialog.SyncOptions.BackgroundAsSteps = GherkinSyncOptions.Instance.BackgroundAsSteps;

                    if (File.Exists(currentFilePath + ".cs"))
                    {
                        var codeBehindFile = dte.ItemOperations.OpenFile(currentFilePath + ".cs");
                        codeBehindFile.Visible = false;
                        GherkinParser.AvailableMethods(codeBehindFile);
                        syncOptionsDialog.SyncOptions.AutomatedTestStorage = codeBehindFile.Project.Properties.Cast<Property>().FirstOrDefault(x => x.Name == "AssemblyName").Value + ".dll";
                        codeBehindFile.Close();
                    }
                    else
                    {
                        syncOptionsDialog.SyncOptions.AllowAutomatedTests = false;
                    }

                    syncOptionsDialog.SyncOptions.AssociateAutomation = GherkinSyncOptions.Instance.AssociateAutomation && syncOptionsDialog.SyncOptions.AllowAutomatedTests;

                    var parser = new Parser();
                    var gherkinDocument = parser.Parse(currentFilePath);
                    var feature = gherkinDocument.Feature;

                    var testPlanReferenceTag = feature.Tags.Where(t => Regex.Match(t.Name, "@" + GherkinSyncOptions.Instance.TestPlanReferenceIdTag + "\\((.*)\\)").Success).FirstOrDefault();
                    if (testPlanReferenceTag != null)
                    {
                        var testPlanId = testPlanReferenceTag.Name.Substring(testPlanReferenceTag.Name.IndexOf("(") + 1, testPlanReferenceTag.Name.IndexOf(")") - 1 - testPlanReferenceTag.Name.IndexOf("("));
                        syncOptionsDialog.SyncOptions.TestPlanId = int.Parse(testPlanId);
                    }

                    var testSuiteReferenceTag = feature.Tags.Where(t => Regex.Match(t.Name, "@" + GherkinSyncOptions.Instance.TestSuiteReferenceIdTag + "\\((.*)\\)").Success).FirstOrDefault();
                    if (testSuiteReferenceTag != null)
                    {
                        var testSuiteId = testSuiteReferenceTag.Name.Substring(testSuiteReferenceTag.Name.IndexOf("(") + 1, testSuiteReferenceTag.Name.IndexOf(")") - 1 - testSuiteReferenceTag.Name.IndexOf("("));
                        syncOptionsDialog.SyncOptions.TestSuiteId = int.Parse(testSuiteId);
                    }

                    if (syncOptionsDialog.ShowDialog().Value == true)
                    {
                        if (GherkinSyncOptions.Instance.ProjectName.Length == 0)
                        { GherkinSyncOptions.Instance.ProjectName = syncOptionsDialog.SyncOptions.ProjectName; }

                        if (GherkinSyncOptions.Instance.ProjectName.Length == 0)
                        { GherkinSyncOptions.Instance.DescriptionTemplate = syncOptionsDialog.SyncOptions.DescriptionTemplate; }

                        GherkinSyncOptions.Instance.AssociateAutomation = syncOptionsDialog.SyncOptions.AssociateAutomation;

                        await GherkinSyncOptions.Instance.SaveAsync();

                        var featureBackground = feature.Children.Where(c => c.GetType() == typeof(Background)).Cast<Background>().FirstOrDefault();
                        var featureScenarios = feature.Children.Where(c => c.GetType() == typeof(Scenario)).Cast<Scenario>();

                        var featureBackgroundSteps = featureBackground != default ? GherkinParser.StepsToList(featureBackground.Steps) : [];

                        var testCasesList = GherkinParser.ConvertToTestCases(featureScenarios, featureBackgroundSteps, feature.Name, feature.Description, syncOptionsDialog.SyncOptions.AssociateAutomation, syncOptionsDialog.SyncOptions.AutomatedTestStorage);

                        var featureRules = feature.Children.Where(c => c.GetType() == typeof(Rule)).Cast<Rule>();

                        foreach (var featureRule in featureRules)
                        {
                            var ruleBackground = featureRule.Children.Where(c => c.GetType() == typeof(Background)).Cast<Background>().FirstOrDefault();

                            // It is not supposed to exist rule background when there is a feature level background.
                            // If there are steps for feature backround we will ignore existing rule background steps.
                            var ruleBackgroundSteps = featureBackgroundSteps.Count > 0 ?
                                featureBackgroundSteps :
                                ruleBackground != default ?
                                GherkinParser.StepsToList(featureBackground.Steps) :
                                [];

                            var ruleScenarios = featureRule.Children.Where(c => c.GetType() == typeof(Scenario)).Cast<Scenario>();

                            testCasesList.AddRange(GherkinParser.ConvertToTestCases(featureScenarios, featureBackgroundSteps, feature.Name, feature.Description, syncOptionsDialog.SyncOptions.AssociateAutomation, syncOptionsDialog.SyncOptions.AutomatedTestStorage, featureRule.Name, featureRule.Description));
                        }

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        var fac = (IVsThreadedWaitDialogFactory)await VS.Services.GetThreadedWaitDialogAsync();
                        IVsThreadedWaitDialog4 twd = fac.CreateInstance();

                        twd.StartWaitDialog("GherkinSync", "Synchronizing test cases", "", null, "", 1, true, true);

                        using var connection = new VssConnection(new Uri(GherkinSyncOptions.Instance.AzureDevopsBaseUrl), new VssBasicCredential("", GherkinSyncOptions.Instance.PatToken));
                        var testManagementClient = connection.GetClient<TestPlanHttpClient>();
                        var testCasesInSuite = await testManagementClient.GetTestCaseListAsync(syncOptionsDialog.SyncOptions.ProjectName, syncOptionsDialog.SyncOptions.TestPlanId, syncOptionsDialog.SyncOptions.TestSuiteId);

                        for (int i = 0; i < testCasesList.Count; i++)
                        {
                            twd.UpdateProgress("", "Synchronizing " + testCasesList[i].TestCaseName, "Test Case " + (i + 1) + " of " + testCasesList.Count, i + 1, testCasesList.Count + 2, true, out _);

                            testCasesList[i].TestCaseId = await CreateOrUpdateTestCaseAsync(testCasesList[i], syncOptionsDialog.SyncOptions);

                            if (!testCasesInSuite.Where(t => t.workItem.Id == testCasesList[i].TestCaseId).Any())
                            {
                                var suiteTestCaseCreateUpdateParameters = new List<SuiteTestCaseCreateUpdateParameters>
                            {
                                new() { workItem = new Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.WorkItem() { Id = testCasesList[i].TestCaseId } }
                            };

                                _ = await testManagementClient.AddTestCasesToSuiteAsync(suiteTestCaseCreateUpdateParameters, syncOptionsDialog.SyncOptions.ProjectName, syncOptionsDialog.SyncOptions.TestPlanId, syncOptionsDialog.SyncOptions.TestSuiteId);
                            }
                        }

                        if (syncOptionsDialog.SyncOptions.RemoveCasesFromSuite)
                        {
                            twd.UpdateProgress("", "Removing test cases from suite", "Removing test cases from suite", testCasesList.Count + 1, testCasesList.Count + 2, true, out _);

                            var testCasesToRemove = testCasesInSuite
                                .Select(tc => tc.workItem.Id)
                                .Except(testCasesList.Select(tc => tc.TestCaseId))
                                .ToList();

                            if (testCasesToRemove.Any())
                            {
                                await testManagementClient.RemoveTestCasesListFromSuiteAsync(syncOptionsDialog.SyncOptions.ProjectName, syncOptionsDialog.SyncOptions.TestPlanId, syncOptionsDialog.SyncOptions.TestSuiteId, string.Join(",", testCasesToRemove));
                            }
                        }

                        twd.UpdateProgress("", "Updating feature file", "Updating feature file", testCasesList.Count + 2, testCasesList.Count + 2, true, out _);

                        var featureFileLines = File.ReadAllLines(currentFilePath).ToList();

                        var groupedTestCaseIds = testCasesList
                            .Select(s => new { s.ReferenceTagLine, s.TestCaseFirstLine, s.TestCaseId, TestCaseTagLine = (s.ReferenceTagExists ? s.ReferenceTagLine : s.TestCaseFirstLine - 1) })
                            .GroupBy(tc => tc.TestCaseTagLine)
                            .Select(g => new { TestCaseTagLine = g.Key, TestCaseIds = string.Join(",", g.Select(tc => tc.TestCaseId.ToString())) })
                            .ToList().OrderByDescending(o => o.TestCaseTagLine);

                        foreach (var groupedTestCaseId in groupedTestCaseIds)
                        {
                            if (featureFileLines[groupedTestCaseId.TestCaseTagLine].Contains(GherkinSyncOptions.Instance.TestCaseReferenceIdTag))
                            {
                                var replacementLine = UpdateReferenceTagLine(featureFileLines[groupedTestCaseId.TestCaseTagLine], GherkinSyncOptions.Instance.TestCaseReferenceIdTag, groupedTestCaseId.TestCaseIds);
                                featureFileLines.RemoveAt(groupedTestCaseId.TestCaseTagLine);
                                featureFileLines.Insert(groupedTestCaseId.TestCaseTagLine, replacementLine);
                            }
                            else
                            { featureFileLines.Insert(groupedTestCaseId.TestCaseTagLine, "@" + GherkinSyncOptions.Instance.TestCaseReferenceIdTag + "(" + groupedTestCaseId.TestCaseIds + ")"); }
                        }

                        if (testSuiteReferenceTag != null)
                        {
                            var replacementLine = UpdateReferenceTagLine(featureFileLines[testSuiteReferenceTag.Location.Line - 1], GherkinSyncOptions.Instance.TestSuiteReferenceIdTag, syncOptionsDialog.SyncOptions.TestSuiteId.ToString());
                            featureFileLines.RemoveAt(testSuiteReferenceTag.Location.Line - 1);
                            featureFileLines.Insert(testSuiteReferenceTag.Location.Line - 1, replacementLine);
                        }
                        else
                        { featureFileLines.Insert(gherkinDocument.Feature.Location.Line - 1, "@" + GherkinSyncOptions.Instance.TestSuiteReferenceIdTag + "(" + syncOptionsDialog.SyncOptions.TestSuiteId + ")"); }

                        if (testPlanReferenceTag != null)
                        {
                            var replacementLine = UpdateReferenceTagLine(featureFileLines[testPlanReferenceTag.Location.Line - 1], GherkinSyncOptions.Instance.TestPlanReferenceIdTag, syncOptionsDialog.SyncOptions.TestPlanId.ToString());
                            featureFileLines.RemoveAt(testPlanReferenceTag.Location.Line - 1);
                            featureFileLines.Insert(testPlanReferenceTag.Location.Line - 1, replacementLine);
                        }
                        else
                        { featureFileLines.Insert(gherkinDocument.Feature.Location.Line - 1, "@" + GherkinSyncOptions.Instance.TestPlanReferenceIdTag + "(" + syncOptionsDialog.SyncOptions.TestPlanId + ")"); }

                        File.WriteAllLines(currentFilePath, featureFileLines);

                        twd.EndWaitDialog();
                        (twd as IDisposable).Dispose();

                        GherkinSyncOptions.Instance.ProjectName = syncOptionsDialog.SyncOptions.ProjectName;
                        GherkinSyncOptions.Instance.DescriptionTemplate = syncOptionsDialog.SyncOptions.DescriptionTemplate;
                        GherkinSyncOptions.Instance.CustomFields = syncOptionsDialog.SyncOptions.CustomFields;
                        GherkinSyncOptions.Instance.RemoveTestCasesFromSuite = syncOptionsDialog.SyncOptions.RemoveCasesFromSuite;
                        GherkinSyncOptions.Instance.BackgroundAsSteps = syncOptionsDialog.SyncOptions.BackgroundAsSteps;

                        await GherkinSyncOptions.Instance.SaveAsync();

                        await VS.MessageBox.ShowAsync("GherkinSync", "Synchronization complete.", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
                    }
                    else
                    {
                        await VS.MessageBox.ShowAsync("GherkinSync", "Synchronization cancelled.", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
                    }

                }
                catch (Exception ex)
                {
                    await VS.MessageBox.ShowAsync("GherkinSync", ex.Message, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
                }
            }
        }

        private string UpdateReferenceTagLine(string currentLine, string tagPattern, string newTagValue)
        {
            string pattern = $"{ParseRegexString("@" + tagPattern + "(")}[\\d,]*{ParseRegexString(")")}";
            var returnString = Regex.Replace(currentLine, pattern, $"{"@" + tagPattern + "("}{newTagValue}{")"}");

            return returnString;
        }

        private string ParseRegexString(string input)
        {
            input = input.Replace(@"\", @"\\").Replace("^", @"\^");
            input = input.Replace("(", @"\(").Replace(")", @"\)");
            input = input.Replace(@"$", @"\$");

            return input;
        }

        public async Task<int> CreateOrUpdateTestCaseAsync(Models.TestCase testCase, SyncOptionsDialogViewModel syncOptions)
        {
            WorkItem workItem = null;

            using var connection = new VssConnection(new Uri(GherkinSyncOptions.Instance.AzureDevopsBaseUrl), new VssBasicCredential("", GherkinSyncOptions.Instance.PatToken));

            try
            {
                var workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();

                if (testCase.TestCaseId > 0)
                {
                    workItem = await workItemClient.GetWorkItemAsync(testCase.TestCaseId, null, null, WorkItemExpand.None);

                    workItem ??= new WorkItem();
                }
                else
                {
                    workItem = new WorkItem();
                }

                var existingFields = workItem.Fields.Keys.ToList();

                workItem.Fields.AddOrUpdate("System.Title", testCase.TestCaseName, (key, value) => { return value; });

                string[] splitString = { Environment.NewLine };

                workItem.Fields.AddOrUpdate("System.Description",
                    string.Join("", syncOptions.DescriptionTemplate
                    .Replace("[FeatureName]", testCase.FeatureName)
                    .Replace("[FeatureDescription]", testCase.FeatureDescription)
                    .Replace("[TestCaseName]", testCase.TestCaseName)
                    .Replace("[TestCaseDescription]", testCase.TestCaseDescription)
                    .Replace("[RuleName]", testCase.RuleName)
                    .Replace("[RuleDescription]", testCase.RuleDescription)
                    .Replace("[BackgroundSteps]", string.Join(Environment.NewLine, testCase.BackgroundSteps.Select(s => string.Join("", s))))
                    .Split(splitString, StringSplitOptions.None).Select(r => "<div>" + r.Replace(" ", "&nbsp;") + "</div>"))
                    , (key, value) => { return value; });

                workItem.Fields.AddOrUpdate("Microsoft.VSTS.TCM.Steps", ConvertTestStepsToStepsXml(syncOptions.BackgroundAsSteps ? testCase.BackgroundSteps.Union(testCase.Steps).ToList() : testCase.Steps), (key, value) => { return value; });

                foreach (var customFiled in syncOptions.CustomFields)
                {
                    workItem.Fields.AddOrUpdate(customFiled.FieldName, customFiled.DefaultValue, (key, value) => { return value; });
                }

                var automatedTestId = testCase.AutomationStatus ?
                    existingFields.Contains("Microsoft.VSTS.TCM.AutomatedTestId") ?
                        workItem.Fields["Microsoft.VSTS.TCM.AutomatedTestId"] :
                        Guid.NewGuid().ToString() :
                    string.Empty;

                workItem.Fields.AddOrUpdate("Microsoft.VSTS.TCM.AutomatedTestName", testCase.AutomatedTestName, (key, value) => value);
                workItem.Fields.AddOrUpdate("Microsoft.VSTS.TCM.AutomatedTestStorage", testCase.AutomatedTestStorage, (key, value) => value);
                workItem.Fields.AddOrUpdate("Microsoft.VSTS.TCM.AutomatedTestType", testCase.AutomatedTestType, (key, value) => value);
                workItem.Fields.AddOrUpdate("Microsoft.VSTS.TCM.AutomatedTestId", automatedTestId, (key, value) => value);

                var patchDocument = new JsonPatchDocument();

                foreach (var key in workItem.Fields.Keys)
                {
                    patchDocument.Add(new JsonPatchOperation()
                    {
                        Operation = existingFields.Contains(key) ? Operation.Replace : Operation.Add,
                        Path = "/fields/" + key,
                        Value = workItem.Fields[key]
                    });
                }

                if (testCase.TestCaseId > 0)
                {
                    workItem = await workItemClient.UpdateWorkItemAsync(patchDocument, testCase.TestCaseId);
                }
                else
                {
                    workItem = await workItemClient.CreateWorkItemAsync(patchDocument, syncOptions.ProjectName, "Test Case");
                }
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowAsync("GherkinSync", ex.Message, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }

            return workItem.Id.Value;
        }

        private string ConvertTestStepsToStepsXml(List<string> testSteps)
        {
            var sb = new StringBuilder();
            sb.Append("<steps id=\"0\" last=\"" + (testSteps.Count + 1) + "\">");

            for (int i = 0; i < testSteps.Count; i++)
            {
                sb.Append($"<step id=\"{i + 2}\" type=\"ActionStep\">");
                sb.Append($"<parameterizedString isformatted=\"true\">{EscapeStepHtml("<DIV><DIV><P>" + testSteps[i] + "<BR/></P></DIV></DIV>")}</parameterizedString>");
                sb.Append($"<parameterizedString isformatted=\"true\">{EscapeStepHtml("<DIV><P><BR/></P></DIV>")}</parameterizedString>");
                sb.Append("<description/></step>");
            }

            sb.Append("</steps>");
            return sb.ToString();
        }

        private string EscapeStepHtml(string input)
        {
            // We are only modifying the html tags that mess with the API call. All other tags should remain as is.
            return input

                //Revert existing HTML
                .Replace("&apos;", "'")
                .Replace("&gt;", ">")
                .Replace("&lt;", "<")
                .Replace("&amp;", "&")

                //Escape HTML
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("'", "&apos;");
        }

        private string ConvertToPascalCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var words = Regex.Matches(input, @"\w+")
                             .Cast<Match>()
                             .Select(m => char.ToUpper(m.Value[0]) + m.Value.Substring(1));

            return string.Join("", words);
        }
    }
}
