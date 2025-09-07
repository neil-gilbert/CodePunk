using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace CodePunk.Data.Entities;

/// <summary>
/// EF entity for sessions table
/// </summary>
public class SessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string? ParentSessionId { get; set; }
    
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    public int MessageCount { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public decimal Cost { get; set; }
    public long CreatedAt { get; set; } // Unix timestamp
    public long UpdatedAt { get; set; } // Unix timestamp
    public string? SummaryMessageId { get; set; }

    public virtual ICollection<MessageEntity> Messages { get; set; } = [];
    public virtual ICollection<SessionFileEntity> Files { get; set; } = [];
    public virtual SessionEntity? ParentSession { get; set; }
    public virtual ICollection<SessionEntity> ChildSessions { get; set; } = [];
}

/// <summary>
/// EF configuration for SessionEntity
/// </summary>
public class SessionConfiguration : IEntityTypeConfiguration<SessionEntity>
{
    public void Configure(EntityTypeBuilder<SessionEntity> builder)
    {
        builder.ToTable("sessions");
        
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(50);
        
        builder.Property(e => e.ParentSessionId).HasMaxLength(50);
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SummaryMessageId).HasMaxLength(50);
        
        builder.Property(e => e.Cost).HasColumnType("decimal(18,6)");
        
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.ParentSessionId);
        
        builder.HasOne(e => e.ParentSession)
            .WithMany(e => e.ChildSessions)
            .HasForeignKey(e => e.ParentSessionId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasMany(e => e.Messages)
            .WithOne(e => e.Session)
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(e => e.Files)
            .WithOne(e => e.Session)
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
