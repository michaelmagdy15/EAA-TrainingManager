using System;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

public sealed class AvailabilityWindow
{
    public int Id { get; set; }
    public string ResourceType { get; set; } = "Resource";
    public string ResourceName { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string AvailabilityState { get; set; } = "Unavailable";
    public string Reason { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int? CreatedByUserId { get; set; }
    public string? SessionId { get; set; }
    public int? LocationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string DisplayText => $"{ResourceType}: {ResourceName} — {StartAt:HH:mm}-{EndAt:HH:mm} — {AvailabilityState} — {Reason}";
}
