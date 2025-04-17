using GherkinSync.Models;
using System.Collections.Generic;
using System.ComponentModel;

namespace GherkinSync.ToolWindows
{
    public class SyncOptionsDialogViewModel : INotifyPropertyChanged
    {
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

        private string _testPlanId = string.Empty;
        public string TestPlanId
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

        private string _testSuiteId = string.Empty;
        public string TestSuiteId
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

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
