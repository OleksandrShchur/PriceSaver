using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;

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
                _db.Users.Add(new User
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

        public async Task<User?> GetAsync(long telegramId, CancellationToken cancellationToken)
        {
            return await _db.Users.FindAsync([telegramId], cancellationToken);
        }

        public async Task<bool> HasLocationAsync(long telegramId, CancellationToken cancellationToken)
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);

            return user is { Latitude: not null, Longitude: not null };
        }

        public async Task SetConversationStateAsync(
            long telegramId,
            string state,
            string? payload,
            CancellationToken cancellationToken)
        {
            var user = await _db.Users.FindAsync([telegramId], cancellationToken)
                ?? throw new InvalidOperationException($"User {telegramId} not found.");

            user.ConversationState = state;
            user.ConversationPayload = payload;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task SaveLocationAsync(
            long telegramId,
            decimal latitude,
            decimal longitude,
            string locationName,
            CancellationToken cancellationToken)
        {
            var user = await _db.Users.FindAsync([telegramId], cancellationToken)
                ?? throw new InvalidOperationException($"User {telegramId} not found.");

            user.Latitude = latitude;
            user.Longitude = longitude;
            user.LocationName = locationName;
            user.LocationUpdatedAt = DateTime.UtcNow;
            user.ConversationState = ConversationStates.None;
            user.ConversationPayload = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
