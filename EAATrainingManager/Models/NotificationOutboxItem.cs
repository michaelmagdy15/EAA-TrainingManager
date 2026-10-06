using System;

namespace EAATrainingManager.Models;

public sealed class NotificationOutboxItem
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string RecipientType { get; set; } = string.Empty;
    public string RecipientKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
