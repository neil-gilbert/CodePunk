using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace CodePunk.Data.Entities;

/// <summary>
/// EF entity for messages table
/// </summary>
public class MessageEntity
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Parts { get; set; } = "[]"; // JSON array
    public string? Model { get; set; }
    public string? Provider { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? FinishedAt { get; set; }

    public virtual SessionEntity Session { get; set; } = null!;
}

public class MessageConfiguration : IEntityTypeConfiguration<MessageEntity>
{
    public void Configure(EntityTypeBuilder<MessageEntity> builder)
    {
        builder.ToTable("messages");
        
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(50);
        builder.Property(e => e.SessionId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Role).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Parts).IsRequired();
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        
        builder.HasIndex(e => e.SessionId);
        builder.HasIndex(e => e.CreatedAt);
        
        builder.HasOne(e => e.Session)
            .WithMany(e => e.Messages)
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
