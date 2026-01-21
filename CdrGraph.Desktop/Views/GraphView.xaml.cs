
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
    // Camera State
    private float _scale = 1.0f;
    private SKPoint _offset = new SKPoint(0, 0);

    // Mouse State
    private bool _isDragging;
    private Point _lastMousePos;
    private SKPoint _lastMouseWorldPos;

    // Hover State
    private GraphEdge _hoveredEdge;
    private GraphNode _hoveredNode;

    // Paints (Cached for performance)
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

    private readonly SKPaint _tooltipBgPaint = new SKPaint
    {
        Color = SKColors.Black.WithAlpha(200),
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint _tooltipTextPaint = new SKPaint
    {
        Color = SKColors.White,
        TextSize = 14,
        IsAntialias = true
    };

    public GraphView()
    {
        InitializeComponent();
    }

    // --- Rendering Loop ---

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var vm = DataContext as GraphViewModel;

        // 1. Clear Background
        canvas.Clear(SKColor.Parse("#1E1E1E"));

        if (vm == null || vm.Nodes == null) return;

        // 2. Apply Camera Transform
        canvas.Save();
        canvas.Translate(_offset.X, _offset.Y);
        canvas.Scale(_scale);

        // 3. Draw The Graph (Reusable Logic)
        DrawGraph(canvas, vm.Nodes, vm.Edges);

        canvas.Restore();

        // 4. Draw Overlays (Tooltips) - Not affected by Zoom/Pan logic directly, but positioned relative to mouse
        // Note: Tooltip coordinates are in World Space, so we need to project them or just draw near mouse screen pos.
        // Here we use the World Position calculated in MouseMove but mapped back to screen for simplicity, 
        // OR just draw based on current mouse screen position if available. 
        // Better: Draw based on _lastMouseWorldPos transformed to screen.

        if (_hoveredNode != null)
        {
            var screenPos = WorldToScreen(_lastMouseWorldPos);
            DrawTooltip(canvas, screenPos.X, screenPos.Y,
                $"Number: {_hoveredNode.Id}\nCalls: {_hoveredNode.TotalCalls}\nDuration: {_hoveredNode.TotalDurationMinutes:N1} min");
        }
        else if (_hoveredEdge != null)
        {
            var screenPos = WorldToScreen(_lastMouseWorldPos);
            DrawTooltip(canvas, screenPos.X, screenPos.Y,
                $"Link: {_hoveredEdge.CallCount} Calls\n{_hoveredEdge.TotalDurationMinutes:N1} min");
        }
    }

    /// <summary>
    /// Core drawing logic, separated to be used by both Screen Rendering and PDF Export.
    /// </summary>
    private void DrawGraph(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges)
    {
        // A) Draw Edges
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

        // B) Draw Nodes
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

            // Draw Label if zoomed in or hovered
            if (_scale > 0.6f || node == _hoveredNode)
            {
                canvas.DrawText(node.Id, node.X, node.Y + radius + 15, _textPaint);
            }
        }
    }

    // --- Interaction Logic ---

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = true;
            _lastMousePos = e.GetPosition(this);
            GraphCanvas.CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            // Right click context menu could go here
            // For now, let's trigger Export on Right Click (Temp) or use a Button in UI
            // ExportGraph();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Calculate World Position
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

        // Check Nodes
        // Reverse loop to check top nodes first
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

        // Check Edges (only if no node is hovered)
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

    // --- Export Logic ---

    /// <summary>
    /// This method should be called from UI (e.g. Button Click event in Code Behind)
    /// </summary>
    public void ExportGraph()
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;

        var saveDialog = new SaveFileDialog
        {
            Filter = "PDF Report|*.pdf",
            FileName = $"Graph_Report_{DateTime.Now:yyyyMMdd_HHmm}"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                // 1. Capture High-Res Image of the Graph
                // We create a new surface with fixed size to ensure high quality regardless of window size
                int width = 2000;
                int height = 2000;

                // Fit graph to bounds logic could be added here, 
                // for now we just center it or use default coordinates if they fit.
                // Better: Calculate bounding box of all nodes and scale/translate to fit 2000x2000.

                using var surface = SKSurface.Create(new SKImageInfo(width, height));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.White); // White background for print

                // Center the graph in the image
                // Simple centering: Translate to center
                canvas.Translate(width / 2, height / 2);
                // Assuming nodes are centered around 0,0 or we need to find center.
                // For now, let's just use a safe scale and draw.
                canvas.Translate(-1000, -1000); // Adjust based on your coordinate system range

                // Draw clean graph (without hover effects)
                var tempNode = _hoveredNode;
                _hoveredNode = null;
                var tempEdge = _hoveredEdge;
                _hoveredEdge = null;

                DrawGraph(canvas, vm.Nodes, vm.Edges);

                // Restore hover state
                _hoveredNode = tempNode;
                _hoveredEdge = tempEdge;

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var imageBytes = data.ToArray();

                // 2. Generate PDF
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
        float width = 160;
        float lineHeight = 20;
        float height = lines.Length * lineHeight + 10;

        var rect = new SKRect(x + 15, y + 15, x + 15 + width, y + 15 + height);
        canvas.DrawRoundRect(rect, 5, 5, _tooltipBgPaint);

        float textY = y + 35;
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