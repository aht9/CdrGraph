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
    // Camera
    private float _scale = 1.0f;
    private SKPoint _offset = new SKPoint(0, 0);

    // Interaction State
    private bool _isPanning;
    private GraphNode _draggedNode;
    private GraphEdge _draggedEdge; // خطی که در حال جابجایی است
    private Point _lastMousePos;
    private SKPoint _lastMouseWorldPos;

    private GraphEdge _hoveredEdge;
    private GraphNode _hoveredNode;

    // Theme Colors (Dynamic)
    private SKColor _bgColor = SKColor.Parse("#1E1E1E");
    private SKColor _defaultEdgeColor = SKColors.Gray.WithAlpha(100);
    private SKColor _defaultTextColor = SKColors.White;

    // Paints (Will be updated on theme change)
    private SKPaint _edgePaint;
    private SKPaint _dimmedEdgePaint;
    private SKPaint _activeEdgePaint;
    private SKPaint _nodePaint;
    private SKPaint _dimmedNodePaint;
    private SKPaint _activeNodePaint;
    private SKPaint _textPaint;
    private SKPaint _tooltipBgPaint;
    private SKPaint _tooltipBorderPaint;
    private SKPaint _tooltipTextPaint;

    public GraphView()
    {
        InitializeComponent();
        InitializePaints(); // ساخت اولیه قلم‌ها
    }

    // --- مدیریت تم و رنگ ---
    private void InitializePaints()
    {
        _edgePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke, Color = _defaultEdgeColor, IsAntialias = true, StrokeCap = SKStrokeCap.Round
        };
        _dimmedEdgePaint = new SKPaint
            { Style = SKPaintStyle.Stroke, Color = _defaultEdgeColor.WithAlpha(20), IsAntialias = true };
        _activeEdgePaint = new SKPaint
            { Style = SKPaintStyle.Stroke, Color = SKColors.Gold, IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        _nodePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.DodgerBlue, IsAntialias = true };
        _dimmedNodePaint = new SKPaint
            { Style = SKPaintStyle.Fill, Color = SKColors.DodgerBlue.WithAlpha(40), IsAntialias = true };
        _activeNodePaint = new SKPaint
            { Style = SKPaintStyle.Fill, Color = SKColors.Orange, IsAntialias = true }; // Selected Node Color

        _textPaint = new SKPaint
            { Color = _defaultTextColor, TextSize = 12, IsAntialias = true, TextAlign = SKTextAlign.Center };

        _tooltipBgPaint = new SKPaint { Color = SKColors.Black.WithAlpha(240), Style = SKPaintStyle.Fill };
        _tooltipBorderPaint = new SKPaint
            { Color = SKColors.White.WithAlpha(150), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        _tooltipTextPaint = new SKPaint
        {
            Color = SKColors.White, TextSize = 16, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI")
        };
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;

        if (vm.SelectedTheme == "Light Mode")
        {
            _bgColor = SKColors.White;
            _defaultEdgeColor = SKColors.Black.WithAlpha(150); // خطوط مشکی در تم روشن
            _defaultTextColor = SKColors.Black;
            _tooltipBgPaint.Color = SKColors.White.WithAlpha(240);
            _tooltipBorderPaint.Color = SKColors.Black.WithAlpha(50);
            _tooltipTextPaint.Color = SKColors.Black;
        }
        else // Dark Mode
        {
            _bgColor = SKColor.Parse("#1E1E1E");
            _defaultEdgeColor = SKColors.Gray.WithAlpha(100);
            _defaultTextColor = SKColors.White;
            _tooltipBgPaint.Color = SKColors.Black.WithAlpha(240);
            _tooltipBorderPaint.Color = SKColors.White.WithAlpha(150);
            _tooltipTextPaint.Color = SKColors.White;
        }

        // بروزرسانی رنگ قلم‌ها
        _edgePaint.Color = _defaultEdgeColor;
        _dimmedEdgePaint.Color = _defaultEdgeColor.WithAlpha(30);
        _textPaint.Color = _defaultTextColor;

        GraphCanvas.InvalidateVisual();
    }

    private void OnZoomChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm != null && vm.SelectedZoom != null)
        {
            // تبدیل "100%" به 1.0
            string zoomStr = vm.SelectedZoom.Replace("%", "");
            if (float.TryParse(zoomStr, out float zoomVal))
            {
                _scale = zoomVal / 100f;
                GraphCanvas.InvalidateVisual();
            }
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportGraph();

    // --- Rendering ---

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var vm = DataContext as GraphViewModel;

        canvas.Clear(_bgColor); // استفاده از رنگ پس‌زمینه پویا

        if (vm == null || vm.Nodes == null) return;

        float density = 1.0f;
        if (GraphCanvas.ActualWidth > 0) density = (float)(e.Info.Width / GraphCanvas.ActualWidth);

        canvas.Save();
        canvas.Scale(density);
        canvas.Translate(_offset.X, _offset.Y);
        canvas.Scale(_scale);

        var focusNode = _draggedNode ?? _hoveredNode ?? vm.SelectedNode;

        DrawGraphSmart(canvas, vm.Nodes, vm.Edges, focusNode);

        // رسم دایره هایلایت دور نود انتخاب شده
        if (vm.SelectedNode != null)
        {
            float strokeWidth = 3f / _scale;
            using (var selectionPaint = new SKPaint
                   {
                       Style = SKPaintStyle.Stroke, Color = SKColors.Yellow, StrokeWidth = strokeWidth,
                       IsAntialias = true
                   })
            {
                // نود انتخاب شده بزرگتر رسم شده، پس هایلایت هم باید بزرگتر باشد
                // ضریب 1.8 همان ضریبی است که در DrawSingleNode برای حالت Active استفاده کردیم
                float radius = (10 + (float)(vm.SelectedNode.Weight * 2)) * 1.8f;
                canvas.DrawCircle(vm.SelectedNode.X, vm.SelectedNode.Y, radius + 4, selectionPaint);
            }
        }

        canvas.Restore();

        // رسم تولتیپ (اگر درگ نمی‌کنیم)
        if (_draggedNode == null && _draggedEdge == null)
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

    private void DrawGraphSmart(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges, GraphNode focusNode)
    {
        if (focusNode == null)
        {
            foreach (var edge in edges)
            {
                var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s == null || t == null) continue;

                var paint = (edge == _hoveredEdge) ? _activeEdgePaint : _edgePaint;
                if (edge != _hoveredEdge) paint.StrokeWidth = edge.Thickness;
                else paint.StrokeWidth = edge.Thickness + 2;

                DrawCurvedLine(canvas, s.X, s.Y, t.X, t.Y, edge, paint);
            }

            foreach (var node in nodes) DrawSingleNode(canvas, node, false);
        }
        else
        {
            var connectedEdges = new HashSet<GraphEdge>();
            var connectedNodeIds = new HashSet<string> { focusNode.Id };

            foreach (var edge in edges)
            {
                if (edge.SourceId == focusNode.Id || edge.TargetId == focusNode.Id)
                {
                    connectedEdges.Add(edge);
                    connectedNodeIds.Add(edge.SourceId);
                    connectedNodeIds.Add(edge.TargetId);
                }
            }

            // 1. پس‌زمینه
            foreach (var edge in edges)
            {
                if (!connectedEdges.Contains(edge))
                {
                    var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                    var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                    if (s != null && t != null)
                    {
                        _dimmedEdgePaint.StrokeWidth = 1;
                        DrawCurvedLine(canvas, s.X, s.Y, t.X, t.Y, edge, _dimmedEdgePaint);
                    }
                }
            }

            foreach (var node in nodes)
            {
                if (!connectedNodeIds.Contains(node.Id)) DrawSingleNode(canvas, node, true);
            }

            // 2. پیش‌زمینه
            foreach (var edge in connectedEdges)
            {
                var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s != null && t != null)
                {
                    _activeEdgePaint.StrokeWidth = edge.Thickness + 2;
                    DrawCurvedLine(canvas, s.X, s.Y, t.X, t.Y, edge, _activeEdgePaint);
                }
            }

            foreach (var node in nodes)
            {
                if (connectedNodeIds.Contains(node.Id))
                {
                    // اگر نود فوکوس باشد، بزرگتر رسم شود (IsActive = true)
                    DrawSingleNode(canvas, node, false, node.Id == focusNode.Id);
                }
            }
        }
    }

    private void DrawCurvedLine(SKCanvas canvas, float x1, float y1, float x2, float y2, GraphEdge edge, SKPaint paint)
    {
        // محاسبه نقطه کنترل با در نظر گرفتن جابجایی دستی کاربر
        var cp = GetControlPoint(x1, y1, x2, y2, edge.ControlPointOffsetX, edge.ControlPointOffsetY);

        using (var path = new SKPath())
        {
            path.MoveTo(x1, y1);
            path.QuadTo(cp.X, cp.Y, x2, y2);
            canvas.DrawPath(path, paint);
        }
    }

    private SKPoint GetControlPoint(float x1, float y1, float x2, float y2, float offsetX = 0, float offsetY = 0)
    {
        float midX = (x1 + x2) / 2;
        float midY = (y1 + y2) / 2;

        // انحنای پیش‌فرض + انحنای دستی کاربر
        float defaultCurveFactor = 0.15f;
        float deltaX = x2 - x1;
        float deltaY = y2 - y1;

        float defaultCpX = midX - (deltaY * defaultCurveFactor);
        float defaultCpY = midY + (deltaX * defaultCurveFactor);

        return new SKPoint(defaultCpX + offsetX, defaultCpY + offsetY);
    }

    private void DrawSingleNode(SKCanvas canvas, GraphNode node, bool isDimmed, bool isActive = false)
    {
        float baseRadius = 10 + (float)(node.Weight * 2);
        // *** بزرگنمایی نود فعال ***
        float radius = isActive ? baseRadius * 1.8f : baseRadius;

        SKPaint paint;
        if (isDimmed)
        {
            var nodeColor = !string.IsNullOrEmpty(node.Color) ? SKColor.Parse(node.Color) : SKColors.DodgerBlue;
            paint = new SKPaint { Style = SKPaintStyle.Fill, Color = nodeColor.WithAlpha(40), IsAntialias = true };
        }
        else if (isActive || node == _hoveredNode)
        {
            paint = _activeNodePaint; // نود نارنجی برای حالت فعال
        }
        else
        {
            var nodeColor = !string.IsNullOrEmpty(node.Color) ? SKColor.Parse(node.Color) : SKColors.DodgerBlue;
            paint = new SKPaint { Style = SKPaintStyle.Fill, Color = nodeColor, IsAntialias = true };
        }

        canvas.DrawCircle(node.X, node.Y, radius, paint);

        if (!isDimmed && (_scale > 0.6f || isActive || node == _hoveredNode))
        {
            // متن را کمی پایین‌تر می‌بریم اگر نود بزرگ شده باشد
            float textOffset = isActive ? 25 : 15;
            canvas.DrawText(node.Id, node.X, node.Y + radius + textOffset, _textPaint);
        }
    }

    // --- Mouse Interaction ---

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            float worldX = ((float)pos.X - _offset.X) / _scale;
            float worldY = ((float)pos.Y - _offset.Y) / _scale;

            var clickedNode = FindNodeAt(vm.Nodes, worldX, worldY);

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
                // اگر روی نود نبود، چک کن روی خط هست؟
                var clickedEdge = FindEdgeAt(vm.Nodes, vm.Edges, worldX, worldY);
                if (clickedEdge != null)
                {
                    // شروع جابجایی خط
                    _draggedEdge = clickedEdge;
                    _lastMousePos = pos;
                    GraphCanvas.CaptureMouse();
                    Cursor = Cursors.Hand; // یا آیکون تغییر شکل
                }
                else
                {
                    // Pan
                    vm.SelectedNode = null;
                    _isPanning = true;
                    _lastMousePos = pos;
                    GraphCanvas.CaptureMouse();
                    Cursor = Cursors.ScrollAll;
                }
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
            float deltaX = (float)(pos.X - _lastMousePos.X) / _scale;
            float deltaY = (float)(pos.Y - _lastMousePos.Y) / _scale;
            _draggedNode.X += deltaX;
            _draggedNode.Y += deltaY;
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else if (_draggedEdge != null)
        {
            // *** جابجایی خط ***
            // ما Offset کنترل پوینت را تغییر می‌دهیم
            float deltaX = (float)(pos.X - _lastMousePos.X) / _scale;
            float deltaY = (float)(pos.Y - _lastMousePos.Y) / _scale;

            _draggedEdge.ControlPointOffsetX += deltaX;
            _draggedEdge.ControlPointOffsetY += deltaY;

            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else if (_isPanning)
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
            // Hover Logic
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
                        if (_hoveredEdge != null) Cursor = Cursors.Hand;
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
        _draggedEdge = null;
        GraphCanvas.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    // --- Helpers ---
    private GraphNode FindNodeAt(List<GraphNode> nodes, float x, float y)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            // شعاع کلیک برای نودهای بزرگ شده باید بیشتر باشد
            // اگر نود انتخاب شده است (Active)، شعاعش 1.8 برابر است
            var vm = DataContext as GraphViewModel;
            bool isActive = vm?.SelectedNode == n;
            float radiusMultiplier = isActive ? 1.8f : 1.0f;

            float dist = (float)Math.Sqrt(Math.Pow(n.X - x, 2) + Math.Pow(n.Y - y, 2));
            if (dist < (15 + n.Weight * 2) * radiusMultiplier) return n;
        }

        return null;
    }

    private GraphEdge FindEdgeAt(List<GraphNode> nodes, List<GraphEdge> edges, float x, float y)
    {
        float screenTolerance = 15f;
        float worldTolerance = screenTolerance / _scale;

        foreach (var edge in edges)
        {
            var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
            if (s == null || t == null) continue;

            var cp = GetControlPoint(s.X, s.Y, t.X, t.Y, edge.ControlPointOffsetX, edge.ControlPointOffsetY);
            float dist = GetDistanceToBezier(new SKPoint(x, y), new SKPoint(s.X, s.Y), cp, new SKPoint(t.X, t.Y));

            if (dist < (edge.Thickness / 2 + worldTolerance)) return edge;
        }

        return null;
    }

    private float GetDistanceToBezier(SKPoint p, SKPoint p0, SKPoint p1, SKPoint p2)
    {
        float minDistance = float.MaxValue;
        SKPoint prev = p0;
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

        float lineHeight = 20;
        float width = maxWidth + 30;
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

    private SKPoint WorldToScreen(SKPoint worldPos) =>
        new SKPoint(worldPos.X * _scale + _offset.X, worldPos.Y * _scale + _offset.Y);

    private string GetNodeInfo(GraphNode node) =>
        $"Number: {node.Id}\nTotal Calls: {node.TotalCalls}\nDuration: {node.TotalDurationMinutes:N1} min";

    private string GetEdgeInfo(GraphEdge edge, GraphViewModel vm) =>
        $"Calls: {edge.CallCount}\nDuration: {edge.TotalDurationMinutes:N1} min";

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        var zoomFactor = 1.1f;
        float newScale = e.Delta > 0 ? _scale * zoomFactor : _scale / zoomFactor;
        newScale = Math.Clamp(newScale, 0.1f, 10.0f);

        // آپدیت کردن ComboBox ویومدل
        var vm = DataContext as GraphViewModel;
        if (vm != null)
        {
            // این کار باعث می‌شود SelectedZoom تغییر کند و از طریق OnZoomChanged دوباره Scale ست شود
            // اما برای روانی کار، Scale داخلی را هم تغییر می‌دهیم
            vm.SelectedZoom = $"{(int)(newScale * 100)}%";
        }

        float scaleRatio = newScale / _scale;
        _offset.X = (float)pos.X - ((float)pos.X - _offset.X) * scaleRatio;
        _offset.Y = (float)pos.Y - ((float)pos.Y - _offset.Y) * scaleRatio;
        _scale = newScale;
        GraphCanvas.InvalidateVisual();
    }

    public void ExportGraph()
    {
        // (کد اکسپورت قبلی با استفاده از DrawGraphSmart)
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
                c.Clear(_bgColor); // پس زمینه رنگ تم فعلی (یا سفید برای پرینت)
                c.Translate(imgSize / 2, imgSize / 2);
                c.Scale(scale);
                c.Translate(-centerX, -centerY);

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