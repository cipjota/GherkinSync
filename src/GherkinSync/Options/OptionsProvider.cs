using System.Runtime.InteropServices;

namespace GherkinSync.Options
{
    internal partial class OptionsProvider
    {
        // Register the options with these attributes on your package class:
        // [ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "MyExtension", "General", 0, 0, true)]
        // [ProvideProfile(typeof(OptionsProvider.GeneralOptions), "MyExtension", "General", 0, 0, true)]
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<GherkinSyncOptions> { }
    }
}
