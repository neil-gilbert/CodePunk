using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Data.Entities;
using CodePunk.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CodePunk.Data.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly CodePunkDbContext _context;

    public MessageRepository(CodePunkDbContext context)
    {
        _context = context;
    }

    public async Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return entity?.ToDomainModel();
    }

    public async Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomainModel()).ToList();
    }

    public async Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default)
    {
        var entity = message.FromDomainModel();
        _context.Messages.Add(entity);

        // Increment session message count (best-effort)
        var session = await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == message.SessionId, cancellationToken);
        if (session != null)
        {
            session.MessageCount += 1;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return entity.ToDomainModel();
    }

    public async Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == message.Id, cancellationToken);
            
        if (entity == null)
            throw new InvalidOperationException($"Message {message.Id} not found");

        entity.UpdateFromDomainModel(message);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.ToDomainModel();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Messages.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity != null)
        {
            // Decrement session message count if possible
            var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Id == entity.SessionId, cancellationToken);
            if (session != null && session.MessageCount > 0)
            {
                session.MessageCount -= 1;
            }

            _context.Messages.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        // Count messages first so we can adjust session MessageCount accurately
        var messageQuery = _context.Messages.Where(m => m.SessionId == sessionId);
        var count = await messageQuery.CountAsync(cancellationToken);

        if (count > 0)
        {
            // Fallback approach instead of ExecuteDeleteAsync (not available in current EF runtime)
            var toRemove = await messageQuery.ToListAsync(cancellationToken);
            if (toRemove.Count > 0)
            {
                _context.Messages.RemoveRange(toRemove);
            }

            var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
            if (session != null)
            {
                session.MessageCount = 0; // reset; live queries recompute if needed
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
