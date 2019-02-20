// ReSharper disable once CheckNamespace - Common convention to locate extensions in Microsoft namespaces for simplifying autocompletion as a consumer.

using System.Diagnostics.CodeAnalysis;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.DocumentDB;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class DocumentDBIdentityBuilderExtensions
    {
        /// <summary>
        ///     If you want control over creating the users and roles collections, use this overload.
        ///     This method only registers DocumentDB stores, you also need to call AddIdentity.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="documentClient"></param>
        /// <param name="collectionFactory">Function containing DocumentCollection</param>
        public static IdentityBuilder RegisterDocumentDBStores(
            this IdentityBuilder builder,
            Action<DocumentDbOptions> documentDbOptions)
        {
            return RegisterDocumentDBStores<IdentityUser, IdentityRole>(builder, documentDbOptions);
        }

        /// <summary>
        ///     If you want control over creating the users and roles collections, use this overload.
        ///     This method only registers DocumentDB stores, you also need to call AddIdentity.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="documentClient"></param>
        /// <param name="collectionFactory">Function containing DocumentCollection</param>
        public static IdentityBuilder RegisterDocumentDBStores<TUser, TRole>(
            this IdentityBuilder builder,
            Action<DocumentDbOptions> documentDbOptions)
            where TRole : IdentityRole
            where TUser : IdentityUser
        {
            var dbOptions = new DocumentDbOptions();
            documentDbOptions(dbOptions);

            if (dbOptions == null)
            {
                throw new ArgumentException("dbOptions cannot be null.");
            }
            if (dbOptions.DocumentUrl == null)
            {
                throw new ArgumentException("DocumentUrl cannot be null.");
            }
            if (dbOptions.DocumentKey == null)
            {
                throw new ArgumentException("DocumentKey cannot be null.");
            }
            if (dbOptions.DatabaseId == null)
            {
                throw new ArgumentException("DatabaseId cannot be null.");
            }
            if (dbOptions.CollectionId == null)
            {
                throw new ArgumentException("CollectionId cannot be null.");
            }

            var documentClient = new DocumentClient(new Uri(dbOptions.DocumentUrl), dbOptions.DocumentKey, dbOptions.ConnectionPolicy);
            var database = new Database { Id = dbOptions.DatabaseId };
            database = documentClient.CreateDatabaseIfNotExistsAsync(database).Result;
            var collection = new DocumentCollection { Id = dbOptions.CollectionId };
            if (dbOptions.PartitionKeyDefinition != null)
            {
                collection.PartitionKey = dbOptions.PartitionKeyDefinition;
            }
            collection = documentClient.CreateDocumentCollectionIfNotExistsAsync(database.AltLink, collection).Result;

            return RegisterDocumentDBStores<TUser, TRole>(builder, documentClient, (p) => collection);
        }

        /// <summary>
        ///     If you want control over creating the users and roles collections, use this overload.
        ///     This method only registers DocumentDB stores, you also need to call AddIdentity.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="documentClient"></param>
        /// <param name="collectionFactory">Function containing DocumentCollection</param>
        public static IdentityBuilder RegisterDocumentDBStores(
            this IdentityBuilder builder,
            IDocumentClient documentClient,
            Func<IServiceProvider, DocumentCollection> collectionFactory)
        {
            return RegisterDocumentDBStores<IdentityUser, IdentityRole>(builder, documentClient, collectionFactory);
        }

        /// <summary>
        ///     If you want control over creating the users and roles collections, use this overload.
        ///     This method only registers DocumentDB stores, you also need to call AddIdentity.
        /// </summary>
        /// <typeparam name="TUser"></typeparam>
        /// <typeparam name="TRole"></typeparam>
        /// <param name="builder"></param>
        /// <param name="documentClient"></param>
        /// <param name="collectionFactory">Function containing DocumentCollection</param>
        public static IdentityBuilder RegisterDocumentDBStores<TUser, TRole>(
            this IdentityBuilder builder,
            IDocumentClient documentClient,
            Func<IServiceProvider, DocumentCollection> collectionFactory)
            where TRole : IdentityRole
            where TUser : IdentityUser
        {
            if (typeof(TUser) != builder.UserType)
            {
                var message = "User type passed to RegisterDocumentDBStores must match user type passed to AddIdentity. "
                              + $"You passed {builder.UserType} to AddIdentity and {typeof(TUser)} to RegisterDocumentDBStores, "
                              + "these do not match.";
                throw new ArgumentException(message);
            }
            if (typeof(TRole) != builder.RoleType)
            {
                var message = "Role type passed to RegisterDocumentDBStores must match role type passed to AddIdentity. "
                              + $"You passed {builder.RoleType} to AddIdentity and {typeof(TRole)} to RegisterDocumentDBStores, "
                              + "these do not match.";
                throw new ArgumentException(message);
            }

            builder.Services.AddSingleton<IUserStore<TUser>>(p => new UserStore<TUser>(documentClient, collectionFactory(p)));
            builder.Services.AddSingleton<IRoleStore<TRole>>(p => new RoleStore<TRole>(documentClient, collectionFactory(p)));

            return builder;
        }

        /// <summary>
        ///     This method registers identity services and DocumentDB stores using the IdentityUser and IdentityRole types.
        /// </summary>
        /// <param name="service">The <see cref="IdentityBuilder"/> to build upon.</param>
        /// <param name="documentDbOptions">The options for creating the DocumentDB client.</param>
        /// <param name="identityOptions">The identity options used when calling AddIdentity.</param>
        /// <returns>The <see cref="IdentityBuilder"/> with the DocumentDB settings applied.</returns>
        public static IdentityBuilder AddIdentityWithDocumentDBStores(
            this IServiceCollection service,
            Action<DocumentDbOptions> documentDbOptions,
            Action<IdentityOptions> identityOptions = null)
        {
            return service.AddIdentityWithDocumentDBStores<IdentityUser, IdentityRole>(
                    documentDbOptions, identityOptions);
        }

        /// <summary>
        ///     This method allows you to customize the user and role type when registering identity services
        ///     and DocumentDB stores.`
        /// </summary>
        /// <typeparam name="TUser">The type associated with user identity information.</typeparam>
        /// <typeparam name="TRole">The type associated with role identity information.</typeparam>
        /// <param name="service">The <see cref="IdentityBuilder"/> to build upon.</param>
        /// <param name="documentDbOptions">The options for creating the DocumentDB client.</param>
        /// <param name="identityOptions">The identity options used when calling AddIdentity.</param>
        /// <returns>The <see cref="IdentityBuilder"/> with the DocumentDB settings applied.</returns>
        public static IdentityBuilder AddIdentityWithDocumentDBStores<TUser, TRole>(
            this IServiceCollection service,
            Action<DocumentDbOptions> documentDbOptions,
            Action<IdentityOptions> identityOptions = null)
            where TUser : IdentityUser
            where TRole : IdentityRole
        {
            return service.AddIdentity<TUser, TRole>(identityOptions)
                .RegisterDocumentDBStores<TUser, TRole>(documentDbOptions);
        }

        /// <summary>
        ///     This method allows you to customize the user and role type when registering identity services
        ///     and DocumentDB stores.`
        /// </summary>
        /// <typeparam name="TUser">The type associated with user identity information.</typeparam>
        /// <typeparam name="TRole">The type associated with role identity information.</typeparam>
        /// <param name="service">The <see cref="IdentityBuilder"/> to build upon.</param>
        /// <param name="documentDbOptions">The options for creating the DocumentDB client.</param>
        /// <param name="identityOptions">The identity options used when calling AddIdentity.</param>
        /// <returns>The <see cref="IdentityBuilder"/> with the DocumentDB settings applied.</returns>
        public static IdentityBuilder AddIdentityWithDocumentDBStores<TUser, TRole>(
            this IServiceCollection service,
            IDocumentClient documentClient,
            Func<IServiceProvider, DocumentCollection> collectionFactory,
            Action<IdentityOptions> identityOptions = null)
            where TUser : IdentityUser
            where TRole : IdentityRole
        {
            return service.AddIdentity<TUser, TRole>(identityOptions)
                .RegisterDocumentDBStores<TUser, TRole>(documentClient, collectionFactory);
        }

        private static async Task<Database> CreateDatabaseIfNotExistsAsync(this IDocumentClient client, Database database)
        {
            try
            {
                database = (await client.ReadDatabaseAsync(UriFactory.CreateDatabaseUri(database.Id))).Resource;
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    database = (await client.CreateDatabaseAsync(database)).Resource;
                else
                    throw;
            }

            return database;
        }

        private static async Task<DocumentCollection> CreateDocumentCollectionIfNotExistsAsync(this IDocumentClient client, string databaseLink, DocumentCollection collection)
        {
            try
            {
                var databaseId = databaseLink.Replace("dbs/", "").Replace("/", "");
                collection = (await client.ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(databaseId, collection.Id))).Resource;
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    collection = (await client.CreateDocumentCollectionAsync(databaseLink, collection)).Resource;
                else
                    throw;
            }

            return collection;
        }
    }
}