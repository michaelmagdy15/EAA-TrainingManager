using System.Collections.Generic;

namespace EAATrainingManager.Models;

public sealed class FlightOperationsMetrics
{
    public int Scheduled { get; set; }
    public int Confirmed { get; set; }
    public int Released { get; set; }
    public int Airborne { get; set; }
    public int Landed { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int NoShow { get; set; }
    public double CompletedFlightHours { get; set; }
    public Dictionary<string, double> HoursByResource { get; set; } = new();
    public List<string> CancellationReasons { get; set; } = new();
}
