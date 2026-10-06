using System;

namespace EAATrainingManager.Models;

public enum PermissionLevel
{
    None = 0,
    ReadOnly = 1,
    LimitedEdit = 2,
    FullEdit = 3,
    Approve = 4
}

public sealed class UserAccount
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class UserSession
{
    public string SessionId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public sealed class LocationRecord
{
    public int Id { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class ApprovalRecord
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Transition { get; set; } = string.Empty;
    public int ApproverUserId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; }
    public DateTime ValidUntil { get; set; }
    public string EvidenceFingerprint { get; set; } = string.Empty;
}
