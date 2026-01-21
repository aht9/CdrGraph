using System.Windows;
using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;

namespace CdrGraph.Desktop.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly IExcelReaderService _excelService;
    private readonly IGraphLayoutService _layoutService;

    private object _currentView;

    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public MainViewModel(IExcelReaderService excelService, IGraphLayoutService layoutService)
    {
        _excelService = excelService;
        _layoutService = layoutService;

        // شروع برنامه با صفحه ایمپورت
        // نکته: چون ImportViewModel نیاز به MainViewModel دارد، اینجا دستی می‌سازیم یا از Factory استفاده می‌کنیم
        // برای سادگی اینجا دستی پاس می‌دهیم (در پروژه بزرگتر از NavigationService استفاده می‌شود)
        CurrentView = new ImportViewModel(_excelService, this);
    }

    public async Task StartGraphProcessingAsync(string filePath, ColumnMapping mapping)
    {
        try
        {
            // 1. خواندن داده‌ها
            var records = await _excelService.ParseFileAsync(filePath, mapping);

            // 2. تبدیل داده‌های خام به گراف (Aggregation Logic)
            // این لاجیک را بعداً به یک سرویس جداگانه (GraphService) منتقل می‌کنیم
            var (nodes, edges) = ProcessData(records);

            // 3. محاسبه چیدمان (Heavy Calculation)
            // اینجا منتظر می‌مانیم (یا لودینگ نمایش می‌دهیم)
            await _layoutService.ApplyLayoutAsync(nodes, edges);

            // 4. تغییر صفحه به نمایش گراف
            CurrentView = new GraphViewModel(nodes, edges); 
            MessageBox.Show($"Graph Ready! Nodes: {nodes.Count}, Edges: {edges.Count}");
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Analysis Error: {ex.Message}");
        }
    }

    // منطق ساده تبدیل رکوردها به گراف (موقت در اینجا)
    private (List<GraphNode> nodes, List<GraphEdge> edges) ProcessData(IEnumerable<CdrRecord> records)
    {
        var nodeDict = new Dictionary<string, GraphNode>();
        var edgeDict = new Dictionary<string, GraphEdge>();

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.SourceNumber) || string.IsNullOrWhiteSpace(r.TargetNumber)) continue;

            // Create/Get Nodes
            if (!nodeDict.ContainsKey(r.SourceNumber)) nodeDict[r.SourceNumber] = new GraphNode(r.SourceNumber);
            if (!nodeDict.ContainsKey(r.TargetNumber)) nodeDict[r.TargetNumber] = new GraphNode(r.TargetNumber);

            // Update Node Metrics
            nodeDict[r.SourceNumber].AddMetrics(1, r.DurationSeconds);
            nodeDict[r.TargetNumber].AddMetrics(1, r.DurationSeconds);

            // Create/Get Edge
            // کلید یکتا: همیشه کوچکتر-بزرگتر (برای گراف بدون جهت) یا جهت‌دار بسته به نیاز
            // اینجا فرض می‌کنیم جهت مهم است:
            var edgeKey = $"{r.SourceNumber}_{r.TargetNumber}";

            if (!edgeDict.ContainsKey(edgeKey))
                edgeDict[edgeKey] = new GraphEdge(r.SourceNumber, r.TargetNumber);

            edgeDict[edgeKey].AddInteraction(r.DurationSeconds);
        }

        // نرمال‌سازی وزن‌ها برای نمایش
        var edges = edgeDict.Values.ToList();
        if (edges.Any())
        {
            var maxCalls = edges.Max(e => e.CallCount);
            foreach (var e in edges)
            {
                // وزن خط: ترکیبی از تعداد تماس و مدت زمان (فعلا فقط تعداد برای سادگی)
                e.CalculatedWeight = (double)e.CallCount / maxCalls;
                e.Thickness = (float)(1 + (e.CalculatedWeight * 5)); // ضخامت بین 1 تا 6 پیکسل
            }
        }

        return (nodeDict.Values.ToList(), edges);
    }
}