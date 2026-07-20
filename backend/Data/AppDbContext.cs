using DockerVm.Models;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Data;

public class AppDbContext : DbContext
{
    public DbSet<VmContainer> Containers => Set<VmContainer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<QuotaState> QuotaStates => Set<QuotaState>();
    public DbSet<UserQuotaBonus> UserQuotaBonuses => Set<UserQuotaBonus>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VmContainer>(e =>
        {
            e.HasKey(x => x.Key);
            e.HasIndex(x => x.HostPort);
            e.HasIndex(x => x.ContainerName);
            e.HasIndex(x => x.UserId);   // 按用户查容器的高频索引
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.Password).HasMaxLength(128);
            e.Property(x => x.UserId).HasMaxLength(64);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(32);
            e.Property(x => x.Id).HasMaxLength(64);
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ExpiresAt);  // 便于后续按过期清理
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
        });

        modelBuilder.Entity<QuotaState>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<UserQuotaBonus>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.Note).HasMaxLength(128);
        });
    }
}
