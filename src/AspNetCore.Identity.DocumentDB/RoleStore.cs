
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
// I'm using async methods to leverage implicit Task wrapping of results from expression bodied functions.

namespace Microsoft.AspNetCore.Identity.DocumentDB
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Documents;
    using Azure.Documents.Client;
    using global::AspNetCore.Identity.DocumentDB;

    /// <summary>
    ///     Note: Deleting and updating do not modify the roles stored on a user document. If you desire this dynamic
    ///     capability, override the appropriate operations on RoleStore as desired for your application. For example you could
    ///     perform a document modification on the users collection before a delete or a rename.
    ///     When passing a cancellation token, it will only be used if the operation requires a database interaction.
    /// </summary>
    /// <typeparam name="TRole">Needs to extend the provided IdentityRole type.</typeparam>
    public class RoleStore<TRole> : IQueryableRoleStore<TRole>, IRoleClaimStore<TRole>
        where TRole : IdentityRole
    {
        private readonly IDocumentClient _Client;
        private readonly DocumentCollection _Roles;
        private readonly bool UsesPartitioning;

        public RoleStore(IDocumentClient documentClient, DocumentCollection roles)
        {
            _Client = documentClient;
            _Roles = roles;
            UsesPartitioning = _Roles.PartitionKey?.Paths.Any() ?? false;
        }

        public virtual void Dispose()
        {
            // no need to dispose of anything, DocumentDB handles connection pooling automatically
        }

        public virtual async Task<IdentityResult> CreateAsync(TRole role, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                role.RoleId = role.DocId ?? Guid.NewGuid().ToString();
                role.DocId = "role";
            }

            var result = await _Client.CreateDocumentAsync(_Roles.DocumentsLink, role);
            var roleResult = (TRole)(dynamic)result.Resource;
            role.DocId = roleResult.DocId;
            role.RoleId = roleResult.RoleId;
            role.ResourceId = roleResult.ResourceId;

            if (UsesPartitioning)
            {
                await CreateMapping(role.NormalizedName, role.RoleId);
            }

            return IdentityResult.Success;
        }

        public virtual async Task<IdentityResult> UpdateAsync(TRole role, CancellationToken token)
        {
            var oldRole = await FindByIdAsync(role.DocId, token);

            if (UsesPartitioning && oldRole.NormalizedName != role.NormalizedName)
            {
                await DeleteMapping(oldRole.NormalizedName);
                await CreateMapping(role.NormalizedName, role.RoleId);
            }

            var result = await _Client.ReplaceDocumentAsync(GetRoleUri(role.DocId), role);

            // todo low priority result based on replace result
            return IdentityResult.Success;
        }

        public virtual async Task<IdentityResult> DeleteAsync(TRole role, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                await DeleteMapping(role.NormalizedName);
            }
            await _Client.DeleteDocumentAsync(GetRoleUri(role.DocId), GetRequestOptions(role.RoleId));

            // todo low priority result based on delete result
            return IdentityResult.Success;
        }

        public virtual async Task<string> GetRoleIdAsync(TRole role, CancellationToken cancellationToken)
            => role.Id;

        public virtual async Task<string> GetRoleNameAsync(TRole role, CancellationToken cancellationToken)
            => role.Name;

        public virtual async Task SetRoleNameAsync(TRole role, string roleName, CancellationToken cancellationToken)
            => role.Name = roleName;

        // note: can't test as of yet through integration testing because the Identity framework doesn't use this method internally anywhere
        public virtual async Task<string> GetNormalizedRoleNameAsync(TRole role, CancellationToken cancellationToken)
            => role.NormalizedName;

        public virtual async Task SetNormalizedRoleNameAsync(TRole role, string normalizedName, CancellationToken cancellationToken)
            => role.NormalizedName = normalizedName;

        public virtual async Task<TRole> FindByIdAsync(string roleId, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                return _Client.CreateDocumentQuery<TRole>(_Roles.DocumentsLink, GetFeedOptions(roleId))
                    .Where(r => r.Type == TypeEnum.Role && r.DocId == "role")
                    .AsEnumerable().FirstOrDefault();
            }

            return _Client.CreateDocumentQuery<TRole>(_Roles.DocumentsLink)
                .Where(r => r.Type == TypeEnum.Role && r.DocId == roleId)
                .AsEnumerable().FirstOrDefault();
        }

        public virtual async Task<TRole> FindByNameAsync(string normalizedName, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                var partitionKeyMapping = _Client.CreateDocumentQuery<PartitionMapping>(_Roles.DocumentsLink, GetFeedOptions(normalizedName))
                   .Where(r => r.Id == TypeEnum.RoleMapping).AsEnumerable().FirstOrDefault();

                return partitionKeyMapping != null ?
                    _Client.CreateDocumentQuery<TRole>(_Roles.DocumentsLink, GetFeedOptions(partitionKeyMapping.TargetId))
                    .Where(r => r.Type == TypeEnum.Role && r.DocId == "role").AsEnumerable().FirstOrDefault() : null;
            }

            return _Client.CreateDocumentQuery<TRole>(_Roles.DocumentsLink)
                .Where(r => r.Type == TypeEnum.Role && r.NormalizedName == normalizedName).AsEnumerable().FirstOrDefault();
        }

        /// <summary>
        /// Returns a list of all roles.
        /// Avoid using this property whenever possible.
        /// The cross-partition database request resulting from this will be very expensive.
        /// </summary>
        public virtual IQueryable<TRole> Roles
        {
            get
            {
                if (UsesPartitioning)
                {
                    return _Client.CreateDocumentQuery<TRole>(_Roles.DocumentsLink, new FeedOptions { EnableCrossPartitionQuery = true })
                        .Where(u => u.DocId == "role").AsQueryable();
                }

                return _Client.CreateDocumentQuery<TRole>(_Roles.DocumentsLink)
                    .Where(u => u.Type == TypeEnum.Role).AsQueryable();
            }
        }

        private string GetRoleUri(string documentId)
        {
            return string.Format("{0}/docs/{1}", _Roles.AltLink, documentId);
        }

        public virtual async Task<IList<Claim>> GetClaimsAsync(TRole role, CancellationToken token)
            => role.Claims.Select(c => c.ToSecurityClaim()).ToList();

        public virtual Task AddClaimAsync(TRole role, Claim claim, CancellationToken token = default(CancellationToken))
        {
            role.AddClaim(claim);
            return Task.FromResult(0);
        }

        public virtual Task RemoveClaimAsync(TRole role, Claim claim, CancellationToken token = default(CancellationToken))
        {
            role.RemoveClaim(claim);
            return Task.FromResult(0);
        }

        private async Task CreateMapping(string id, string targetId)
        {
            await _Client.CreateDocumentAsync(_Roles.DocumentsLink, new PartitionMapping
            {
                PartitionKey = id,
                Id = TypeEnum.RoleMapping,
                TargetId = targetId
            });
        }

        private async Task DeleteMapping(string id)
        {
            if (id != null)
            {
                var typeString = Helper.GetEnumMemberValue(TypeEnum.RoleMapping);
                await _Client.DeleteDocumentAsync(GetRoleUri(id), GetRequestOptions(id));
            }
        }

        private RequestOptions GetRequestOptions(object partitionKeyValue, RequestOptions options = null)
        {
            return Helper.GetRequestOptions(UsesPartitioning ? partitionKeyValue : null, options);
        }

        private FeedOptions GetFeedOptions(object partitionKeyValue, FeedOptions options = null)
        {
            return Helper.GetFeedOptions(UsesPartitioning ? partitionKeyValue : null, options);
        }
    }
}