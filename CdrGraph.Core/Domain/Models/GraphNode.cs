namespace CdrGraph.Core.Domain.Models;

public class GraphNode
{
    public string Id { get; private set; }
    public string Label { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
        
    public double Weight { get; set; }
    public int TotalCalls { get; set; }
    public double TotalDurationMinutes { get; set; }

    public bool IsSelected { get; set; }
    public bool IsVisible { get; set; } = true;

    // لیستی از تمام رکوردهایی که این شماره در آن‌ها (به عنوان مبدا یا مقصد) حضور داشته
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