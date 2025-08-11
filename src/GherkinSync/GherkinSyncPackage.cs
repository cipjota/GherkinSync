global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using GherkinSync.Options;
using System.Runtime.InteropServices;
using System.Threading;

namespace GherkinSync
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "GherkinSync", "Settings", 0, 0, true)]
    [ProvideProfile(typeof(OptionsProvider.GeneralOptions), "GherkinSync", "Settings", 0, 0, true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.GherkinSyncString)]
    public sealed class GherkinSyncPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                GherkinSyncOptions.Saved += OnSettingsSaved;

                await this.RegisterCommandsAsync();
            }
            catch (Exception ex)
            {
                ActivityLog.TryLogError(ex.Source, ex.Message);
            }
        }

        private void OnSettingsSaved(GherkinSyncOptions obj)
        {

        }
    }
}