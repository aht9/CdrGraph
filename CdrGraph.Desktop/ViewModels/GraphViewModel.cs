using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using SkiaSharp;

namespace CdrGraph.Desktop.ViewModels;

public class GraphViewModel : ObservableObject
{
    public List<GraphNode> Nodes { get; }
    public List<GraphEdge> Edges { get; }

    // وضعیت دوربین (برای بایندینگ به UI اگر نیاز شد، فعلا در Code-behind مدیریت می‌شود)
    private string _statusText;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public GraphViewModel(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
        StatusText = $"{nodes.Count} Nodes, {edges.Count} Edges";
    }
    
}