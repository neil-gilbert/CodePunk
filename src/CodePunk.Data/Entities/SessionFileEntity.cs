using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace CodePunk.Data.Entities;

public class SessionFileEntity
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long Version { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public virtual SessionEntity Session { get; set; } = null!;
}

public class SessionFileConfiguration : IEntityTypeConfiguration<SessionFileEntity>
{
    public void Configure(EntityTypeBuilder<SessionFileEntity> builder)
    {
        builder.ToTable("files");
        
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(50);
        builder.Property(e => e.SessionId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Path).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.Content).IsRequired();
        
        builder.HasIndex(e => e.SessionId);
        builder.HasIndex(e => e.Path);
        builder.HasIndex(e => new { e.Path, e.SessionId, e.Version }).IsUnique();
        
        builder.HasOne(e => e.Session)
            .WithMany(e => e.Files)
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
