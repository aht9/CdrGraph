namespace CdrGraph.Core.Domain.Models;

using System.Collections.Generic;

public class GraphNode
{
    public string Id { get; private set; }
    public string Label { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public double Weight { get; set; }
    public int TotalCalls { get; set; }
    public double TotalDurationMinutes { get; set; }

    // ویژگی جدید: رنگ نود (Hex Code)
    public string Color { get; set; } = "#1E90FF"; // پیش‌فرض آبی

    public List<CdrRecord> RelatedRecords { get; } = new List<CdrRecord>();

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

    public void AddRecord(CdrRecord record)
    {
        RelatedRecords.Add(record);
    }
}