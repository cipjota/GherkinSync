using GherkinSync.Models;
using GherkinSync.Options;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace GherkinSync.ToolWindows
{
    public class SyncOptionsDialogViewModel : INotifyPropertyChanged
    {
        public DelegateCommand OkCommand { get; }
        public DelegateCommand CancelCommand { get; }

        public DelegateCommand LoadedCommand { get; }

        #region Properties
        private string _projectName = string.Empty;
        public string ProjectName
        {
            get => _projectName;
            set
            {
                if (_projectName != value)
                {
                    _projectName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProjectName)));
                }
            }
        }

        private int _testPlanId = 0;
        public int TestPlanId
        {
            get => _testPlanId;
            set
            {
                if (_testPlanId != value)
                {
                    _testPlanId = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestPlanId)));
                }
            }
        }

        private string _testPlanName = string.Empty;
        public string TestPlanName
        {
            get => _testPlanName;
            set
            {
                if (_testPlanName != value)
                {
                    _testPlanName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestPlanName)));
                }
            }
        }

        private int _testSuiteId = 0;
        public int TestSuiteId
        {
            get => _testSuiteId;
            set
            {
                if (_testSuiteId != value)
                {
                    _testSuiteId = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestSuiteId)));
                }
            }
        }

        private string _testSuiteName = string.Empty;
        public string TestSuiteName
        {
            get => _testSuiteName;
            set
            {
                if (_testSuiteName != value)
                {
                    _testSuiteName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestSuiteName)));
                }
            }
        }

        private string _descriptionTemplate = string.Empty;
        public string DescriptionTemplate
        {
            get => _descriptionTemplate;
            set
            {
                if (_descriptionTemplate != value)
                {
                    _descriptionTemplate = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionTemplate)));
                }
            }
        }

        private List<CustomField> _customFields = [];
        public List<CustomField> CustomFields
        {
            get => _customFields;
            set
            {
                if (_customFields != value)
                {
                    _customFields = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomFields)));
                }
            }
        }

        public bool IsTestPlanIdEnabled { get { return TestPlanId > 0; } }

        public bool IsTestSuiteIdEnabled { get { return TestSuiteId > 0; } }

        #endregion Properties

        public SyncOptionsDialogViewModel()
        {
            OkCommand = new DelegateCommand(OnOkCommandExecute);
            CancelCommand = new DelegateCommand(OnCancelCommandExecute);
            LoadedCommand = new DelegateCommand(OnLoadedCommandExecute);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnOkCommandExecute(object parameter)
        {
            Window wnd = parameter as Window;
            wnd.DialogResult = true;
            wnd.Close();
        }

        private void OnCancelCommandExecute(object parameter)
        {
            Window wnd = parameter as Window;
            wnd.DialogResult = false;
            wnd.Close();
        }

        private async void OnLoadedCommandExecute()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var fac = (IVsThreadedWaitDialogFactory)await VS.Services.GetThreadedWaitDialogAsync();
            IVsThreadedWaitDialog4 twd = fac.CreateInstance();

            twd.StartWaitDialog("GherkinSync", "Obtaining data...", "", null, "", 1, true, true);
            twd.UpdateProgress("", "Obtaining test plan", "Obtaining test plan", 1, 2, true, out _);
            await GetTestPlanInfoAsync();
            twd.UpdateProgress("", "Obtaining test suite", "Obtaining test suite", 2, 2, true, out _);
            await GetTestSuiteInfoAsync();

            twd.EndWaitDialog();
            (twd as IDisposable).Dispose();
        }

        private async Task GetTestPlanInfoAsync()
        {
            try
            {
                using var connection = new VssConnection(new Uri(GherkinSyncOptions.Instance.AzureDevopsBaseUrl), new VssBasicCredential("", GherkinSyncOptions.Instance.PatToken));

                var testManagementClient = connection.GetClient<TestPlanHttpClient>();

                if (TestPlanId > 0)
                {
                    var testPlan = await testManagementClient.GetTestPlanByIdAsync(ProjectName, TestPlanId);
                    if (testPlan != null)
                    {
                        TestPlanName = testPlan.Name;

                        if (TestSuiteId > 0)
                        {
                            var testSuite = await testManagementClient.GetTestSuiteByIdAsync(ProjectName, TestPlanId, TestSuiteId);
                            if (testSuite != null)
                            {
                                TestSuiteName = testSuite.Name;
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowAsync("GherkinSync: Error", ex.Message, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }
        }

        private async Task GetTestSuiteInfoAsync()
        {
            try
            {
                using var connection = new VssConnection(new Uri(GherkinSyncOptions.Instance.AzureDevopsBaseUrl), new VssBasicCredential("", GherkinSyncOptions.Instance.PatToken));

                var testManagementClient = connection.GetClient<TestPlanHttpClient>();

                if (TestPlanId > 0 && TestSuiteId > 0)
                {
                    var testSuite = await testManagementClient.GetTestSuiteByIdAsync(ProjectName, TestPlanId, TestSuiteId);
                    if (testSuite != null)
                    {
                        TestSuiteName = testSuite.Name;
                    }

                }
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowAsync("GherkinSync: Error", ex.Message, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }
        }

        private async Task CreateOrUpdateTestPlan()
        {
            using var connection = new VssConnection(new Uri(GherkinSyncOptions.Instance.AzureDevopsBaseUrl), new VssBasicCredential("", GherkinSyncOptions.Instance.PatToken));

        }


    }
}
