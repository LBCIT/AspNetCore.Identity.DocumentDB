
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
    ///     When passing a cancellation token, it will only be used if the operation requires a database interaction.
    /// </summary>
    /// <typeparam name="TUser"></typeparam>
    public class UserStore<TUser> :
            IUserPasswordStore<TUser>,
            IUserRoleStore<TUser>,
            IUserLoginStore<TUser>,
            IUserSecurityStampStore<TUser>,
            IUserEmailStore<TUser>,
            IUserClaimStore<TUser>,
            IUserPhoneNumberStore<TUser>,
            IUserTwoFactorStore<TUser>,
            IUserLockoutStore<TUser>,
            IQueryableUserStore<TUser>,
#if NETSTANDARD2_0
            IUserAuthenticatorKeyStore<TUser>,
            IUserTwoFactorRecoveryCodeStore<TUser>,
#endif
            IUserAuthenticationTokenStore<TUser>
        where TUser : IdentityUser
    {
        private readonly IDocumentClient _Client;
        private readonly DocumentCollection _Users; // DocumentCollection of TUser
        private readonly bool UsesPartitioning;

        public UserStore(IDocumentClient documentClient, DocumentCollection users) // DocumentCollection of TUser
        {
            _Client = documentClient;
            _Users = users;
            UsesPartitioning = _Users.PartitionKey?.Paths.Any() ?? false;
        }

        public virtual void Dispose()
        {
            // no need to dispose of anything, DocumentDB handles connection pooling automatically
        }

        public virtual async Task<IdentityResult> CreateAsync(TUser user, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                user.UserId = user.DocId ?? Guid.NewGuid().ToString();
                user.DocId = "user";
            }

            var result = await _Client.CreateDocumentAsync(_Users.DocumentsLink, user);
            var userResult = (TUser)(dynamic)result.Resource;
            user.DocId = userResult.DocId;
            user.UserId = userResult.UserId;
            user.ResourceId = userResult.ResourceId;

            if (UsesPartitioning)
            {
                await CreateMapping(user.NormalizedUserName, user.UserId, TypeEnum.UserMappingUsername);
                await CreateMapping(user.NormalizedEmail, user.UserId, TypeEnum.UserMappingEmail);
            }

            return IdentityResult.Success;
        }

        public virtual async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken token)
        {
            var oldUser = await FindByIdAsync(user.UserId, token);

            if (UsesPartitioning)
            {
                if (oldUser.NormalizedUserName != user.NormalizedUserName)
                {
                    await DeleteMapping(oldUser.NormalizedUserName, TypeEnum.UserMappingUsername);
                    await CreateMapping(user.NormalizedUserName, user.UserId, TypeEnum.UserMappingUsername);
                }

                if (oldUser.NormalizedEmail != user.NormalizedEmail)
                {
                    await DeleteMapping(oldUser.NormalizedEmail, TypeEnum.UserMappingEmail);
                    await CreateMapping(user.NormalizedEmail, user.UserId, TypeEnum.UserMappingEmail);
                }
            }

            // todo should add an optimistic concurrency check
            await _Client.ReplaceDocumentAsync(GetUserUri(user), user);

            return IdentityResult.Success;
        }

        public virtual async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                await DeleteMapping(user.NormalizedUserName, TypeEnum.UserMappingUsername);
                await DeleteMapping(user.NormalizedEmail, TypeEnum.UserMappingEmail);
                /*await Task.WhenAll(user.Logins.Select((login) =>
                {
                    var partitionKey = login.LoginProvider + login.ProviderKey;
                    return DeleteMapping(partitionKey);
                }));*/
            }
            await _Client.DeleteDocumentAsync(GetUserUri(user), GetRequestOptions(user.UserId));

            // todo success based on delete result
            return IdentityResult.Success;
        }

        public virtual async Task<string> GetUserIdAsync(TUser user, CancellationToken cancellationToken)
            => user.Id;

        public virtual async Task<string> GetUserNameAsync(TUser user, CancellationToken cancellationToken)
            => user.UserName;

        public virtual async Task SetUserNameAsync(TUser user, string userName, CancellationToken cancellationToken)
            => user.UserName = userName;

        // note: again this isn't used by Identity framework so no way to integration test it
        public virtual async Task<string> GetNormalizedUserNameAsync(TUser user, CancellationToken cancellationToken)
            => user.NormalizedUserName;

        public virtual async Task SetNormalizedUserNameAsync(TUser user, string normalizedUserName, CancellationToken cancellationToken)
            => user.NormalizedUserName = normalizedUserName;

        public virtual async Task<TUser> FindByIdAsync(string userId, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink, GetFeedOptions(userId))
                    .Where(u => u.DocId == "user").AsEnumerable().FirstOrDefault();
            }

            return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink)
                .Where(u => u.Type == TypeEnum.User && u.DocId == userId).AsEnumerable().FirstOrDefault();
        }

        public virtual async Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                var partitionKeyMapping = _Client.CreateDocumentQuery<PartitionMapping>(_Users.DocumentsLink, GetFeedOptions(normalizedUserName))
                    .Where(u => u.Id == TypeEnum.UserMappingUsername).AsEnumerable().FirstOrDefault();

                return partitionKeyMapping != null ?
                    _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink, GetFeedOptions(partitionKeyMapping.TargetId))
                    .Where(u => u.Type == TypeEnum.User && u.DocId == "user").AsEnumerable().FirstOrDefault() : null;
            }

            return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink)
                .Where(u => u.Type == TypeEnum.User && u.NormalizedUserName == normalizedUserName).AsEnumerable().FirstOrDefault();
        }

        public virtual async Task SetPasswordHashAsync(TUser user, string passwordHash, CancellationToken token)
            => user.PasswordHash = passwordHash;

        public virtual async Task<string> GetPasswordHashAsync(TUser user, CancellationToken token)
            => user.PasswordHash;

        public virtual async Task<bool> HasPasswordAsync(TUser user, CancellationToken token)
            => user.HasPassword();

        public virtual async Task AddToRoleAsync(TUser user, string normalizedRoleName, CancellationToken token)
            => user.AddRole(normalizedRoleName);

        public virtual async Task RemoveFromRoleAsync(TUser user, string normalizedRoleName, CancellationToken token)
            => user.RemoveRole(normalizedRoleName);

        // todo might have issue, I'm just storing Normalized only now, so I'm returning normalized here instead of not normalized.
        // EF provider returns not noramlized here
        // however, the rest of the API uses normalized (add/remove/isinrole) so maybe this approach is better anyways
        // note: could always map normalized to not if people complain
        public virtual async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken token)
            => user.Roles;

        public virtual async Task<bool> IsInRoleAsync(TUser user, string normalizedRoleName, CancellationToken token)
            => user.Roles.Contains(normalizedRoleName);

        public virtual async Task<IList<TUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken token)
            => _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink).Where(u => u.Roles.Contains(normalizedRoleName))
                .ToList();

        public virtual async Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken token)
            => user.AddLogin(login);

        public virtual async Task RemoveLoginAsync(TUser user, string loginProvider, string providerKey, CancellationToken cancellationToken = default(CancellationToken))
            => user.RemoveLogin(loginProvider, providerKey);

        public virtual async Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken token)
            => user.Logins
                .Select(l => l.ToUserLoginInfo())
                .ToList();

        public virtual async Task<TUser> FindByLoginAsync(
            string loginProvider, string providerKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (UsesPartitioning)
            {
                throw new NotImplementedException("FindByLogin is not yet supported for partitioned collections.");

                /* TODO: I have an idea for implementing this with paritioning support:
                 * We need to store the logins as separate documents with their loginProvier + providerKey as partition key
                 * and the corresponding IdentityUser ID.
                 * In the IdentityUser we keep the Logins array but store the IDs (partition keys) of the logins.
                 * Then we can query logins from the provider + key and we can get logins when we know the IdentityUser.
                 * 
                 * When returning the IdentityUser we always need to query the assigned logins as well.
                 * IdentityUser: Create private Logins with IDs and JsonProperty and public Logins with actual Login objects and JsonIgnore.
                 * 
                 * Remember to also adjust the CreateAsync, UpdateAsync, DeleteAsync & AddLoginAsync methods.
                 * /

                /*
                var partitionKey = loginProvider + providerKey;
                var partitionKeyMapping = _Client.CreateDocumentQuery<PartitionMapping>(_Users.DocumentsLink, GetFeedOptions(partitionKey))
                    .Where(m => m.Id == partitionKey)
                    .ToList().FirstOrDefault();

                return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink, GetFeedOptions(partitionKeyMapping.TargetId))
                    .SelectMany(u => u.Logins.Where(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey).Select(u2 => u))
                    .ToList().FirstOrDefault();
                */
            }

            return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink)
                .SelectMany(u => u.Logins.Where(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey).Select(u2 => u))
                .ToList().FirstOrDefault();
        }
        public virtual async Task SetSecurityStampAsync(TUser user, string stamp, CancellationToken token)
            => user.SecurityStamp = stamp;

        public virtual async Task<string> GetSecurityStampAsync(TUser user, CancellationToken token)
            => user.SecurityStamp;

        public virtual async Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken token)
            => user.EmailConfirmed;

        public virtual async Task SetEmailConfirmedAsync(TUser user, bool confirmed, CancellationToken token)
            => user.EmailConfirmed = confirmed;

        public virtual async Task SetEmailAsync(TUser user, string email, CancellationToken token)
            => user.Email = email;

        public virtual async Task<string> GetEmailAsync(TUser user, CancellationToken token)
            => user.Email;

        // note: no way to intergation test as this isn't used by Identity framework	
        public virtual async Task<string> GetNormalizedEmailAsync(TUser user, CancellationToken cancellationToken)
            => user.NormalizedEmail;

        public virtual async Task SetNormalizedEmailAsync(TUser user, string normalizedEmail, CancellationToken cancellationToken)
            => user.NormalizedEmail = normalizedEmail;

        public virtual async Task<TUser> FindByEmailAsync(string normalizedEmail, CancellationToken token)
        {
            if (UsesPartitioning)
            {
                var partitionKeyMapping = _Client.CreateDocumentQuery<PartitionMapping>(_Users.DocumentsLink, GetFeedOptions(normalizedEmail))
                    .Where(u => u.Id == TypeEnum.UserMappingEmail).AsEnumerable().FirstOrDefault();

                return partitionKeyMapping != null ?
                    _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink, GetFeedOptions(partitionKeyMapping.TargetId))
                    .Where(u => u.Type == TypeEnum.User && u.DocId == "user").AsEnumerable().FirstOrDefault() : null;
            }

            return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink)
                .Where(u => u.Type == TypeEnum.User && u.NormalizedEmail == normalizedEmail).AsEnumerable().FirstOrDefault();
        }

        public virtual async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken token)
            => user.Claims.Select(c => c.ToSecurityClaim()).ToList();

        public virtual Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken token)
        {
            foreach (var claim in claims)
            {
                user.AddClaim(claim);
            }
            return Task.FromResult(0);
        }

        public virtual Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken token)
        {
            foreach (var claim in claims)
            {
                user.RemoveClaim(claim);
            }
            return Task.FromResult(0);
        }

        public virtual async Task ReplaceClaimAsync(TUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken = default(CancellationToken))
        {
            user.ReplaceClaim(claim, newClaim);
        }

        public virtual Task SetPhoneNumberAsync(TUser user, string phoneNumber, CancellationToken token)
        {
            user.PhoneNumber = phoneNumber;
            return Task.FromResult(0);
        }

        public virtual Task<string> GetPhoneNumberAsync(TUser user, CancellationToken token)
        {
            return Task.FromResult(user.PhoneNumber);
        }

        public virtual Task<bool> GetPhoneNumberConfirmedAsync(TUser user, CancellationToken token)
        {
            return Task.FromResult(user.PhoneNumberConfirmed);
        }

        public virtual Task SetPhoneNumberConfirmedAsync(TUser user, bool confirmed, CancellationToken token)
        {
            user.PhoneNumberConfirmed = confirmed;
            return Task.FromResult(0);
        }

        public virtual Task SetTwoFactorEnabledAsync(TUser user, bool enabled, CancellationToken token)
        {
            user.TwoFactorEnabled = enabled;
            return Task.FromResult(0);
        }

        public virtual Task<bool> GetTwoFactorEnabledAsync(TUser user, CancellationToken token)
        {
            return Task.FromResult(user.TwoFactorEnabled);
        }

        public virtual async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken = default(CancellationToken))
        {
            return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink)
                .SelectMany(u => u.Claims.Where(c => c.Type == claim.Type && c.Value == claim.Value).Select(u2 => u))
                .ToList();
        }

        public virtual Task<DateTimeOffset?> GetLockoutEndDateAsync(TUser user, CancellationToken token)
        {
            DateTimeOffset? dateTimeOffset = user.LockoutEndDateUtc;
            return Task.FromResult(dateTimeOffset);
        }

        public virtual Task SetLockoutEndDateAsync(TUser user, DateTimeOffset? lockoutEnd, CancellationToken token)
        {
            user.LockoutEndDateUtc = lockoutEnd?.UtcDateTime;
            return Task.FromResult(0);
        }

        public virtual Task<int> IncrementAccessFailedCountAsync(TUser user, CancellationToken token)
        {
            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        public virtual Task ResetAccessFailedCountAsync(TUser user, CancellationToken token)
        {
            user.AccessFailedCount = 0;
            return Task.FromResult(0);
        }

        public virtual async Task<int> GetAccessFailedCountAsync(TUser user, CancellationToken token)
            => user.AccessFailedCount;

        public virtual async Task<bool> GetLockoutEnabledAsync(TUser user, CancellationToken token)
            => user.LockoutEnabled;

        public virtual async Task SetLockoutEnabledAsync(TUser user, bool enabled, CancellationToken token)
            => user.LockoutEnabled = enabled;

        /// <summary>
        /// Returns a list of all users.
        /// Avoid using this property whenever possible.
        /// The cross-partition database request resulting from this will be very expensive.
        /// </summary>
        public virtual IQueryable<TUser> Users
        {
            get
            {
                if (UsesPartitioning)
                {
                    return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink, new FeedOptions { EnableCrossPartitionQuery = true })
                        .Where(u => u.DocId == "user").AsQueryable();
                }

                return _Client.CreateDocumentQuery<TUser>(_Users.DocumentsLink)
                    .Where(u => u.Type == TypeEnum.User).AsQueryable();
            }
        }
            

        public virtual async Task SetTokenAsync(TUser user, string loginProvider, string name, string value, CancellationToken cancellationToken)
            => user.SetToken(loginProvider, name, value);

        public virtual async Task RemoveTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
            => user.RemoveToken(loginProvider, name);

        public virtual async Task<string> GetTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
            => user.GetTokenValue(loginProvider, name);

        private string GetUserUri(TUser user)
        {
            if (user.SelfLink != null)
                return user.SelfLink;
            else if (user.ResourceId != null)
                return _Users.DocumentsLink + user.ResourceId;
            else
                return GetUserUri(user.DocId);
        }

        private string GetUserUri(string id)
        {
            return string.Format("{0}/docs/{1}", _Users.AltLink, id);
        }

        private async Task CreateMapping(string id, string targetId, TypeEnum userMappingType)
        {
            // don't create mappings for null ID
            if (id == null) return;

            if (userMappingType != TypeEnum.UserMappingEmail && userMappingType != TypeEnum.UserMappingUsername && userMappingType != TypeEnum.UserMappingExternalProvider)
            {
                throw new NotImplementedException($"No support for creating mappings for type {userMappingType}");
            }

            await _Client.CreateDocumentAsync(_Users.DocumentsLink, new PartitionMapping
            {
                PartitionKey = id,
                Id = userMappingType,
                TargetId = targetId
            });
        }

        private async Task DeleteMapping(string id, TypeEnum userMappingType)
        {
            if (id != null)
            {
                var typeString = Helper.GetEnumMemberValue(userMappingType);
                await _Client.DeleteDocumentAsync(GetUserUri(typeString), GetRequestOptions(id));
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

        private string GetLoginPartitionKey(UserLoginInfo login) {
            return GetLoginPartitionKey(login.LoginProvider, login.ProviderKey);
        }

        private string GetLoginPartitionKey(IdentityUserLogin login) {
            return GetLoginPartitionKey(login.LoginProvider, login.ProviderKey);
        }

        private string GetLoginPartitionKey(string loginProvider, string providerKey) {
            return loginProvider + providerKey;
        }
#if NETSTANDARD2_0
        public Task SetAuthenticatorKeyAsync( TUser user, string key, CancellationToken cancellationToken ) {
            user.TwoFactorAuthenticatorKey = key;
            return Task.CompletedTask;
        }

        public Task<string> GetAuthenticatorKeyAsync( TUser user, CancellationToken cancellationToken ) {

            return Task.FromResult( user.TwoFactorAuthenticatorKey );
        }

        public Task ReplaceCodesAsync( TUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken ) {
            user.TwoFactorRecoveryCodes = new List<string>( recoveryCodes );
            return Task.CompletedTask;
        }

        public Task<bool> RedeemCodeAsync( TUser user, string code, CancellationToken cancellationToken ) {
            return Task.FromResult( user.TwoFactorRecoveryCodes.Remove( code ) );
        }

        public Task<int> CountCodesAsync( TUser user, CancellationToken cancellationToken ) {
            return Task.FromResult( user.TwoFactorRecoveryCodes?.Count ?? 0 );
        }
#endif
    }
}