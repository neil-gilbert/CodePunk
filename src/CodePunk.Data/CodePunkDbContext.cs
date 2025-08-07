using CodePunk.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodePunk.Data;

public class CodePunkDbContext : DbContext
{
    public CodePunkDbContext(DbContextOptions<CodePunkDbContext> options) : base(options)
    {
    }

    public DbSet<SessionEntity> Sessions { get; set; }
    public DbSet<MessageEntity> Messages { get; set; }
    public DbSet<SessionFileEntity> Files { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply all configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CodePunkDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        
        // Performance optimizations for SQLite
        if (optionsBuilder.Options.Extensions.Any(e => e.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)))
        {
            optionsBuilder.EnableServiceProviderCaching();
            optionsBuilder.EnableSensitiveDataLogging(false); // Disable in production
        }
    }

    // Optimized SaveChanges - only update timestamps on entities that actually changed
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    private void UpdateTimestamps()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Only process entities that are actually being added or modified
        foreach (var entry in ChangeTracker.Entries().Where(e => 
            e.State == EntityState.Added || e.State == EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case SessionEntity sessionEntity when entry.State == EntityState.Added:
                    sessionEntity.CreatedAt = now;
                    sessionEntity.UpdatedAt = now;
                    break;
                case SessionEntity sessionEntity when entry.State == EntityState.Modified:
                    sessionEntity.UpdatedAt = now;
                    break;
                case MessageEntity messageEntity when entry.State == EntityState.Added:
                    messageEntity.CreatedAt = now;
                    messageEntity.UpdatedAt = now;
                    break;
                case MessageEntity messageEntity when entry.State == EntityState.Modified:
                    messageEntity.UpdatedAt = now;
                    break;
                case SessionFileEntity fileEntity when entry.State == EntityState.Added:
                    fileEntity.CreatedAt = now;
                    fileEntity.UpdatedAt = now;
                    break;
                case SessionFileEntity fileEntity when entry.State == EntityState.Modified:
                    fileEntity.UpdatedAt = now;
                    break;
            }
        }
    }
}
