using Microsoft.VisualStudio.PlatformUI;

namespace GherkinSync.ToolWindows
{
    public partial class SyncOptionsDialog : DialogWindow
    {
        public SyncOptionsDialog()
        {
            InitializeComponent();
        }

        public SyncOptionsDialogViewModel SyncOptions { get; private set; } = new SyncOptionsDialogViewModel();

        private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
