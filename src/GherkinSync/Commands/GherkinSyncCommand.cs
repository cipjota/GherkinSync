using EnvDTE;
using Gherkin;
using Gherkin.Ast;
using GherkinSync.Options;
using GherkinSync.ToolWindows;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.Linq;
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
                var syncOptionsDialog = new SyncOptionsDialog();
                syncOptionsDialog.SyncOptions.ProjectName = GherkinSyncOptions.Instance.ProjectName;
                syncOptionsDialog.SyncOptions.DescriptionTemplate = GherkinSyncOptions.Instance.DescriptionTemplate;
                syncOptionsDialog.SyncOptions.CustomFields = GherkinSyncOptions.Instance.CustomFields;
                syncOptionsDialog.SyncOptions.RemoveCasesFromSuite = GherkinSyncOptions.Instance.RemoveTestCasesFromSuite;
                syncOptionsDialog.SyncOptions.BackgroundAsSteps = GherkinSyncOptions.Instance.BackgroundAsSteps;

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

                    await GherkinSyncOptions.Instance.SaveAsync();

                    var featureBackground = feature.Children.Where(c => c.GetType() == typeof(Background)).Cast<Background>().FirstOrDefault();
                    var featureScenarios = feature.Children.Where(c => c.GetType() == typeof(Scenario)).Cast<Scenario>();

                    var featureBackgroundSteps = featureBackground != default ? GherkinParser.StepsToList(featureBackground.Steps) : new List<string>();

                    var testCasesList = GherkinParser.ConvertToTestCases(featureScenarios, featureBackgroundSteps, feature.Name, feature.Description);

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

                        testCasesList.AddRange(GherkinParser.ConvertToTestCases(featureScenarios, featureBackgroundSteps, feature.Name, feature.Description, featureRule.Name, featureRule.Description));
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
                        twd.UpdateProgress("", "Synchronizing " + testCasesList[i].TestCaseName, "Test Case " + i + 1 + " of " + testCasesList.Count, i + 1, testCasesList.Count + 2, true, out _);

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

                        var testCasesToRemove = string.Join(",", testCasesInSuite.Select(tc => tc.workItem.Id)
                                           .Except(testCasesList.Select(tc => tc.TestCaseId))
                                           .ToList());

                        await testManagementClient.RemoveTestCasesListFromSuiteAsync(syncOptionsDialog.SyncOptions.ProjectName, syncOptionsDialog.SyncOptions.TestPlanId, syncOptionsDialog.SyncOptions.TestSuiteId, testCasesToRemove);
                    }

                    twd.UpdateProgress("", "Updating feature file", "Updating feature file", testCasesList.Count + 2, testCasesList.Count + 2, true, out _);

                    // Update the feature files

                    twd.EndWaitDialog();
                    (twd as IDisposable).Dispose();

                    GherkinSyncOptions.Instance.ProjectName = syncOptionsDialog.SyncOptions.ProjectName;
                    GherkinSyncOptions.Instance.DescriptionTemplate = syncOptionsDialog.SyncOptions.DescriptionTemplate;
                    GherkinSyncOptions.Instance.CustomFields = syncOptionsDialog.SyncOptions.CustomFields;
                    GherkinSyncOptions.Instance.RemoveTestCasesFromSuite = syncOptionsDialog.SyncOptions.RemoveCasesFromSuite;
                    GherkinSyncOptions.Instance.BackgroundAsSteps = syncOptionsDialog.SyncOptions.BackgroundAsSteps;

                    await GherkinSyncOptions.Instance.SaveAsync();
                }
                else
                {
                    await VS.MessageBox.ShowAsync("GherkinSync", "Synchronization cancelled.", Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
                }
            }
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

                // Add or edit steps

                workItem.Fields.AddOrUpdate("Title", testCase.TestCaseName, (key, value) => { return value; });

                workItem.Fields.AddOrUpdate("Description", syncOptions.DescriptionTemplate
                    .Replace("[FeatureName]", testCase.FeatureName)
                    .Replace("[FeatureDescription]", testCase.FeatureDescription)
                    .Replace("[TestCaseName]", testCase.TestCaseName)
                    .Replace("[TestCaseDescription]", testCase.TestCaseDescription)
                    .Replace("[RuleName]", testCase.RuleName)
                    .Replace("[RuleDescription]", testCase.RuleDescription)
                    .Replace("[BackgroundSteps]", "<ul>" + string.Join(Environment.NewLine, testCase.BackgroundSteps.Select(s => "<li>" + s + "</li>")) + "</ul>"),
                    (key, value) => { return value; });

                foreach (var customFiled in syncOptions.CustomFields)
                {
                    workItem.Fields.AddOrUpdate(customFiled.FieldName, customFiled.DefaultValue, (key, value) => { return value; });
                }

                var patchDocument = new JsonPatchDocument();

                foreach (var key in workItem.Fields.Keys)
                {
                    patchDocument.Add(new JsonPatchOperation()
                    {
                        Operation = Operation.Add,
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
                var msg = ex.Message;
            }


            return workItem.Id.Value;
        }
    }
}
