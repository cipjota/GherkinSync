using Microsoft.VisualStudio.PlatformUI;
using System.Windows;

namespace GherkinSync.ToolWindows
{
    public partial class SyncOptionsDialog : DialogWindow
    {
        public SyncOptionsDialog()
        {
            InitializeComponent();

            // Work around for bug https://github.com/microsoft/XamlBehaviorsWpf/issues/86
            var _ = new Microsoft.Xaml.Behaviors.DefaultTriggerAttribute(typeof(Trigger), typeof(Microsoft.Xaml.Behaviors.TriggerBase), null);
        }

        public SyncOptionsDialogViewModel SyncOptions { get; private set; } = new SyncOptionsDialogViewModel();
    }
}
