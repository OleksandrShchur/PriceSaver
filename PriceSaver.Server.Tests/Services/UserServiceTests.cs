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
    }
}
