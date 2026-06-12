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
        public DbSet<MaudauMarket> MaudauProducts => Set<MaudauMarket>();

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
            });

            modelBuilder.Entity<PriceHistory>(b =>
            {
                b.HasKey(p => p.Id);
            });

            modelBuilder.Entity<MaudauMarket>(b =>
            {
                b.ToTable("MaudauMarket");
                b.HasKey(m => m.Id);

                b.Property(m => m.Slug).IsRequired().HasMaxLength(512);
                b.Property(m => m.Title).IsRequired().HasMaxLength(1000);
                b.Property(m => m.Price).HasColumnType("decimal(18,2)");
                b.Property(m => m.OldPrice).HasColumnType("decimal(18,2)");

                b.HasIndex(m => m.ProductId).IsUnique().HasDatabaseName("IX_MaudauMarket_ProductId");
                b.HasIndex(m => m.Slug).HasDatabaseName("IX_MaudauMarket_Slug");
            });
        }
    }
}
