using EnvDTE;
using Gherkin;
using Gherkin.Ast;
using GherkinSync.Models;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace GherkinSync
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class GherkinSyncCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("1d25768b-a9ba-4966-b9af-c103c63e34de");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage _package;

        private GherkinSyncPackage _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="GherkinSyncCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private GherkinSyncCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _options = _package as GherkinSyncPackage;
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static GherkinSyncCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this._package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in GherkinSyncCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new GherkinSyncCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            var currentFilePath = dte.ActiveDocument.FullName;

            if (currentFilePath.EndsWith(".feature"))
            {
                var parser = new Parser();
                var gherkinDocument = parser.Parse(currentFilePath);

                var feature = gherkinDocument.Feature;

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
                        new List<string>();

                    var ruleScenarios = featureRule.Children.Where(c => c.GetType() == typeof(Scenario)).Cast<Scenario>();

                    testCasesList.AddRange(GherkinParser.ConvertToTestCases(featureScenarios, featureBackgroundSteps, feature.Name, feature.Description, featureRule.Name, featureRule.Description));
                }


                foreach (var testCase in testCasesList)
                {
                    _ = CreateOrUpdateTestCaseAsync(testCase, 0, 0).Result;
                }
            }
        }

        public async Task<int> CreateOrUpdateTestCaseAsync(TestCase testCase, int testPlanId, int testSuiteId)
        {
            using (var connection = new VssConnection(new Uri(_options.AzureDevopsBaseUrl), new VssBasicCredential("", _options.PatToken)))
            {
                var witClient = connection.GetClient<WorkItemTrackingHttpClient>();

                WorkItem workItem = null;

                if (testCase.TestCaseId > 0)
                {
                    workItem = await witClient.GetWorkItemAsync(testCase.TestCaseId);

                    if (workItem == null)
                    {
                        workItem = new WorkItem();
                    }
                }
                else
                {
                    workItem = new WorkItem();
                }

                workItem.Fields["Title"] = testCase.TestCaseName;

            }

            return testCase.TestCaseId;
        }
    }
}
