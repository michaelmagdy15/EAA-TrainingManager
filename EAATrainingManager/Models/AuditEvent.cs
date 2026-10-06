using System;

namespace EAATrainingManager.Models;

/// <summary>Immutable operational evidence for regulatory review and troubleshooting.</summary>
public class AuditEvent
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Actor { get; set; } = "Local Operator";
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    public string? SessionId { get; set; }
    public int? LocationId { get; set; }
    public int VersionNo { get; set; } = 1;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string DisplayText => $"{OccurredAt:yyyy/MM/dd HH:mm} — {Action}: {Summary}";
}
