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

    // وضعیت موس
    private bool _isDragging;
    private Point _lastMousePos;
    private SKPoint _lastMouseWorldPos;

    // وضعیت هاور (Hover)
    private GraphEdge _hoveredEdge;
    private GraphNode _hoveredNode;

    // قلم‌های گرافیکی (Cached Paints)
    private readonly SKPaint _edgePaint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = SKColors.Gray.WithAlpha(100),
        IsAntialias = true
    };

    private readonly SKPaint _nodePaint = new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = SKColors.DodgerBlue,
        IsAntialias = true
    };

    private readonly SKPaint _textPaint = new SKPaint
    {
        Color = SKColors.White,
        TextSize = 12,
        IsAntialias = true,
        TextAlign = SKTextAlign.Center
    };

    // استایل تولتیپ
    private readonly SKPaint _tooltipBgPaint = new SKPaint
    {
        Color = SKColors.Black.WithAlpha(230),
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint _tooltipBorderPaint = new SKPaint
    {
        Color = SKColors.White.WithAlpha(100),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1
    };

    private readonly SKPaint _tooltipTextPaint = new SKPaint
    {
        Color = SKColors.White,
        TextSize = 13,
        IsAntialias = true,
        Typeface = SKTypeface.FromFamilyName("Segoe UI")
    };

    public GraphView()
    {
        InitializeComponent();
    }

    // هندلر دکمه خروجی (رفع خطای قبلی)
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        ExportGraph();
    }

    // --- Rendering Loop (حلقه رسم) ---

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var vm = DataContext as GraphViewModel;

        // 1. پاک کردن صفحه
        canvas.Clear(SKColor.Parse("#1E1E1E"));

        if (vm == null || vm.Nodes == null) return;

        // *** DPI FIX: محاسبه ضریب تراکم پیکسلی ***
        // این خط مشکل کلیک را حل می‌کند
        float density = 1.0f;
        if (GraphCanvas.ActualWidth > 0)
        {
            density = (float)(e.Info.Width / GraphCanvas.ActualWidth);
        }

        // 2. اعمال تنظیمات دوربین
        canvas.Save();

        // اول اسکیل DPI را اعمال می‌کنیم
        canvas.Scale(density);
        // سپس تنظیمات دوربین کاربر
        canvas.Translate(_offset.X, _offset.Y);
        canvas.Scale(_scale);

        // 3. رسم گراف
        DrawGraph(canvas, vm.Nodes, vm.Edges);

        // رسم هایلایت دور نود انتخاب شده
        if (vm.SelectedNode != null)
        {
            // ضخامت خط انتخاب را با زوم تنظیم می‌کنیم تا خیلی کلفت نشود
            float strokeWidth = 3f / _scale;
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

        // 4. رسم تولتیپ‌ها (Overlays)
        if (_hoveredNode != null)
        {
            var screenPos = WorldToScreen(_lastMouseWorldPos);
            string info = GetNodeInfo(_hoveredNode);
            // ضرب در density مهم است چون روی سطح فیزیکی می‌کشیم
            DrawTooltip(canvas, screenPos.X * density, screenPos.Y * density, info);
        }
        else if (_hoveredEdge != null)
        {
            var screenPos = WorldToScreen(_lastMouseWorldPos);
            string info = GetEdgeInfo(_hoveredEdge, vm);
            DrawTooltip(canvas, screenPos.X * density, screenPos.Y * density, info);
        }
    }

    // --- Helper Methods for Info Extraction ---

    private string GetNodeInfo(GraphNode node)
    {
        return $"Number: {node.Id}\n" +
               $"Total Calls: {node.TotalCalls}\n" +
               $"Total Duration: {node.TotalDurationMinutes:N1} min";
    }

    private string GetEdgeInfo(GraphEdge edge, GraphViewModel vm)
    {
        string baseInfo = $"Calls: {edge.CallCount}\nDuration: {edge.TotalDurationMinutes:N1} min";

        // پیدا کردن اطلاعات آخرین تماس از داده‌های خام
        var sourceNode = vm.Nodes.FirstOrDefault(n => n.Id == edge.SourceId);

        if (sourceNode != null && sourceNode.RelatedRecords.Any())
        {
            var relevantRecord = sourceNode.RelatedRecords
                .Where(r => r.TargetNumber == edge.TargetId || r.SourceNumber == edge.TargetId)
                .LastOrDefault(); // آخرین رکورد (یا می‌توان بر اساس تاریخ سورت کرد)

            if (relevantRecord != null)
            {
                // نمایش تاریخ و ساعت اگر موجود باشد
                string timeStr = $"{relevantRecord.DateStr} {relevantRecord.TimeStr}".Trim();
                if (!string.IsNullOrEmpty(timeStr))
                {
                    baseInfo += $"\nLast: {timeStr}";
                }

                if (!string.IsNullOrEmpty(relevantRecord.OriginFileName))
                {
                    baseInfo += $"\nFile: {relevantRecord.OriginFileName}";
                }
            }
        }

        return baseInfo;
    }

    private void DrawGraph(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges)
    {
        // A) رسم خطوط
        foreach (var edge in edges)
        {
            var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);

            if (s == null || t == null) continue;

            if (edge == _hoveredEdge)
            {
                _edgePaint.Color = SKColors.Red;
                _edgePaint.StrokeWidth = edge.Thickness + 2;
                canvas.DrawLine(s.X, s.Y, t.X, t.Y, _edgePaint);
            }
            else
            {
                _edgePaint.Color = SKColors.Gray.WithAlpha(80);
                _edgePaint.StrokeWidth = edge.Thickness;
                canvas.DrawLine(s.X, s.Y, t.X, t.Y, _edgePaint);
            }
        }

        // B) رسم نودها
        foreach (var node in nodes)
        {
            float radius = 10 + (float)(node.Weight * 2);

            if (node == _hoveredNode)
            {
                _nodePaint.Color = SKColors.Orange;
                canvas.DrawCircle(node.X, node.Y, radius + 2, _nodePaint);
            }
            else
            {
                _nodePaint.Color = SKColors.DodgerBlue;
                canvas.DrawCircle(node.X, node.Y, radius, _nodePaint);
            }

            // رسم متن فقط اگر زوم کافی باشد یا نود هاور شده باشد
            if (_scale > 0.6f || node == _hoveredNode)
            {
                canvas.DrawText(node.Id, node.X, node.Y + radius + 15, _textPaint);
            }
        }
    }

    // --- Interaction Logic (ماوس و کلیک) ---

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            // تبدیل مختصات کلیک به مختصات گراف
            float worldX = ((float)pos.X - _offset.X) / _scale;
            float worldY = ((float)pos.Y - _offset.Y) / _scale;

            GraphNode clickedNode = null;

            // جستجو برای نود کلیک شده
            for (int i = vm.Nodes.Count - 1; i >= 0; i--)
            {
                var n = vm.Nodes[i];
                float dist = (float)Math.Sqrt(Math.Pow(n.X - worldX, 2) + Math.Pow(n.Y - worldY, 2));

                if (dist < (15 + n.Weight * 2))
                {
                    clickedNode = n;
                    break;
                }
            }

            if (clickedNode != null)
            {
                vm.SelectedNode = clickedNode;
            }
            else
            {
                vm.SelectedNode = null;
                _isDragging = true;
                _lastMousePos = e.GetPosition(this);
                GraphCanvas.CaptureMouse();
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

        if (_isDragging)
        {
            var deltaX = (float)(pos.X - _lastMousePos.X);
            var deltaY = (float)(pos.Y - _lastMousePos.Y);
            _offset.X += deltaX;
            _offset.Y += deltaY;
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else
        {
            PerformHitTest(worldX, worldY);
        }
    }

    private void PerformHitTest(float worldX, float worldY)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null || vm.Nodes == null) return;

        bool needsRedraw = false;
        GraphNode foundNode = null;
        GraphEdge foundEdge = null;

        // بررسی برخورد با نودها
        for (int i = vm.Nodes.Count - 1; i >= 0; i--)
        {
            var n = vm.Nodes[i];
            if (Dist(n.X, n.Y, worldX, worldY) < (15 + n.Weight * 2))
            {
                foundNode = n;
                break;
            }
        }

        if (foundNode != _hoveredNode)
        {
            _hoveredNode = foundNode;
            needsRedraw = true;
        }

        // بررسی برخورد با خطوط (اگر نودی انتخاب نشده بود)
        if (_hoveredNode == null)
        {
            foreach (var edge in vm.Edges)
            {
                var s = vm.Nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = vm.Nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s == null || t == null) continue;

                if (PointToSegmentDist(worldX, worldY, s.X, s.Y, t.X, t.Y) < (edge.Thickness / 2 + 5))
                {
                    foundEdge = edge;
                    break;
                }
            }

            if (foundEdge != _hoveredEdge)
            {
                _hoveredEdge = foundEdge;
                needsRedraw = true;
            }
        }
        else
        {
            if (_hoveredEdge != null)
            {
                _hoveredEdge = null;
                needsRedraw = true;
            }
        }

        if (needsRedraw) GraphCanvas.InvalidateVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        GraphCanvas.ReleaseMouseCapture();
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

    // --- Export Logic (اصلاح شده: Auto-Fit) ---

    public void ExportGraph()
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null || vm.Nodes == null || !vm.Nodes.Any())
        {
            MessageBox.Show("No graph data to export.", "Warning");
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Filter = "PDF Report|*.pdf",
            FileName = $"Graph_Report_{DateTime.Now:yyyyMMdd_HHmm}"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                // 1. محاسبه ابعاد واقعی گراف (Bounding Box)
                float minX = vm.Nodes.Min(n => n.X);
                float minY = vm.Nodes.Min(n => n.Y);
                float maxX = vm.Nodes.Max(n => n.X);
                float maxY = vm.Nodes.Max(n => n.Y);

                // اضافه کردن حاشیه (Padding)
                float padding = 50f;
                float graphWidth = maxX - minX + (padding * 2);
                float graphHeight = maxY - minY + (padding * 2);

                // مرکز گراف
                float graphCenterX = (minX + maxX) / 2;
                float graphCenterY = (minY + maxY) / 2;

                // ابعاد تصویر خروجی
                int targetWidth = 2000;
                int targetHeight = 2000;

                // محاسبه مقیاس مناسب (Scale)
                float scaleX = targetWidth / graphWidth;
                float scaleY = targetHeight / graphHeight;
                float exportScale = Math.Min(scaleX, scaleY); // حفظ نسبت ابعاد

                // 2. ساخت تصویر با کیفیت بالا
                using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.White); // پس‌زمینه سفید برای چاپ

                canvas.Save();

                // انتقال مبدا به مرکز تصویر
                canvas.Translate(targetWidth / 2, targetHeight / 2);
                // اعمال مقیاس
                canvas.Scale(exportScale);
                // انتقال مرکز گراف به مبدا (0,0)
                canvas.Translate(-graphCenterX, -graphCenterY);

                // رسم گراف تمیز (بدون هاور)
                var tempNode = _hoveredNode;
                _hoveredNode = null;
                var tempEdge = _hoveredEdge;
                _hoveredEdge = null;

                DrawGraph(canvas, vm.Nodes, vm.Edges);

                _hoveredNode = tempNode;
                _hoveredEdge = tempEdge;

                canvas.Restore();

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var imageBytes = data.ToArray();

                // 3. تولید PDF
                var pdfService = new CdrGraph.Infrastructure.Services.PdfReportService();
                pdfService.GenerateReport(saveDialog.FileName, vm.Nodes, vm.Edges, imageBytes);

                MessageBox.Show("Export Successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // --- Helper Methods ---

    private void DrawTooltip(SKCanvas canvas, float x, float y, string text)
    {
        var lines = text.Split('\n');
        float maxWidth = 0;
        foreach (var line in lines)
        {
            float w = _tooltipTextPaint.MeasureText(line);
            if (w > maxWidth) maxWidth = w;
        }

        float width = maxWidth + 30;
        float lineHeight = 20;
        float height = lines.Length * lineHeight + 15;

        var rect = new SKRect(x + 15, y + 15, x + 15 + width, y + 15 + height);
        canvas.DrawRoundRect(rect, 5, 5, _tooltipBgPaint);
        canvas.DrawRoundRect(rect, 5, 5, _tooltipBorderPaint);

        float textY = y + 32;
        foreach (var line in lines)
        {
            canvas.DrawText(line, x + 25, textY, _tooltipTextPaint);
            textY += lineHeight;
        }
    }

    private SKPoint WorldToScreen(SKPoint worldPos)
    {
        return new SKPoint(worldPos.X * _scale + _offset.X, worldPos.Y * _scale + _offset.Y);
    }

    private float Dist(float x1, float y1, float x2, float y2)
        => (float)Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

    private float PointToSegmentDist(float px, float py, float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        if (dx == 0 && dy == 0) return Dist(px, py, x1, y1);

        float t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
        if (t < 0) return Dist(px, py, x1, y1);
        if (t > 1) return Dist(px, py, x2, y2);
        return Dist(px, py, x1 + t * dx, y1 + t * dy);
    }
}