using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Data.Entities;
using CodePunk.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CodePunk.Data.Repositories;

public class FileHistoryRepository : IFileHistoryRepository
{
    private readonly CodePunkDbContext _context;

    public FileHistoryRepository(CodePunkDbContext context)
    {
        _context = context;
    }

    public async Task<SessionFile?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        return entity?.ToDomainModel();
    }

    public async Task<IReadOnlyList<SessionFile>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Files
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId)
            .OrderBy(f => f.Path)
            .ThenByDescending(f => f.Version)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomainModel()).ToList();
    }

    public async Task<SessionFile?> GetLatestVersionAsync(string sessionId, string path, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Files
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && f.Path == path)
            .OrderByDescending(f => f.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return entity?.ToDomainModel();
    }

    public async Task<SessionFile> CreateAsync(SessionFile file, CancellationToken cancellationToken = default)
    {
        var entity = file.FromDomainModel();
        _context.Files.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.ToDomainModel();
    }

    public async Task<SessionFile> UpdateAsync(SessionFile file, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Files
            .FirstOrDefaultAsync(f => f.Id == file.Id, cancellationToken);
            
        if (entity == null)
            throw new InvalidOperationException($"SessionFile {file.Id} not found");

        entity.UpdateFromDomainModel(file);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.ToDomainModel();
    }
}
