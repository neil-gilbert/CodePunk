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
        return entity?.ToDomainModel();
    }

    public async Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Sessions
            .AsNoTracking()
            .Where(s => s.ParentSessionId == null)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

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
        // Use tracked entity for updates (can't use AsNoTracking here)
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
        // Efficient delete without loading the entity
        await _context.Sessions
            .Where(s => s.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
