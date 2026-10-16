using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Infrastructure.Services;

/// <summary>Utilisateur courant (session en mémoire).</summary>
public sealed class CurrentUser : ICurrentUser
{
    public string? UserId { get; private set; }
    public string? UserName { get; private set; }
    public short? RoleId { get; private set; }
    public string? RoleCode { get; private set; }
    public short? BorderPointId { get; private set; }
    public string MachineName { get; } = Environment.MachineName;
    public string? PcNumber { get; private set; }
    public bool IsAuthenticated => UserId is not null;

    public void Set(User user, string roleCode)
    {
        UserId = user.Id;
        UserName = user.UserName;
        RoleId = user.UserRoleId;
        RoleCode = roleCode;
        BorderPointId = user.BorderPointId;
    }

    public void SetPcNumber(string? pcNumber) => PcNumber = pcNumber;

    public void Clear()
    {
        UserId = null;
        UserName = null;
        RoleId = null;
        RoleCode = null;
        BorderPointId = null;
    }
}

/// <summary>Horloge système.</summary>
public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

/// <summary>Écriture du journal d'audit (table AuditLog).</summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly BorderNextDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AuditWriter(BorderNextDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task WriteAsync(string eventType, string? entity = null, string? entityId = null, string? details = null, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EventType = eventType,
            UserId = _currentUser.UserId,
            MachineName = _currentUser.MachineName,
            PcNumber = _currentUser.PcNumber,
            Entity = entity,
            EntityId = entityId,
            Details = details,
            EventTime = _clock.Now
        });
        await _db.SaveChangesAsync(ct);
    }
}
