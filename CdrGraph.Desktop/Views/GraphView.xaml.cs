using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using CdrGraph.Desktop.ViewModels;
using CdrGraph.Core.Domain.Models;
using Microsoft.Win32;

namespace CdrGraph.Desktop.Views;

public partial class GraphView : UserControl
{
    // وضعیت دوربین (Zoom/Pan)
    private float _scale = 1.0f;
    private SKPoint _offset = new SKPoint(0, 0);

    // وضعیت موس و درگ
    private bool _isPanning;
    private GraphNode _draggedNode;
    private Point _lastMousePos;
    private SKPoint _lastMouseWorldPos;

    // وضعیت انتخاب و هاور
    private GraphEdge _hoveredEdge;
    private GraphNode _hoveredNode;

    // --- قلم‌ها (Paints) ---

    private readonly SKPaint _edgePaint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = SKColors.Gray.WithAlpha(100),
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round // سر گرد برای زیبایی خطوط
    };

    private readonly SKPaint _dimmedEdgePaint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = SKColors.Gray.WithAlpha(20), // بسیار کمرنگ
        IsAntialias = true
    };

    private readonly SKPaint _activeEdgePaint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = SKColors.Gold, // طلایی برای حالت انتخاب
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round
    };

    private readonly SKPaint _nodePaint = new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = SKColors.DodgerBlue,
        IsAntialias = true
    };

    private readonly SKPaint _dimmedNodePaint = new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = SKColors.DodgerBlue.WithAlpha(40),
        IsAntialias = true
    };

    private readonly SKPaint _activeNodePaint = new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = SKColors.Orange,
        IsAntialias = true
    };

    private readonly SKPaint _textPaint = new SKPaint
    {
        Color = SKColors.White,
        TextSize = 12,
        IsAntialias = true,
        TextAlign = SKTextAlign.Center
    };

    // تنظیمات تولتیپ (بزرگتر و خواناتر)
    private readonly SKPaint _tooltipBgPaint = new SKPaint
        { Color = SKColors.Black.WithAlpha(240), Style = SKPaintStyle.Fill };

    private readonly SKPaint _tooltipBorderPaint = new SKPaint
        { Color = SKColors.White.WithAlpha(150), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

    private readonly SKPaint _tooltipTextPaint = new SKPaint
        { Color = SKColors.White, TextSize = 16, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };

    public GraphView()
    {
        InitializeComponent();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportGraph();

    // --- حلقه اصلی ترسیم (Rendering Loop) ---
    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var vm = DataContext as GraphViewModel;

        // پاک کردن صفحه
        canvas.Clear(SKColor.Parse("#1E1E1E"));

        if (vm == null || vm.Nodes == null) return;

        // اصلاح DPI برای مانیتورهای با رزولوشن بالا
        float density = 1.0f;
        if (GraphCanvas.ActualWidth > 0) density = (float)(e.Info.Width / GraphCanvas.ActualWidth);

        canvas.Save();
        canvas.Scale(density);
        canvas.Translate(_offset.X, _offset.Y);
        canvas.Scale(_scale);

        // تعیین نود متمرکز (یا هاور شده یا انتخاب شده)
        var focusNode = _draggedNode ?? _hoveredNode ?? vm.SelectedNode;

        // رسم هوشمند گراف (Smart Draw)
        DrawGraphSmart(canvas, vm.Nodes, vm.Edges, focusNode);

        // رسم دایره هایلایت دور نود انتخاب شده
        if (vm.SelectedNode != null)
        {
            float strokeWidth = 3f / _scale; // ضخامت ثابت نسبت به زوم
            using (var selectionPaint = new SKPaint
                   {
                       Style = SKPaintStyle.Stroke, Color = SKColors.Yellow, StrokeWidth = strokeWidth,
                       IsAntialias = true
                   })
            {
                float radius = 10 + (float)(vm.SelectedNode.Weight * 2);
                canvas.DrawCircle(vm.SelectedNode.X, vm.SelectedNode.Y, radius + 4, selectionPaint);
            }
        }

        canvas.Restore();

        // رسم تولتیپ (فقط اگر در حال درگ کردن نیستیم)
        if (_draggedNode == null)
        {
            if (_hoveredNode != null)
            {
                var screenPos = WorldToScreen(_lastMouseWorldPos);
                DrawTooltip(canvas, screenPos.X * density, screenPos.Y * density, GetNodeInfo(_hoveredNode));
            }
            else if (_hoveredEdge != null)
            {
                var screenPos = WorldToScreen(_lastMouseWorldPos);
                DrawTooltip(canvas, screenPos.X * density, screenPos.Y * density, GetEdgeInfo(_hoveredEdge, vm));
            }
        }
    }

    // --- منطق رسم هوشمند ---
    private void DrawGraphSmart(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges, GraphNode focusNode)
    {
        // حالت ۱: هیچ نودی فوکوس نیست -> همه چیز عادی رسم شود
        if (focusNode == null)
        {
            foreach (var edge in edges)
            {
                var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s == null || t == null) continue;

                float thickness = edge.Thickness;
                var paint = _edgePaint;

                // اگر خط هاور شده باشد، قرمز و ضخیم‌تر شود
                if (edge == _hoveredEdge)
                {
                    paint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = SKColors.Red, StrokeWidth = thickness + 2,
                        IsAntialias = true, StrokeCap = SKStrokeCap.Round
                    };
                }
                else
                {
                    _edgePaint.StrokeWidth = thickness;
                }

                DrawCurvedLine(canvas, s.X, s.Y, t.X, t.Y, paint);
            }

            foreach (var node in nodes) DrawSingleNode(canvas, node, false);
        }
        // حالت ۲: نودی فوکوس است -> فقط مرتبط‌ها روشن باشند (Focus Mode)
        else
        {
            var connectedEdges = new HashSet<GraphEdge>();
            var connectedNodeIds = new HashSet<string> { focusNode.Id };

            // شناسایی همسایه‌ها
            foreach (var edge in edges)
            {
                if (edge.SourceId == focusNode.Id || edge.TargetId == focusNode.Id)
                {
                    connectedEdges.Add(edge);
                    connectedNodeIds.Add(edge.SourceId);
                    connectedNodeIds.Add(edge.TargetId);
                }
            }

            // لایه ۱: رسم خطوط غیرمرتبط (کمرنگ)
            foreach (var edge in edges)
            {
                if (!connectedEdges.Contains(edge))
                {
                    var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                    var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                    if (s != null && t != null)
                    {
                        _dimmedEdgePaint.StrokeWidth = 1;
                        DrawCurvedLine(canvas, s.X, s.Y, t.X, t.Y, _dimmedEdgePaint);
                    }
                }
            }

            // لایه ۲: رسم نودهای غیرمرتبط (کمرنگ)
            foreach (var node in nodes)
            {
                if (!connectedNodeIds.Contains(node.Id)) DrawSingleNode(canvas, node, true);
            }

            // لایه ۳: رسم خطوط مرتبط (پررنگ/طلایی)
            foreach (var edge in connectedEdges)
            {
                var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s != null && t != null)
                {
                    _activeEdgePaint.StrokeWidth = edge.Thickness + 2;
                    DrawCurvedLine(canvas, s.X, s.Y, t.X, t.Y, _activeEdgePaint);
                }
            }

            // لایه ۴: رسم نودهای مرتبط (عادی)
            foreach (var node in nodes)
            {
                if (connectedNodeIds.Contains(node.Id))
                {
                    DrawSingleNode(canvas, node, false, node.Id == focusNode.Id);
                }
            }
        }
    }

    // *** رسم خط خمیده (Bezier Curve) ***
    private void DrawCurvedLine(SKCanvas canvas, float x1, float y1, float x2, float y2, SKPaint paint)
    {
        float distance = (float)Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

        // اگر فاصله کم بود، خط صاف بکش (پرفورمنس)
        if (distance < 10)
        {
            canvas.DrawLine(x1, y1, x2, y2, paint);
            return;
        }

        var cp = GetControlPoint(x1, y1, x2, y2);

        using (var path = new SKPath())
        {
            path.MoveTo(x1, y1);
            // منحنی درجه ۲ (یک نقطه کنترل)
            path.QuadTo(cp.X, cp.Y, x2, y2);
            canvas.DrawPath(path, paint);
        }
    }

    // محاسبه نقطه کنترل برای انحنا
    private SKPoint GetControlPoint(float x1, float y1, float x2, float y2)
    {
        float midX = (x1 + x2) / 2;
        float midY = (y1 + y2) / 2;
        float curveFactor = 0.15f; // میزان انحنا
        float deltaX = x2 - x1;
        float deltaY = y2 - y1;

        // انحنا با چرخش ۹۰ درجه بردار خط
        return new SKPoint(midX - (deltaY * curveFactor), midY + (deltaX * curveFactor));
    }

    private void DrawSingleNode(SKCanvas canvas, GraphNode node, bool isDimmed, bool isActive = false)
    {
        float radius = 10 + (float)(node.Weight * 2);
        SKPaint paint;

        if (isDimmed)
        {
            // استفاده از رنگ نود اما شفاف
            var nodeColor = !string.IsNullOrEmpty(node.Color) ? SKColor.Parse(node.Color) : SKColors.DodgerBlue;
            paint = new SKPaint { Style = SKPaintStyle.Fill, Color = nodeColor.WithAlpha(30), IsAntialias = true };
        }
        else if (isActive || node == _hoveredNode)
        {
            paint = _activeNodePaint;
        }
        else
        {
            // استفاده از رنگ اختصاصی نود (اگر فایل رنگ داشته باشد)
            var nodeColor = !string.IsNullOrEmpty(node.Color) ? SKColor.Parse(node.Color) : SKColors.DodgerBlue;
            paint = new SKPaint { Style = SKPaintStyle.Fill, Color = nodeColor, IsAntialias = true };
        }

        canvas.DrawCircle(node.X, node.Y, radius, paint);

        // رسم لیبل فقط اگر دیم نشده باشد یا زوم زیاد باشد
        if (!isDimmed && (_scale > 0.6f || isActive || node == _hoveredNode))
        {
            canvas.DrawText(node.Id, node.X, node.Y + radius + 15, _textPaint);
        }
    }

    // --- متدهای کمکی دریافت اطلاعات ---

    private string GetNodeInfo(GraphNode node)
    {
        return $"Number: {node.Id}\nTotal Calls: {node.TotalCalls}\nDuration: {node.TotalDurationMinutes:N1} min";
    }

    private string GetEdgeInfo(GraphEdge edge, GraphViewModel vm)
    {
        string baseInfo = $"Calls: {edge.CallCount}\nDuration: {edge.TotalDurationMinutes:N1} min";
        return baseInfo;
    }

    // --- تعاملات ماوس (Mouse Interaction) ---

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            float worldX = ((float)pos.X - _offset.X) / _scale;
            float worldY = ((float)pos.Y - _offset.Y) / _scale;

            GraphNode clickedNode = FindNodeAt(vm.Nodes, worldX, worldY);

            if (clickedNode != null)
            {
                vm.SelectedNode = clickedNode;
                _draggedNode = clickedNode;
                _lastMousePos = pos;
                GraphCanvas.CaptureMouse();
                Cursor = Cursors.SizeAll;
            }
            else
            {
                vm.SelectedNode = null;
                _isPanning = true;
                _lastMousePos = pos;
                GraphCanvas.CaptureMouse();
                Cursor = Cursors.ScrollAll;
            }

            GraphCanvas.InvalidateVisual();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        float worldX = ((float)pos.X - _offset.X) / _scale;
        float worldY = ((float)pos.Y - _offset.Y) / _scale;
        _lastMouseWorldPos = new SKPoint(worldX, worldY);

        if (_draggedNode != null)
        {
            // جابجایی نود
            float deltaX = (float)(pos.X - _lastMousePos.X) / _scale;
            float deltaY = (float)(pos.Y - _lastMousePos.Y) / _scale;
            _draggedNode.X += deltaX;
            _draggedNode.Y += deltaY;
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else if (_isPanning)
        {
            // جابجایی صفحه
            var deltaX = (float)(pos.X - _lastMousePos.X);
            var deltaY = (float)(pos.Y - _lastMousePos.Y);
            _offset.X += deltaX;
            _offset.Y += deltaY;
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else
        {
            // تشخیص هاور (Hover)
            var vm = DataContext as GraphViewModel;
            if (vm != null && vm.Nodes != null)
            {
                var foundNode = FindNodeAt(vm.Nodes, worldX, worldY);
                bool needsRedraw = false;

                if (foundNode != _hoveredNode)
                {
                    _hoveredNode = foundNode;
                    needsRedraw = true;
                }

                if (_hoveredNode != null) Cursor = Cursors.Hand;
                else Cursor = Cursors.Arrow;

                if (_hoveredNode == null)
                {
                    var foundEdge = FindEdgeAt(vm.Nodes, vm.Edges, worldX, worldY);
                    if (foundEdge != _hoveredEdge)
                    {
                        _hoveredEdge = foundEdge;
                        needsRedraw = true;
                    }
                }
                else if (_hoveredEdge != null)
                {
                    _hoveredEdge = null;
                    needsRedraw = true;
                }

                if (needsRedraw) GraphCanvas.InvalidateVisual();
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _draggedNode = null;
        GraphCanvas.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        var zoomFactor = 1.1f;
        float newScale = e.Delta > 0 ? _scale * zoomFactor : _scale / zoomFactor;
        newScale = Math.Clamp(newScale, 0.1f, 10.0f);
        float scaleRatio = newScale / _scale;
        _offset.X = (float)pos.X - ((float)pos.X - _offset.X) * scaleRatio;
        _offset.Y = (float)pos.Y - ((float)pos.Y - _offset.Y) * scaleRatio;
        _scale = newScale;
        GraphCanvas.InvalidateVisual();
    }

    // --- متدهای کمکی محاسباتی ---

    private GraphNode FindNodeAt(List<GraphNode> nodes, float x, float y)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            float dist = (float)Math.Sqrt(Math.Pow(n.X - x, 2) + Math.Pow(n.Y - y, 2));
            if (dist < (15 + n.Weight * 2)) return n;
        }

        return null;
    }

    // **تشخیص دقیق برخورد با خطوط خمیده**
    private GraphEdge FindEdgeAt(List<GraphNode> nodes, List<GraphEdge> edges, float x, float y)
    {
        // تلورانس کلیک بر اساس زوم
        float screenTolerance = 15f;
        float worldTolerance = screenTolerance / _scale;

        foreach (var edge in edges)
        {
            var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
            if (s == null || t == null) continue;

            // ابتدا فاصله خط مستقیم (برای سرعت)
            float straightDist = (float)Math.Sqrt(Math.Pow(t.X - s.X, 2) + Math.Pow(t.Y - s.Y, 2));
            float dist;

            if (straightDist < 10)
            {
                dist = PointToSegmentDist(x, y, s.X, s.Y, t.X, t.Y);
            }
            else
            {
                var cp = GetControlPoint(s.X, s.Y, t.X, t.Y);
                // محاسبه فاصله دقیق از منحنی
                dist = GetDistanceToBezier(new SKPoint(x, y), new SKPoint(s.X, s.Y), cp, new SKPoint(t.X, t.Y));
            }

            if (dist < (edge.Thickness / 2 + worldTolerance)) return edge;
        }

        return null;
    }

    // تخمین فاصله نقطه تا منحنی بزیر با قطعه‌بندی (Segmentation)
    private float GetDistanceToBezier(SKPoint p, SKPoint p0, SKPoint p1, SKPoint p2)
    {
        float minDistance = float.MaxValue;
        SKPoint prev = p0;

        // تقسیم منحنی به ۱۵ قطعه برای دقت بالا
        int segments = 15;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1 - t;

            float x = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
            float y = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;
            SKPoint current = new SKPoint(x, y);

            float dist = PointToSegmentDist(p.X, p.Y, prev.X, prev.Y, current.X, current.Y);
            if (dist < minDistance) minDistance = dist;

            prev = current;
        }

        return minDistance;
    }

    private float PointToSegmentDist(float px, float py, float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        if (dx == 0 && dy == 0) return (float)Math.Sqrt(Math.Pow(px - x1, 2) + Math.Pow(py - y1, 2));
        float t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
        if (t < 0) return (float)Math.Sqrt(Math.Pow(px - x1, 2) + Math.Pow(py - y1, 2));
        if (t > 1) return (float)Math.Sqrt(Math.Pow(px - x2, 2) + Math.Pow(py - y2, 2));
        return (float)Math.Sqrt(Math.Pow(px - (x1 + t * dx), 2) + Math.Pow(py - (y1 + t * dy), 2));
    }

    private void DrawTooltip(SKCanvas canvas, float x, float y, string text)
    {
        var lines = text.Split('\n');
        float maxWidth = 0;
        foreach (var line in lines)
        {
            float w = _tooltipTextPaint.MeasureText(line);
            if (w > maxWidth) maxWidth = w;
        }

        float lineHeight = 28; // فاصله خطوط زیاد
        float width = maxWidth + 40;
        float height = lines.Length * lineHeight + 20;

        var rect = new SKRect(x + 15, y + 15, x + 15 + width, y + 15 + height);
        canvas.DrawRoundRect(rect, 8, 8, _tooltipBgPaint);
        canvas.DrawRoundRect(rect, 8, 8, _tooltipBorderPaint);

        float textY = y + 40;
        foreach (var line in lines)
        {
            canvas.DrawText(line, x + 35, textY, _tooltipTextPaint);
            textY += lineHeight;
        }
    }

    private SKPoint WorldToScreen(SKPoint worldPos) =>
        new SKPoint(worldPos.X * _scale + _offset.X, worldPos.Y * _scale + _offset.Y);

    public void ExportGraph()
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null || !vm.Nodes.Any()) return;

        var saveDialog = new SaveFileDialog
            { Filter = "PDF Report|*.pdf", FileName = $"Graph_{DateTime.Now:yyyyMMdd}" };
        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                float minX = vm.Nodes.Min(n => n.X);
                float minY = vm.Nodes.Min(n => n.Y);
                float maxX = vm.Nodes.Max(n => n.X);
                float maxY = vm.Nodes.Max(n => n.Y);
                float graphW = maxX - minX + 100;
                float graphH = maxY - minY + 100;
                float centerX = (minX + maxX) / 2;
                float centerY = (minY + maxY) / 2;

                int imgSize = 2000;
                float scale = Math.Min(imgSize / graphW, imgSize / graphH);

                using var surface = SKSurface.Create(new SKImageInfo(imgSize, imgSize));
                var c = surface.Canvas;
                c.Clear(SKColors.White);
                c.Translate(imgSize / 2, imgSize / 2);
                c.Scale(scale);
                c.Translate(-centerX, -centerY);

                // برای اکسپورت: حالت عادی (بدون فوکوس)
                DrawGraphSmart(c, vm.Nodes, vm.Edges, null);

                using var img = surface.Snapshot();
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                var pdf = new CdrGraph.Infrastructure.Services.PdfReportService();
                pdf.GenerateReport(saveDialog.FileName, vm.Nodes, vm.Edges, data.ToArray());
                MessageBox.Show("Export Successful!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }
    }
}