namespace CdrGraph.Core.Domain.Models;

public class CdrRecord
{
    public string SourceNumber { get; set; }
    public string TargetNumber { get; set; }
    public DateTime CallDate { get; set; }
    public double DurationSeconds { get; set; }
    public string CallType { get; set; } // Incoming, Outgoing
}