using PriceSaver.Server.Data;

namespace PriceSaver.Server.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _db;

        public UserService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task EnsureUserExistsAsync(long telegramId, string? username, CancellationToken cancellationToken)
        {
            var user = await _db.Users.FindAsync([telegramId], cancellationToken);
            if (user is null)
            {
                _db.Users.Add(new Models.User
                {
                    TelegramId = telegramId,
                    Username = username
                });
            }
            else if (!string.Equals(user.Username, username, StringComparison.Ordinal) && username is not null)
            {
                user.Username = username;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
