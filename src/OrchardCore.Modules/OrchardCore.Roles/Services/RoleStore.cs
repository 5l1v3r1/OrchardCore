using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.Data;
using OrchardCore.Infrastructure.Cache;
using OrchardCore.Modules;
using OrchardCore.Roles.Models;
using OrchardCore.Security;
using YesSql;

namespace OrchardCore.Roles.Services
{
    public class RoleStore : IRoleClaimStore<IRole>, IQueryableRoleStore<IRole>
    {
        private readonly ISession _session;
        private readonly ISessionHelper _sessionHelper;
        private readonly IDataStoreDistributedCache<ISessionHelper> _dataStoreDistributedCache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IStringLocalizer<RoleStore> S;

        private bool _updating;

        public RoleStore(
            ISession session,
            ISessionHelper sessionHelper,
            IDataStoreDistributedCache<ISessionHelper> dataStoreDistributedCache,
            IServiceProvider serviceProvider,
            IStringLocalizer<RoleStore> stringLocalizer,
            ILogger<RoleStore> logger)
        {
            _session = session;
            _sessionHelper = sessionHelper;
            _dataStoreDistributedCache = dataStoreDistributedCache;
            _serviceProvider = serviceProvider;
            S = stringLocalizer;
            Logger = logger;
        }

        public ILogger Logger { get; }

        public IQueryable<IRole> Roles => GetRolesAsync().GetAwaiter().GetResult().Roles.AsQueryable();

        /// <summary>
        /// Returns the document from the database to be updated.
        /// </summary>
        private Task<RolesDocument> LoadRolesAsync() => _dataStoreDistributedCache.LoadAsync<RolesDocument>();

        /// <summary>
        /// Returns the document from the cache or creates a new one. The result should not be updated.
        /// </summary>
        private Task<RolesDocument> GetRolesAsync() => _dataStoreDistributedCache.GetAsync<RolesDocument>();

        private Task UpdateRolesAsync(RolesDocument roles)
        {
            _session.Save(roles, checkConcurrency: true);

            _sessionHelper.RegisterAfterCommitSuccess<RolesDocument>(() =>
            {
                return _dataStoreDistributedCache.UpdateAsync(roles);
            });

            // Specific to 'RoleStore'.
            _updating = true;

            return Task.CompletedTask;
        }

        #region IRoleStore<IRole>
        public async Task<IdentityResult> CreateAsync(IRole role, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            var roles = await LoadRolesAsync();
            roles.Roles.Add((Role)role);
            await UpdateRolesAsync(roles);

            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DeleteAsync(IRole role, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            var roleToRemove = (Role)role;

            if (String.Equals(roleToRemove.NormalizedRoleName, "ANONYMOUS") ||
                String.Equals(roleToRemove.NormalizedRoleName, "AUTHENTICATED"))
            {
                return IdentityResult.Failed(new IdentityError { Description = S["Can't delete system roles."] });
            }

            var roleRemovedEventHandlers = _serviceProvider.GetRequiredService<IEnumerable<IRoleRemovedEventHandler>>();
            await roleRemovedEventHandlers.InvokeAsync((handler, roleToRemove) => handler.RoleRemovedAsync(roleToRemove.RoleName), roleToRemove, Logger);

            var roles = await LoadRolesAsync();
            roleToRemove = roles.Roles.FirstOrDefault(r => r.RoleName == roleToRemove.RoleName);
            roles.Roles.Remove(roleToRemove);

            await UpdateRolesAsync(roles);

            return IdentityResult.Success;
        }

        public async Task<IRole> FindByIdAsync(string roleId, CancellationToken cancellationToken)
        {
            // While updating find a role from the loaded document being mutated.
            var roles = _updating ? await LoadRolesAsync() : await GetRolesAsync();

            var role = roles.Roles.FirstOrDefault(x => x.RoleName == roleId);

            if (role == null)
            {
                return null;
            }

            return _updating ? role : role.Clone();
        }

        public async Task<IRole> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
        {
            // While updating find a role from the loaded document being mutated.
            var roles = _updating ? await LoadRolesAsync() : await GetRolesAsync();

            var role = roles.Roles.FirstOrDefault(x => x.NormalizedRoleName == normalizedRoleName);

            if (role == null)
            {
                return null;
            }

            return _updating ? role : role.Clone();
        }

        public Task<string> GetNormalizedRoleNameAsync(IRole role, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            return Task.FromResult(((Role)role).NormalizedRoleName);
        }

        public Task<string> GetRoleIdAsync(IRole role, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            return Task.FromResult(role.RoleName.ToUpperInvariant());
        }

        public Task<string> GetRoleNameAsync(IRole role, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            return Task.FromResult(role.RoleName);
        }

        public Task SetNormalizedRoleNameAsync(IRole role, string normalizedName, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            ((Role)role).NormalizedRoleName = normalizedName;

            return Task.CompletedTask;
        }

        public Task SetRoleNameAsync(IRole role, string roleName, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            ((Role)role).RoleName = roleName;

            return Task.CompletedTask;
        }

        public async Task<IdentityResult> UpdateAsync(IRole role, CancellationToken cancellationToken)
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            var roles = await LoadRolesAsync();
            var existingRole = roles.Roles.FirstOrDefault(x => x.RoleName == role.RoleName);
            roles.Roles.Remove(existingRole);
            roles.Roles.Add((Role)role);

            await UpdateRolesAsync(roles);

            return IdentityResult.Success;
        }

        #endregion

        #region IRoleClaimStore<IRole>
        public Task AddClaimAsync(IRole role, Claim claim, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            if (claim == null)
            {
                throw new ArgumentNullException(nameof(claim));
            }

            ((Role)role).RoleClaims.Add(new RoleClaim { ClaimType = claim.Type, ClaimValue = claim.Value });

            return Task.CompletedTask;
        }

        public Task<IList<Claim>> GetClaimsAsync(IRole role, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            return Task.FromResult<IList<Claim>>(((Role)role).RoleClaims.Select(x => x.ToClaim()).ToList());
        }

        public Task RemoveClaimAsync(IRole role, Claim claim, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (role == null)
            {
                throw new ArgumentNullException(nameof(role));
            }

            if (claim == null)
            {
                throw new ArgumentNullException(nameof(claim));
            }

            ((Role)role).RoleClaims.RemoveAll(x => x.ClaimType == claim.Type && x.ClaimValue == claim.Value);

            return Task.CompletedTask;
        }

        #endregion

        public void Dispose()
        {
        }
    }
}
