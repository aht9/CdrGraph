using System.Collections.ObjectModel;
using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using SkiaSharp;

namespace CdrGraph.Desktop.ViewModels;

public class GraphViewModel : ObservableObject
{
    public List<GraphNode> Nodes { get; }
    public List<GraphEdge> Edges { get; }
    public string StatusText { get; set; }

    // نود انتخاب شده
    private GraphNode _selectedNode;
    public GraphNode SelectedNode 
    { 
        get => _selectedNode; 
        set 
        {
            if(SetProperty(ref _selectedNode, value))
            {
                UpdateDetailView();
            }
        }
    }

    // لیستی که به DataGrid بایند می‌شود
    public ObservableCollection<RecordDisplayModel> SelectedNodeRecords { get; } = new ObservableCollection<RecordDisplayModel>();

    public GraphViewModel(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
        StatusText = $"{nodes.Count} Nodes, {edges.Count} Edges";
    }

    private void UpdateDetailView()
    {
        SelectedNodeRecords.Clear();
        if (SelectedNode == null) return;

        foreach (var record in SelectedNode.RelatedRecords)
        {
            SelectedNodeRecords.Add(new RecordDisplayModel
            {
                Source = record.SourceNumber,
                Target = record.TargetNumber,
                Duration = record.DurationSeconds,
                File = record.OriginFileName,
                Date = record.DateStr, 
                Time = record.TimeStr 
            });
        }
    }
}

public class RecordDisplayModel
{
    public string Source { get; set; }
    public string Target { get; set; }
    public double Duration { get; set; }
    public string File { get; set; }
    
    public string Date { get; set; }
    public string Time { get; set; }
}