using System.Threading.Tasks;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Builders;
using OrchardCore.Environment.Shell.Descriptor.Models;
using OrchardCore.Environment.Shell.Models;

namespace OrchardCore.Shells.Database.Configuration
{
    public static class DatabaseShellContextFactoryExtensions
    {
        internal static Task<ShellContext> GetDatabaseContextAsync(this IShellContextFactory shellContextFactory, DatabaseShellsStorageOptions options)
        {
            var settings = new ShellSettings()
            {
                Name = ShellHelper.DefaultShellName,
                State = TenantState.Running
            };

            settings["DatabaseProvider"] = options.DatabaseProvider;
            settings["ConnectionString"] = options.ConnectionString;
            settings["TablePrefix"] = options.TablePrefix;

            return shellContextFactory.CreateDescribedContextAsync(settings, new ShellDescriptor());
        }
    }
}
