namespace CdrGraph.Core.Domain.Models;

public class GraphEdge
{
    public string Id { get; }
    public string SourceId { get; }
    public string TargetId { get; }
        
    // Metrics
    public int CallCount { get; set; }
    public double TotalDurationMinutes { get; set; }
        
    // Visual Properties
    public double CalculatedWeight { get; set; } // 0.0 to 1.0 (Normalised)
    public float Thickness { get; set; }        // Pixel width

    public GraphEdge(string sourceId, string targetId)
    {
        SourceId = sourceId;
        TargetId = targetId;
        Id = $"{sourceId}_{targetId}"; // Composite Key
    }

    public void AddInteraction(double duration)
    {
        CallCount++;
        TotalDurationMinutes += duration;
    }
}