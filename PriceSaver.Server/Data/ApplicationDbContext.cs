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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.TelegramId);
                b.Property(u => u.Username).HasMaxLength(100);
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
        }
    }
}
