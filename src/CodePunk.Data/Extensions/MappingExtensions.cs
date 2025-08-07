using CodePunk.Core.Models;
using CodePunk.Data.Entities;
using System.Text.Json;

namespace CodePunk.Data.Extensions;

public static class MappingExtensions
{
    public static Session ToDomainModel(this SessionEntity entity)
    {
        return new Session
        {
            Id = entity.Id,
            ParentSessionId = entity.ParentSessionId,
            Title = entity.Title,
            MessageCount = entity.MessageCount,
            PromptTokens = entity.PromptTokens,
            CompletionTokens = entity.CompletionTokens,
            Cost = entity.Cost,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAt),
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAt),
            SummaryMessageId = entity.SummaryMessageId
        };
    }

    public static SessionEntity FromDomainModel(this Session session)
    {
        return new SessionEntity
        {
            Id = session.Id,
            ParentSessionId = session.ParentSessionId,
            Title = session.Title,
            MessageCount = session.MessageCount,
            PromptTokens = session.PromptTokens,
            CompletionTokens = session.CompletionTokens,
            Cost = session.Cost,
            CreatedAt = session.CreatedAt.ToUnixTimeMilliseconds(),
            UpdatedAt = session.UpdatedAt.ToUnixTimeMilliseconds(),
            SummaryMessageId = session.SummaryMessageId
        };
    }

    public static void UpdateFromDomainModel(this SessionEntity entity, Session session)
    {
        entity.ParentSessionId = session.ParentSessionId;
        entity.Title = session.Title;
        entity.MessageCount = session.MessageCount;
        entity.PromptTokens = session.PromptTokens;
        entity.CompletionTokens = session.CompletionTokens;
        entity.Cost = session.Cost;
        entity.SummaryMessageId = session.SummaryMessageId;
        // CreatedAt should not be updated
        // UpdatedAt will be set automatically by SaveChanges
    }

    public static Message ToDomainModel(this MessageEntity entity)
    {
        var parts = JsonSerializer.Deserialize<List<MessagePart>>(entity.Parts) ?? [];
        
        return new Message
        {
            Id = entity.Id,
            SessionId = entity.SessionId,
            Role = Enum.Parse<MessageRole>(entity.Role),
            Parts = parts,
            Model = entity.Model,
            Provider = entity.Provider,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAt),
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAt),
            FinishedAt = entity.FinishedAt.HasValue 
                ? DateTimeOffset.FromUnixTimeMilliseconds(entity.FinishedAt.Value) 
                : null
        };
    }

    public static MessageEntity FromDomainModel(this Message message)
    {
        return new MessageEntity
        {
            Id = message.Id,
            SessionId = message.SessionId,
            Role = message.Role.ToString(),
            Parts = JsonSerializer.Serialize(message.Parts),
            Model = message.Model,
            Provider = message.Provider,
            CreatedAt = message.CreatedAt.ToUnixTimeMilliseconds(),
            UpdatedAt = message.UpdatedAt.ToUnixTimeMilliseconds(),
            FinishedAt = message.FinishedAt?.ToUnixTimeMilliseconds()
        };
    }

    public static void UpdateFromDomainModel(this MessageEntity entity, Message message)
    {
        entity.Role = message.Role.ToString();
        entity.Parts = JsonSerializer.Serialize(message.Parts);
        entity.Model = message.Model;
        entity.Provider = message.Provider;
        entity.FinishedAt = message.FinishedAt?.ToUnixTimeMilliseconds();
        // CreatedAt should not be updated
        // UpdatedAt will be set automatically by SaveChanges
    }

    public static SessionFile ToDomainModel(this SessionFileEntity entity)
    {
        return new SessionFile
        {
            Id = entity.Id,
            SessionId = entity.SessionId,
            Path = entity.Path,
            Content = entity.Content,
            Version = entity.Version,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAt),
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAt)
        };
    }

    public static SessionFileEntity FromDomainModel(this SessionFile file)
    {
        return new SessionFileEntity
        {
            Id = file.Id,
            SessionId = file.SessionId,
            Path = file.Path,
            Content = file.Content,
            Version = file.Version,
            CreatedAt = file.CreatedAt.ToUnixTimeMilliseconds(),
            UpdatedAt = file.UpdatedAt.ToUnixTimeMilliseconds()
        };
    }

    public static void UpdateFromDomainModel(this SessionFileEntity entity, SessionFile file)
    {
        entity.Path = file.Path;
        entity.Content = file.Content;
        entity.Version = file.Version;
        // CreatedAt should not be updated
        // UpdatedAt will be set automatically by SaveChanges
    }
}
