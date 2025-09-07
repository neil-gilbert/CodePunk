using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Data.Entities;
using CodePunk.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CodePunk.Data.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly CodePunkDbContext _context;

    public SessionRepository(CodePunkDbContext context)
    {
        _context = context;
    }

    public async Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity == null) return null;

        var liveCount = await _context.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == entity.Id)
            .CountAsync(cancellationToken);
        entity.MessageCount = liveCount;
        return entity.ToDomainModel();
    }

    public async Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Sessions
            .AsNoTracking()
            .Where(s => s.ParentSessionId == null)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
        if (entities.Count == 0) return Array.Empty<Session>();

        var ids = entities.Select(e => e.Id).ToList();
        var counts = await _context.Messages
            .AsNoTracking()
            .Where(m => ids.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        foreach (var e in entities)
        {
            if (counts.TryGetValue(e.Id, out var c))
                e.MessageCount = c;
            else
                e.MessageCount = 0;
        }

        return entities.Select(e => e.ToDomainModel()).ToList();
    }

    public async Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        var entity = session.FromDomainModel();
        _context.Sessions.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.ToDomainModel();
    }

    public async Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == session.Id, cancellationToken);
            
        if (entity == null)
            throw new InvalidOperationException($"Session {session.Id} not found");

        entity.UpdateFromDomainModel(session);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.ToDomainModel();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity != null)
        {
            _context.Sessions.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
