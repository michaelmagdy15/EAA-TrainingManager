using System;

namespace EAATrainingManager.Models;

public sealed class DispatchReleaseRecord
{
    public int Id { get; set; }
    public int TrainingSessionId { get; set; }
    public string ReleaseReference { get; set; } = string.Empty;
    public string WeatherBriefing { get; set; } = string.Empty;
    public string FlightInformationFile { get; set; } = string.Empty;
    public string ChecklistJson { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? ReleasedByUserId { get; set; }
    public string? SessionId { get; set; }
    public int? LocationId { get; set; }
    public DateTime ReleasedAt { get; set; }
}
