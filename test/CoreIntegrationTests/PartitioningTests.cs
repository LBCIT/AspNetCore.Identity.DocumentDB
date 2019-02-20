using AspNetCore.Identity.DocumentDB;
using IntegrationTests;
using Microsoft.AspNetCore.Identity.DocumentDB;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CoreIntegrationTests
{
    public class PartitioningTests : UserIntegrationTestsBase
    {
        private static readonly PartitionKeyDefinition partitionKey = new PartitionKeyDefinition
        {
            Paths = new Collection<string> { "/partition" }
        };

        private readonly string userMappingEmailStr = Helper.GetEnumMemberValue(TypeEnum.UserMappingEmail);

        private readonly string userMappingUsernameStr = Helper.GetEnumMemberValue(TypeEnum.UserMappingUsername);

        private readonly string roleMappingStr = Helper.GetEnumMemberValue(TypeEnum.RoleMapping);

        public PartitioningTests()
            : base(partitionKey)
        { }

        [Fact]
        public async Task User_CreateAsync_UserNameSet()
        {
            var userManager = GetUserManager();

            // create user
            var user1 = new IdentityUser { UserName = "user1" };
            var user2 = new IdentityUser { UserName = "user2" };
            await userManager.CreateAsync(user1);
            await userManager.CreateAsync(user2);

            // check query on collection
            var results = Client.CreateDocumentQuery(Users.DocumentsLink, new FeedOptions { EnableCrossPartitionQuery = true }).ToList();

            // 2 documents per user (main document & username mapping, no email mapping because none was given)
            Assert.Equal(4, results.Count);

            var dbUser1 = (IdentityUser)(dynamic)results[0];
            Assert.Equal(user1.Id, dbUser1.Id);
            var mappingUsername1 = (dynamic)results[1];
            Assert.Equal(userMappingUsernameStr, mappingUsername1.Id);
            Assert.Equal(dbUser1.NormalizedUserName, mappingUsername1.partition);
            Assert.Equal(dbUser1.Id, mappingUsername1.targetId);

            var dbUser2 = (IdentityUser)(dynamic)results[2];
            Assert.Equal(user2.Id, dbUser2.Id);
            var mappingUsername2 = (dynamic)results[3];
            Assert.Equal(userMappingUsernameStr, mappingUsername2.Id);
            Assert.Equal(dbUser2.NormalizedUserName, mappingUsername2.partition);
            Assert.Equal(dbUser2.Id, mappingUsername2.targetId);

            // .NET Identity Framework - get all results
            var userList = userManager.Users.ToList();
            Assert.Equal(2, userList.Count);
        }

        [Fact]
        public async Task User_CreateAsync_UserNameAndEmailSet()
        {
            var userManager = GetUserManager();

            // create user
            var user1 = new IdentityUser { UserName = "user1", Email = "test1@test.test" };
            var user2 = new IdentityUser { UserName = "user2", Email = "test2@test.test" };
            await userManager.CreateAsync(user1);
            await userManager.CreateAsync(user2);

            // check query on collection
            var results = Client.CreateDocumentQuery(Users.DocumentsLink, new FeedOptions { EnableCrossPartitionQuery = true }).ToList();
            Assert.Equal(6, results.Count); // 2 documents per user

            var dbUser1 = (IdentityUser)(dynamic)results[0];
            Assert.Equal(user1.Id, dbUser1.Id);
            var mappingUsername1 = (dynamic)results[1];
            Assert.Equal(userMappingUsernameStr, mappingUsername1.Id);
            Assert.Equal(dbUser1.NormalizedUserName, mappingUsername1.partition);
            Assert.Equal(dbUser1.Id, mappingUsername1.targetId);
            var mappingEmail1 = (dynamic)results[2];
            Assert.Equal(userMappingEmailStr, mappingEmail1.Id);
            Assert.Equal(dbUser1.NormalizedEmail, mappingEmail1.partition);
            Assert.Equal(dbUser1.Id, mappingEmail1.targetId);

            var dbUser2 = (IdentityUser)(dynamic)results[3];
            Assert.Equal(user2.Id, dbUser2.Id);
            var mappingUsername2 = (dynamic)results[4];
            Assert.Equal(userMappingUsernameStr, mappingUsername2.Id);
            Assert.Equal(dbUser2.NormalizedUserName, mappingUsername2.partition);
            Assert.Equal(dbUser2.Id, mappingUsername2.targetId);
            var mappingEmail2 = (dynamic)results[5];
            Assert.Equal(userMappingEmailStr, mappingEmail2.Id);
            Assert.Equal(dbUser2.NormalizedEmail, mappingEmail2.partition);
            Assert.Equal(dbUser2.Id, mappingEmail2.targetId);

            // .NET Identity Framework - get all results
            var userList = userManager.Users.ToList();
            Assert.Equal(2, userList.Count);
        }

        [Fact]
        public async Task User_FindById()
        {
            var userManager = GetUserManager();

            // create user
            var user1 = new IdentityUser { UserName = "user1", Email = "test1@test.test" };
            await userManager.CreateAsync(user1);

            var userFound = await userManager.FindByIdAsync(user1.Id);

            Assert.NotNull(userFound);
            Assert.Equal(user1.Id, userFound.Id);
            Assert.Equal(user1.UserName, userFound.UserName);
            Assert.Equal(user1.Email, userFound.Email);
        }

        [Fact]
        public async Task User_FindByName()
        {
            var userManager = GetUserManager();

            // create user
            var user1 = new IdentityUser { UserName = "user1", Email = "test1@test.test" };
            await userManager.CreateAsync(user1);

            var userFound = await userManager.FindByNameAsync(user1.UserName);

            Assert.NotNull(userFound);
            Assert.Equal(user1.Id, userFound.Id);
            Assert.Equal(user1.UserName, userFound.UserName);
            Assert.Equal(user1.Email, userFound.Email);
        }

        [Fact]
        public async Task User_FindByEmail()
        {
            var userManager = GetUserManager();

            // create user
            var user1 = new IdentityUser { UserName = "user1", Email = "test1@test.test" };
            await userManager.CreateAsync(user1);

            var userFound = await userManager.FindByEmailAsync(user1.Email);

            Assert.NotNull(userFound);
            Assert.Equal(user1.Id, userFound.Id);
            Assert.Equal(user1.UserName, userFound.UserName);
            Assert.Equal(user1.Email, userFound.Email);
        }

    }
}
