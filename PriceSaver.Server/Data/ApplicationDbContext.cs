using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Models;

namespace PriceSaver.Server.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
        public DbSet<StoreLocation> StoreLocations => Set<StoreLocation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.TelegramId);
                b.Property(u => u.Username).HasMaxLength(100);
                b.Property(u => u.LocationName).HasMaxLength(500);
                b.Property(u => u.Latitude).HasPrecision(9, 6);
                b.Property(u => u.Longitude).HasPrecision(9, 6);
                b.Property(u => u.ConversationState).HasMaxLength(32).HasDefaultValue(ConversationStates.None);
                b.Property(u => u.ConversationPayload).HasMaxLength(2000);
            });

            modelBuilder.Entity<Subscription>(b =>
            {
                b.HasKey(s => s.Id);
                b.Property(s => s.ProductUrl).IsRequired();
                b.Property(s => s.ProductName).HasMaxLength(500);
                b.HasIndex(s => new { s.UserId, s.ProductUrl }).IsUnique();
            });

            modelBuilder.Entity<PriceHistory>(b =>
            {
                b.HasKey(p => p.Id);
            });

            modelBuilder.Entity<StoreLocation>(b =>
            {
                b.HasKey(s => s.Id);
                b.Property(s => s.LocationName).IsRequired().HasMaxLength(500);
                b.Property(s => s.Latitude).HasPrecision(9, 6);
                b.Property(s => s.Longitude).HasPrecision(9, 6);
                b.HasIndex(s => s.StoreType);
                // CHECK (IsOnline = 1 OR coordinates present) lives in docs/sql/001_add_user_and_store_locations.sql
            });
        }
    }
}
