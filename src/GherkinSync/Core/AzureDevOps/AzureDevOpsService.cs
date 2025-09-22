using GherkinSync.Options;
using GherkinSync.ToolWindows;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkItem = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem;

namespace GherkinSync.Core.AzureDevOps
{
    public class AzureDevOpsService : IDisposable
    {
        private readonly WorkItemTrackingHttpClient _workItemClient;
        private readonly TestPlanHttpClient _testPlanClient;
        private readonly VssConnection _connection;
        private readonly SyncOptionsDialogViewModel _options;

        public AzureDevOpsService(SyncOptionsDialogViewModel options)
        {
            _options = options;

            _connection = new VssConnection(
                new Uri(GherkinSyncOptions.Instance.AzureDevopsBaseUrl),
                new VssBasicCredential("", GherkinSyncOptions.Instance.PatToken));

            _workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();
            _testPlanClient = _connection.GetClient<TestPlanHttpClient>();
        }

        /// <summary>
        /// Gets all test cases currently in a test suite.
        /// </summary>
        public async Task<IReadOnlyList<TestCase>> GetTestCasesInSuiteAsync()
        {
            return (IReadOnlyList<TestCase>)await _testPlanClient.GetTestCaseListAsync(
                _options.ProjectName,
                _options.TestPlanId,
                _options.TestSuiteId);
        }

        /// <summary>
        /// Creates or updates a test case in Azure DevOps.
        /// Returns the ID of the created/updated work item.
        /// </summary>
        public async Task<int> CreateOrUpdateTestCaseAsync(Models.TestCase testCase)
        {
            WorkItem? workItem = null;

            try
            {
                if (testCase.TestCaseId > 0)
                {
                    workItem = await _workItemClient.GetWorkItemAsync(testCase.TestCaseId);
                }

                // New work item if none exists
                workItem ??= new WorkItem();

                var existingFields = workItem.Fields?.Keys?.ToList() ?? new List<string>();
                var patchDocument = new JsonPatchDocument();

                void AddOrReplace(string field, object value)
                {
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = existingFields.Contains(field) ? Operation.Replace : Operation.Add,
                        Path = $"/fields/{field}",
                        Value = value
                    });
                }

                // Core fields
                AddOrReplace(AzureDevOpsFields.Title, testCase.TestCaseName);
                AddOrReplace(AzureDevOpsFields.Description, BuildDescription(testCase));

                // Steps
                var stepsXml = ConvertTestStepsToStepsXml(
                    _options.BackgroundAsSteps ? testCase.BackgroundSteps.Concat(testCase.Steps).ToList()
                                               : testCase.Steps);
                AddOrReplace(AzureDevOpsFields.Steps, stepsXml);

                // Custom fields
                foreach (var field in _options.CustomFields)
                {
                    AddOrReplace(field.FieldName, field.DefaultValue);
                }

                // Automation fields
                if (testCase.AutomationStatus)
                {
                    var automatedTestId = existingFields.Contains(AzureDevOpsFields.AutomatedTestId)
                        ? workItem.Fields[AzureDevOpsFields.AutomatedTestId]
                        : Guid.NewGuid().ToString();

                    AddOrReplace(AzureDevOpsFields.AutomatedTestId, automatedTestId);
                    AddOrReplace(AzureDevOpsFields.AutomatedTestName, testCase.AutomatedTestName);
                    AddOrReplace(AzureDevOpsFields.AutomatedTestStorage, testCase.AutomatedTestStorage);
                    AddOrReplace(AzureDevOpsFields.AutomatedTestType, testCase.AutomatedTestType);
                }

                // Save work item
                if (testCase.TestCaseId > 0)
                {
                    workItem = await _workItemClient.UpdateWorkItemAsync(patchDocument, testCase.TestCaseId);
                }
                else
                {
                    workItem = await _workItemClient.CreateWorkItemAsync(patchDocument, _options.ProjectName, "Test Case");
                }
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowAsync("AzureDevOps", ex.Message,
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }

            return workItem?.Id ?? -1;
        }

        /// <summary>
        /// Adds test cases to the suite if not already present.
        /// </summary>
        public async Task AddTestCasesToSuiteAsync(IEnumerable<int> testCaseIds)
        {
            var parameters = testCaseIds.Select(id => new SuiteTestCaseCreateUpdateParameters
            {
                workItem = new Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.WorkItem { Id = id }
            }).ToList();

            if (parameters.Any())
            {
                await _testPlanClient.AddTestCasesToSuiteAsync(parameters,
                    _options.ProjectName, _options.TestPlanId, _options.TestSuiteId);
            }
        }

        /// <summary>
        /// Removes obsolete test cases from the suite.
        /// </summary>
        public async Task RemoveTestCasesFromSuiteAsync(IEnumerable<string> testCaseIds)
        {
            if (testCaseIds.Any())
            {
                await _testPlanClient.RemoveTestCasesListFromSuiteAsync(
                    _options.ProjectName,
                    _options.TestPlanId,
                    _options.TestSuiteId,
                    string.Join(",", testCaseIds));
            }
        }

        private string BuildDescription(Models.TestCase testCase)
        {
            var desc = _options.DescriptionTemplate
                .Replace("[FeatureName]", testCase.FeatureName)
                .Replace("[FeatureDescription]", testCase.FeatureDescription)
                .Replace("[TestCaseName]", testCase.TestCaseName)
                .Replace("[TestCaseDescription]", testCase.TestCaseDescription)
                .Replace("[RuleName]", testCase.RuleName)
                .Replace("[RuleDescription]", testCase.RuleDescription)
                .Replace("[BackgroundSteps]", string.Join(Environment.NewLine, testCase.BackgroundSteps));

            var lines = desc.Split([Environment.NewLine], StringSplitOptions.None);
            return string.Join("", lines.Select(line => $"<div>{line.Replace(" ", "&nbsp;")}</div>"));
        }

        private string ConvertTestStepsToStepsXml(List<string> testSteps)
        {
            var sb = new StringBuilder();
            sb.Append($"<steps id=\"0\" last=\"{testSteps.Count + 1}\">");

            for (int i = 0; i < testSteps.Count; i++)
            {
                sb.Append($"<step id=\"{i + 2}\" type=\"ActionStep\">");
                sb.Append($"<parameterizedString isformatted=\"true\">{EscapeStepHtml($"<DIV><DIV><P>{testSteps[i]}<BR/></P></DIV></DIV>")}</parameterizedString>");
                sb.Append($"<parameterizedString isformatted=\"true\">{EscapeStepHtml("<DIV><P><BR/></P></DIV>")}</parameterizedString>");
                sb.Append("<description/></step>");
            }

            sb.Append("</steps>");
            return sb.ToString();
        }

        private string EscapeStepHtml(string input)
        {
            return input
                .Replace("&apos;", "'")
                .Replace("&gt;", ">")
                .Replace("&lt;", "<")
                .Replace("&amp;", "&")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("'", "&apos;");
        }

        public void Dispose()
        {
            _workItemClient?.Dispose();
            _testPlanClient?.Dispose();
            _connection?.Dispose();
        }
    }
}
