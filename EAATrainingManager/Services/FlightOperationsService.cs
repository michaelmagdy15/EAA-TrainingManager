using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EAATrainingManager.Services;

public sealed record DispatchReleaseRequest(
    string WeatherBriefing,
    string FlightInformationFile,
    string Reason,
    IReadOnlyDictionary<string, bool> Checklist);

public sealed class FlightOperationsService
{
    public static IReadOnlyList<string> RequiredReleaseChecks { get; } =
    [
        "PilotIdentityConfirmed",
        "WeatherBriefingReviewed",
        "ResourceStatusConfirmed",
        "FlightInformationFileReviewed"
    ];

    private readonly DatabaseService _database;

    public FlightOperationsService(DatabaseService database)
    {
        _database = database;
    }

    public Task<bool> TransitionAsync(int sessionId, string targetStatus)
    {
        return _database.UpdateTrainingSessionStatusAsync(sessionId, targetStatus);
    }

    public Task<bool> ResolveAsync(int sessionId, string outcome, string reason)
    {
        return _database.ResolveTrainingSessionAsync(sessionId, outcome, reason);
    }

    public Task<int> ReleaseAsync(int sessionId, DispatchReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.WeatherBriefing))
            throw new ArgumentException("A weather briefing summary is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.FlightInformationFile))
            throw new ArgumentException("A flight-information-file reference is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 8)
            throw new ArgumentException("A release reason of at least eight characters is required.", nameof(request));

        string[] incompleteChecks = RequiredReleaseChecks
            .Where(check => !request.Checklist.TryGetValue(check, out bool complete) || !complete)
            .ToArray();
        if (incompleteChecks.Length > 0)
            throw new InvalidOperationException($"Dispatch release is blocked until all checks are complete: {string.Join(", ", incompleteChecks)}.");

        string checklistJson = JsonSerializer.Serialize(request.Checklist);
        return _database.ReleaseTrainingSessionAsync(sessionId, request.FlightInformationFile, request.WeatherBriefing, checklistJson, request.Reason);
    }
}
