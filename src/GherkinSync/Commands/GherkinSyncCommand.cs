using EnvDTE;
using Gherkin;
using Gherkin.Ast;
using GherkinSync.Core.AzureDevOps;
using GherkinSync.Core.Gherkin;
using GherkinSync.Options;
using GherkinSync.ToolWindows;
using Microsoft.VisualStudio.Shell.Interop;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GherkinSync
{
    [Command(PackageIds.GherkinSyncCommand)]
    internal sealed class GherkinSyncCommand : BaseCommand<GherkinSyncCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            var currentFilePath = dte.ActiveDocument.FullName;

            if (!currentFilePath.EndsWith(".feature"))
            {
                return;
            }

            try
            {
                var syncOptionsDialog = new SyncOptionsDialog();
                InitializeDialogFromSettings(syncOptionsDialog, currentFilePath, dte);

                var parser = new Parser();
                var gherkinDocument = parser.Parse(currentFilePath);
                ExtractPlanAndSuiteIds(syncOptionsDialog, gherkinDocument);

                if (syncOptionsDialog.ShowDialog().Value != true)
                {
                    await VS.MessageBox.ShowAsync("GherkinSync", "Synchronization cancelled.",
                        OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK);
                    return;
                }

                await SaveOptionsAsync(syncOptionsDialog);

                var feature = gherkinDocument.Feature;
                var backgroundSteps = feature.Children.OfType<Background>().FirstOrDefault() is Background bg
                    ? GherkinParser.StepsToList(bg.Steps)
                    : [];

                var scenarios = feature.Children.OfType<Scenario>();
                var testCases = GherkinParser.ConvertToTestCases(scenarios, backgroundSteps, feature.Name,
                    feature.Description, syncOptionsDialog.SyncOptions.AssociateAutomation,
                    syncOptionsDialog.SyncOptions.AutomatedTestStorage);

                foreach (var rule in feature.Children.OfType<Rule>())
                {
                    var ruleBgSteps = backgroundSteps.Count > 0
                        ? backgroundSteps
                        : rule.Children.OfType<Background>().FirstOrDefault() is Background ruleBg
                            ? GherkinParser.StepsToList(ruleBg.Steps)
                            : [];

                    var ruleScenarios = rule.Children.OfType<Scenario>();
                    testCases.AddRange(GherkinParser.ConvertToTestCases(ruleScenarios, ruleBgSteps,
                        feature.Name, feature.Description,
                        syncOptionsDialog.SyncOptions.AssociateAutomation,
                        syncOptionsDialog.SyncOptions.AutomatedTestStorage,
                        rule.Name, rule.Description));
                }

                var ado = new AzureDevOpsService(syncOptionsDialog.SyncOptions);
                var existingCases = await ado.GetTestCasesInSuiteAsync();

                foreach (var tc in testCases)
                {
                    tc.TestCaseId = await ado.CreateOrUpdateTestCaseAsync(tc);

                    if (!existingCases.Any(x => x.workItem.Id == tc.TestCaseId))
                    {
                        await ado.AddTestCasesToSuiteAsync([tc.TestCaseId]);
                    }
                }

                if (syncOptionsDialog.SyncOptions.RemoveCasesFromSuite)
                {
                    var obsoleteIds = existingCases
                        .Select(tc => tc.workItem.Id.ToString())
                        .Except(testCases.Select(tc => tc.TestCaseId.ToString()));
                    await ado.RemoveTestCasesFromSuiteAsync(obsoleteIds);
                }

                FeatureFileUpdater.Update(currentFilePath, testCases, feature, syncOptionsDialog.SyncOptions);

                await SaveOptionsAsync(syncOptionsDialog);

                await VS.MessageBox.ShowAsync("GherkinSync", "Synchronization complete.",
                    OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowAsync("GherkinSync", ex.Message,
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }
        }

        private void InitializeDialogFromSettings(SyncOptionsDialog dialog, string currentFilePath, DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dialog.SyncOptions.ProjectName = GherkinSyncOptions.Instance.ProjectName;
            dialog.SyncOptions.DescriptionTemplate = GherkinSyncOptions.Instance.DescriptionTemplate;
            dialog.SyncOptions.CustomFields = GherkinSyncOptions.Instance.CustomFields;
            dialog.SyncOptions.RemoveCasesFromSuite = GherkinSyncOptions.Instance.RemoveTestCasesFromSuite;
            dialog.SyncOptions.BackgroundAsSteps = GherkinSyncOptions.Instance.BackgroundAsSteps;

            if (File.Exists(currentFilePath + ".cs"))
            {
                var codeBehind = dte.ItemOperations.OpenFile(currentFilePath + ".cs");
                codeBehind.Visible = false;

                GherkinParser.AvailableMethods(codeBehind);
                dialog.SyncOptions.AutomatedTestStorage = codeBehind.Project.Properties
                    .Cast<Property>()
                    .FirstOrDefault(p =>
                    {
                        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                        return p.Name == "AssemblyName";
                    })?.Value + ".dll";

                codeBehind.Close();
            }
            else
            {
                dialog.SyncOptions.AllowAutomatedTests = false;
            }

            dialog.SyncOptions.AssociateAutomation =
                GherkinSyncOptions.Instance.AssociateAutomation && dialog.SyncOptions.AllowAutomatedTests;
        }

        private void ExtractPlanAndSuiteIds(SyncOptionsDialog dialog, GherkinDocument doc)
        {
            var feature = doc.Feature;

            var planTag = feature.Tags.FirstOrDefault(t =>
                Regex.IsMatch(t.Name, $"@{GherkinSyncOptions.Instance.TestPlanReferenceIdTag}\\((.*)\\)"));
            if (planTag != null)
            {
                dialog.SyncOptions.TestPlanId = int.Parse(Regex.Match(planTag.Name, "\\((.*)\\)").Groups[1].Value);
            }

            var suiteTag = feature.Tags.FirstOrDefault(t =>
                Regex.IsMatch(t.Name, $"@{GherkinSyncOptions.Instance.TestSuiteReferenceIdTag}\\((.*)\\)"));
            if (suiteTag != null)
            {
                dialog.SyncOptions.TestSuiteId = int.Parse(Regex.Match(suiteTag.Name, "\\((.*)\\)").Groups[1].Value);
            }
        }

        private async Task SaveOptionsAsync(SyncOptionsDialog dialog)
        {
            GherkinSyncOptions.Instance.ProjectName = dialog.SyncOptions.ProjectName;
            GherkinSyncOptions.Instance.DescriptionTemplate = dialog.SyncOptions.DescriptionTemplate;
            GherkinSyncOptions.Instance.CustomFields = dialog.SyncOptions.CustomFields;
            GherkinSyncOptions.Instance.RemoveTestCasesFromSuite = dialog.SyncOptions.RemoveCasesFromSuite;
            GherkinSyncOptions.Instance.BackgroundAsSteps = dialog.SyncOptions.BackgroundAsSteps;
            GherkinSyncOptions.Instance.AssociateAutomation = dialog.SyncOptions.AssociateAutomation;
            await GherkinSyncOptions.Instance.SaveAsync();
        }
    }
}
