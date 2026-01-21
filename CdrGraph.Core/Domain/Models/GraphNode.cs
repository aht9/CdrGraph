namespace CdrGraph.Core.Domain.Models;

public class GraphNode
{
    public string Id { get; private set; } // Phone Number (Unique Key)
    public string Label { get; set; }      // Display Name
        
    // Coordinates for Rendering (Mutable as layout changes)
    public float X { get; set; }
    public float Y { get; set; }
        
    // Metrics
    public double Weight { get; set; }     // Calculated importance
    public int TotalCalls { get; set; }
    public double TotalDurationMinutes { get; set; }

    // State
    public bool IsSelected { get; set; }
    public bool IsVisible { get; set; } = true;

    public GraphNode(string id)
    {
        Id = id;
        Label = id;
    }

    public void AddMetrics(int calls, double duration)
    {
        TotalCalls += calls;
        TotalDurationMinutes += duration;
    }
}