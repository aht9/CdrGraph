namespace CdrGraph.Core.Domain.Models;

public class GraphEdge
{
    public string Id { get; }
    public string SourceId { get; }
    public string TargetId { get; }

    public int CallCount { get; set; }
    public double TotalDurationMinutes { get; set; }

    public double CalculatedWeight { get; set; }
    public float Thickness { get; set; }

    // --- ویژگی‌های جدید برای جابجایی خط ---
    // فاصله نقطه کنترل منحنی از حالت پیش‌فرض
    public float ControlPointOffsetX { get; set; } = 0;
    public float ControlPointOffsetY { get; set; } = 0;

    public GraphEdge(string sourceId, string targetId)
    {
        SourceId = sourceId;
        TargetId = targetId;
        Id = $"{sourceId}_{targetId}";
    }

    public void AddInteraction(double duration)
    {
        CallCount++;
        TotalDurationMinutes += duration;
    }
}