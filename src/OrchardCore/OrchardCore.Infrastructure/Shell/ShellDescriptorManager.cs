using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.Environment.Shell.Descriptor;
using OrchardCore.Environment.Shell.Descriptor.Models;
using OrchardCore.Modules;
using YesSql;

namespace OrchardCore.Environment.Shell.Data.Descriptors
{
    /// <summary>
    /// Implements <see cref="IShellDescriptorManager"/> by providing the list of features store in the database. 
    /// </summary>
    public class ShellDescriptorManager : IShellDescriptorManager
    {
        private ShellDescriptor _shellDescriptor;
        private List<ShellFeature> _featuresAccrossTenants;

        private readonly IShellHost _shellHost;
        private readonly ShellSettings _shellSettings;
        private readonly IShellConfiguration _shellConfiguration;
        private readonly IEnumerable<ShellFeature> _alwaysEnabledFeatures;
        private readonly IEnumerable<IShellDescriptorManagerEventHandler> _shellDescriptorManagerEventHandlers;
        private readonly ISession _session;
        private readonly ILogger _logger;

        public ShellDescriptorManager(
            IShellHost shellHost,
            ShellSettings shellSettings,
            IShellConfiguration shellConfiguration,
            IEnumerable<ShellFeature> shellFeatures,
            IEnumerable<IShellDescriptorManagerEventHandler> shellDescriptorManagerEventHandlers,
            ISession session,
            ILogger<ShellDescriptorManager> logger)
        {
            _shellHost = shellHost;
            _shellSettings = shellSettings;
            _shellConfiguration = shellConfiguration;
            _alwaysEnabledFeatures = shellFeatures.Where(f => f.AlwaysEnabled).ToArray();
            _shellDescriptorManagerEventHandlers = shellDescriptorManagerEventHandlers;
            _session = session;
            _logger = logger;
        }

        public async Task<ShellDescriptor> GetShellDescriptorAsync()
        {
            var descriptor = await LoadShellDescriptorAsync();

            if (_featuresAccrossTenants == null)
            {
                var featuresAccrossTenants = new List<ShellFeature>();

                if (_shellSettings.Name != ShellHelper.DefaultShellName)
                {
                    var settings = _shellHost.GetSettings(ShellHelper.DefaultShellName);

                    if (settings.State == Models.TenantState.Running)
                    {
                        var shell = await _shellHost.GetOrCreateShellContextAsync(settings);

                        using (var scope = shell.ServiceProvider.CreateScope())
                        {
                            var manager = scope.ServiceProvider.GetRequiredService<IShellDescriptorManager>();
                            var features = (await manager.GetShellDescriptorAsync()).Features.Where(f => f.AcrossTenants);
                            featuresAccrossTenants.AddRange(features);
                        }
                    }
                }

                _featuresAccrossTenants = featuresAccrossTenants;
            }

            return new ShellDescriptor()
            {
                SerialNumber = descriptor.SerialNumber,
                Features = _featuresAccrossTenants.Concat(descriptor.Features).Distinct().ToList(),
                Parameters = descriptor.Parameters.ToList()
            };
        }

        public async Task<ShellDescriptor> LoadShellDescriptorAsync()
        {
            // Prevent multiple queries during the same request
            if (_shellDescriptor == null)
            {
                _shellDescriptor = await _session.Query<ShellDescriptor>().FirstOrDefaultAsync();

                if (_shellDescriptor != null)
                {
                    var configuredFeatures = new ConfiguredFeatures();
                    _shellConfiguration.Bind(configuredFeatures);

                    var features = _alwaysEnabledFeatures.Concat(configuredFeatures.Features
                        .Select(id => new ShellFeature(id) { AlwaysEnabled = true })).Distinct();

                    _shellDescriptor.Features = features
                        .Concat(_shellDescriptor.Features)
                        .Distinct()
                        .ToList();
                }
            }

            return _shellDescriptor;
        }

        public async Task UpdateShellDescriptorAsync(int priorSerialNumber, IEnumerable<ShellFeature> enabledFeatures, IEnumerable<ShellParameter> parameters)
        {
            var shellDescriptorRecord = await LoadShellDescriptorAsync();
            var serialNumber = shellDescriptorRecord == null
                ? 0
                : shellDescriptorRecord.SerialNumber;

            if (priorSerialNumber != serialNumber)
            {
                throw new InvalidOperationException("Invalid serial number for shell descriptor");
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Updating shell descriptor for tenant '{TenantName}' ...", _shellSettings.Name);
            }

            if (shellDescriptorRecord == null)
            {
                shellDescriptorRecord = new ShellDescriptor { SerialNumber = 1 };
            }
            else
            {
                shellDescriptorRecord.SerialNumber++;
            }

            var features = _alwaysEnabledFeatures.Concat(enabledFeatures).Distinct().ToList();

            var allTenants = false;
            if (_shellSettings.Name == ShellHelper.DefaultShellName)
            {
                var oldFeaturesAcrossTenant = shellDescriptorRecord.Features.Where(f => f.AcrossTenants);
                var newFeaturesAcrossTenant = features.Where(f => f.AcrossTenants);

                if (oldFeaturesAcrossTenant.Count() != newFeaturesAcrossTenant.Count())
                {
                    allTenants = true;
                }
            }

            shellDescriptorRecord.Features = features;
            shellDescriptorRecord.Parameters = parameters.ToList();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Shell descriptor updated for tenant '{TenantName}'.", _shellSettings.Name);
            }

            _session.Save(shellDescriptorRecord);

            await _shellDescriptorManagerEventHandlers.InvokeAsync((handler, shellDescriptorRecord, _shellSettings) =>
                handler.Changed(shellDescriptorRecord, !allTenants ? _shellSettings.Name : null), shellDescriptorRecord, _shellSettings, _logger);
        }

        private class ConfiguredFeatures
        {
            public string[] Features { get; set; } = Array.Empty<string>();
        }
    }
}
