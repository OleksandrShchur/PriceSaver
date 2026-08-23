using PriceSaver.Server.Models;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class UserServiceTests
    {
        [Fact]
        public async Task EnsureUserExistsAsync_CreatesNewUser_WhenMissing()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var sut = new UserService(db);

            await sut.EnsureUserExistsAsync(100, "alice", CancellationToken.None);

            var user = db.Users.Single();
            user.TelegramId.Should().Be(100);
            user.Username.Should().Be("alice");
            user.ConversationState.Should().Be(ConversationStates.None);
        }

        [Fact]
        public async Task EnsureUserExistsAsync_UpdatesUsername_WhenChanged()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Users.Add(new User { TelegramId = 100, Username = "old" });
            await db.SaveChangesAsync();

            var sut = new UserService(db);

            await sut.EnsureUserExistsAsync(100, "new", CancellationToken.None);

            db.Users.Single(u => u.TelegramId == 100).Username.Should().Be("new");
        }

        [Fact]
        public async Task EnsureUserExistsAsync_IsNoOp_WhenUsernameUnchanged()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Users.Add(new User { TelegramId = 100, Username = "same" });
            await db.SaveChangesAsync();

            var sut = new UserService(db);

            await sut.EnsureUserExistsAsync(100, "same", CancellationToken.None);

            db.Users.Should().ContainSingle();
            db.Users.Single().Username.Should().Be("same");
        }

        [Fact]
        public async Task EnsureUserExistsAsync_KeepsExistingUsername_WhenNullProvided()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Users.Add(new User { TelegramId = 100, Username = "keep" });
            await db.SaveChangesAsync();

            var sut = new UserService(db);

            await sut.EnsureUserExistsAsync(100, null, CancellationToken.None);

            db.Users.Single().Username.Should().Be("keep");
        }

        [Fact]
        public async Task HasLocationAsync_ReturnsFalse_WhenCoordinatesMissing()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Users.Add(new User { TelegramId = 100, Username = "u" });
            await db.SaveChangesAsync();

            var sut = new UserService(db);

            (await sut.HasLocationAsync(100, CancellationToken.None)).Should().BeFalse();
        }

        [Fact]
        public async Task SaveLocationAsync_PersistsCoordinates_AndClearsConversation()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Users.Add(new User
            {
                TelegramId = 100,
                Username = "u",
                ConversationState = ConversationStates.AwaitingLocationConfirm,
                ConversationPayload = "[]"
            });
            await db.SaveChangesAsync();

            var sut = new UserService(db);

            await sut.SaveLocationAsync(100, 50.45m, 30.52m, "Kyiv", CancellationToken.None);

            var user = db.Users.Single();
            user.Latitude.Should().Be(50.45m);
            user.Longitude.Should().Be(30.52m);
            user.LocationName.Should().Be("Kyiv");
            user.LocationUpdatedAt.Should().NotBeNull();
            user.ConversationState.Should().Be(ConversationStates.None);
            user.ConversationPayload.Should().BeNull();
            (await sut.HasLocationAsync(100, CancellationToken.None)).Should().BeTrue();
        }

        [Fact]
        public async Task SetConversationStateAsync_StoresPayload()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Users.Add(new User { TelegramId = 100 });
            await db.SaveChangesAsync();

            var sut = new UserService(db);

            await sut.SetConversationStateAsync(
                100,
                ConversationStates.AwaitingLocationConfirm,
                "[{\"displayName\":\"A\"}]",
                CancellationToken.None);

            var user = await sut.GetAsync(100, CancellationToken.None);
            user!.ConversationState.Should().Be(ConversationStates.AwaitingLocationConfirm);
            user.ConversationPayload.Should().Contain("displayName");
        }
    }
}
