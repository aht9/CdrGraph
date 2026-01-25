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
        // وضعیت دوربین
        private float _scale = 1.0f;
        private SKPoint _offset = new SKPoint(0, 0);
        
        // وضعیت موس
        private bool _isDragging;
        private Point _lastMousePos;
        private SKPoint _lastMouseWorldPos;
        
        // وضعیت انتخاب و هاور
        private GraphEdge _hoveredEdge;
        private GraphNode _hoveredNode;

        // --- تعریف قلم‌ها (Paints) برای حالت‌های مختلف ---

        // 1. حالت عادی
        private readonly SKPaint _edgePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Gray.WithAlpha(80),
            IsAntialias = true
        };
        private readonly SKPaint _nodePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.DodgerBlue,
            IsAntialias = true
        };

        // 2. حالت "غیرفعال" (کمرنگ شده - Dimmed)
        private readonly SKPaint _dimmedEdgePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Gray.WithAlpha(15), // بسیار کمرنگ
            IsAntialias = true
        };
        private readonly SKPaint _dimmedNodePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.DodgerBlue.WithAlpha(40), // نود کمرنگ
            IsAntialias = true
        };

        // 3. حالت "فعال/تمرکز" (Active/Focused)
        private readonly SKPaint _activeEdgePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Gold, // رنگ طلایی برای مسیرهای مهم
            StrokeWidth = 2,
            IsAntialias = true
        };
        private readonly SKPaint _activeNodePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Orange, // رنگ نود انتخاب شده
            IsAntialias = true
        };

        // سایر قلم‌ها
        private readonly SKPaint _textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 12,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        private readonly SKPaint _tooltipBgPaint = new SKPaint { Color = SKColors.Black.WithAlpha(230), Style = SKPaintStyle.Fill };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Color = SKColors.White.WithAlpha(100), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { Color = SKColors.White, TextSize = 13, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };

        public GraphView()
        {
            InitializeComponent();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportGraph();

        // --- حلقه اصلی ترسیم ---
        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var vm = DataContext as GraphViewModel;

            canvas.Clear(SKColor.Parse("#1E1E1E"));

            if (vm == null || vm.Nodes == null) return;

            // DPI Fix
            float density = 1.0f;
            if (GraphCanvas.ActualWidth > 0) density = (float)(e.Info.Width / GraphCanvas.ActualWidth);

            canvas.Save();
            canvas.Scale(density);
            canvas.Translate(_offset.X, _offset.Y);
            canvas.Scale(_scale);

            // نود کانونی (Focus Node): نودی که موس روی آن است یا انتخاب شده
            var focusNode = _hoveredNode ?? vm.SelectedNode;

            // رسم گراف با منطق هوشمند
            DrawGraphSmart(canvas, vm.Nodes, vm.Edges, focusNode);

            // رسم حلقه دور نود انتخاب شده
            if (vm.SelectedNode != null)
            {
                float strokeWidth = 3f / _scale; 
                using (var selectionPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Yellow, StrokeWidth = strokeWidth, IsAntialias = true })
                {
                    float radius = 10 + (float)(vm.SelectedNode.Weight * 2);
                    canvas.DrawCircle(vm.SelectedNode.X, vm.SelectedNode.Y, radius + 4, selectionPaint);
                }
            }

            canvas.Restore();

            // رسم تولتیپ‌ها
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

        // --- منطق رسم هوشمند (Smart Drawing) ---
        private void DrawGraphSmart(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges, GraphNode focusNode)
        {
            // اگر هیچ نودی فوکوس نبود -> رسم عادی
            if (focusNode == null)
            {
                foreach (var edge in edges)
                {
                    var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                    var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                    if (s == null || t == null) continue;

                    // اگر موس روی خط باشد، قرمز شود
                    var paint = (edge == _hoveredEdge) ? new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Red, StrokeWidth = edge.Thickness + 2 } : _edgePaint;
                    if (edge != _hoveredEdge) paint.StrokeWidth = edge.Thickness;
                    
                    canvas.DrawLine(s.X, s.Y, t.X, t.Y, paint);
                }

                foreach (var node in nodes)
                {
                    DrawSingleNode(canvas, node, false); // false = Normal
                }
            }
            // اگر نودی فوکوس بود -> حالت Focus & Dimming
            else
            {
                var connectedEdges = new HashSet<GraphEdge>();
                var connectedNodeIds = new HashSet<string>();
                connectedNodeIds.Add(focusNode.Id);

                // 1. پیدا کردن همسایه‌ها
                foreach (var edge in edges)
                {
                    if (edge.SourceId == focusNode.Id || edge.TargetId == focusNode.Id)
                    {
                        connectedEdges.Add(edge);
                        connectedNodeIds.Add(edge.SourceId);
                        connectedNodeIds.Add(edge.TargetId);
                    }
                }

                // 2. لایه اول: رسم خطوط غیرمرتبط (کمرنگ) در زیر
                foreach (var edge in edges)
                {
                    if (!connectedEdges.Contains(edge))
                    {
                        var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                        var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                        if (s != null && t != null)
                        {
                            _dimmedEdgePaint.StrokeWidth = 1; // خطوط پس‌زمینه نازک
                            canvas.DrawLine(s.X, s.Y, t.X, t.Y, _dimmedEdgePaint);
                        }
                    }
                }

                // 3. لایه دوم: رسم نودهای غیرمرتبط (کمرنگ)
                foreach (var node in nodes)
                {
                    if (!connectedNodeIds.Contains(node.Id))
                    {
                        DrawSingleNode(canvas, node, true); // true = Dimmed
                    }
                }

                // 4. لایه سوم: رسم خطوط مرتبط (پررنگ و طلایی)
                foreach (var edge in connectedEdges)
                {
                    var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                    var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                    if (s != null && t != null)
                    {
                        _activeEdgePaint.StrokeWidth = edge.Thickness + 1;
                        canvas.DrawLine(s.X, s.Y, t.X, t.Y, _activeEdgePaint);
                    }
                }

                // 5. لایه چهارم: رسم نودهای مرتبط و نود اصلی (عادی/پررنگ)
                foreach (var node in nodes)
                {
                    if (connectedNodeIds.Contains(node.Id))
                    {
                        // اگر نود اصلی است، رنگش نارنجی شود
                        if (node.Id == focusNode.Id) 
                            DrawSingleNode(canvas, node, false, true); // Active
                        else 
                            DrawSingleNode(canvas, node, false); // Normal
                    }
                }
            }
        }

        private void DrawSingleNode(SKCanvas canvas, GraphNode node, bool isDimmed, bool isActive = false)
        {
            float radius = 10 + (float)(node.Weight * 2);
            SKPaint paint;

            if (isDimmed) paint = _dimmedNodePaint;
            else if (isActive || node == _hoveredNode) paint = _activeNodePaint;
            else paint = _nodePaint;
            
            canvas.DrawCircle(node.X, node.Y, radius, paint);

            // متن فقط اگر دیم نشده باشد یا زوم زیاد باشد
            if (!isDimmed && (_scale > 0.6f || isActive || node == _hoveredNode))
            {
                canvas.DrawText(node.Id, node.X, node.Y + radius + 15, _textPaint);
            }
        }

        // --- Helper Methods ---
        
        private string GetNodeInfo(GraphNode node)
        {
            return $"Number: {node.Id}\nTotal Calls: {node.TotalCalls}\nDuration: {node.TotalDurationMinutes:N1} min";
        }

        private string GetEdgeInfo(GraphEdge edge, GraphViewModel vm)
        {
            string baseInfo = $"Calls: {edge.CallCount}\nDuration: {edge.TotalDurationMinutes:N1} min";
            var sourceNode = vm.Nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            if (sourceNode != null)
            {
                // اینجا به دیتابیس یا لیست کش شده دسترسی نداریم.
                // اگر نیاز به تاریخ دقیق در هاور دارید، باید مکانیزم کش کردن در ویومدل داشته باشید.
                // فعلا اطلاعات پایه را نشان می‌دهیم.
            }
            return baseInfo;
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

                GraphNode clickedNode = FindNodeAt(vm.Nodes, worldX, worldY);

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
                // Hit Testing for Hover Effect
                var vm = DataContext as GraphViewModel;
                if (vm != null && vm.Nodes != null)
                {
                    var foundNode = FindNodeAt(vm.Nodes, worldX, worldY);
                    
                    bool needsRedraw = false;
                    if (foundNode != _hoveredNode) { _hoveredNode = foundNode; needsRedraw = true; }

                    // Only check edges if no node is hovered
                    if (_hoveredNode == null)
                    {
                        var foundEdge = FindEdgeAt(vm.Nodes, vm.Edges, worldX, worldY);
                        if (foundEdge != _hoveredEdge) { _hoveredEdge = foundEdge; needsRedraw = true; }
                    }
                    else if (_hoveredEdge != null) { _hoveredEdge = null; needsRedraw = true; }

                    if (needsRedraw) GraphCanvas.InvalidateVisual();
                }
            }
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

        // --- Calculation Helpers ---

        private GraphNode FindNodeAt(List<GraphNode> nodes, float x, float y)
        {
            // Reverse loop for Z-Order (top items first)
            for (int i = nodes.Count - 1; i >= 0; i--) 
            {
                var n = nodes[i];
                float dist = (float)Math.Sqrt(Math.Pow(n.X - x, 2) + Math.Pow(n.Y - y, 2));
                if (dist < (15 + n.Weight * 2)) return n;
            }
            return null;
        }

        private GraphEdge FindEdgeAt(List<GraphNode> nodes, List<GraphEdge> edges, float x, float y)
        {
            foreach (var edge in edges)
            {
                var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s == null || t == null) continue;
                if (PointToSegmentDist(x, y, s.X, s.Y, t.X, t.Y) < (edge.Thickness / 2 + 5)) return edge;
            }
            return null;
        }

        private float PointToSegmentDist(float px, float py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1; float dy = y2 - y1;
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
            
            // --- FIX: تعریف متغیر lineHeight که در کد قبلی جا افتاده بود ---
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

        private SKPoint WorldToScreen(SKPoint worldPos) => new SKPoint(worldPos.X * _scale + _offset.X, worldPos.Y * _scale + _offset.Y);

        public void ExportGraph()
        {
             var vm = DataContext as GraphViewModel;
             if (vm == null || !vm.Nodes.Any()) return;
             
             var saveDialog = new SaveFileDialog { Filter = "PDF Report|*.pdf", FileName = $"Graph_{DateTime.Now:yyyyMMdd}" };
             if (saveDialog.ShowDialog() == true)
             {
                 try {
                     // Auto-Fit logic
                     float minX = vm.Nodes.Min(n => n.X); float minY = vm.Nodes.Min(n => n.Y);
                     float maxX = vm.Nodes.Max(n => n.X); float maxY = vm.Nodes.Max(n => n.Y);
                     float graphW = maxX - minX + 100; float graphH = maxY - minY + 100;
                     float centerX = (minX + maxX) / 2; float centerY = (minY + maxY) / 2;

                     int imgSize = 2000;
                     float scale = Math.Min(imgSize / graphW, imgSize / graphH);

                     using var surface = SKSurface.Create(new SKImageInfo(imgSize, imgSize));
                     var c = surface.Canvas;
                     c.Clear(SKColors.White);
                     c.Translate(imgSize / 2, imgSize / 2);
                     c.Scale(scale);
                     c.Translate(-centerX, -centerY);
                     
                     // رسم برای اکسپورت: همیشه حالت عادی (بدون هایلایت)
                     DrawGraphSmart(c, vm.Nodes, vm.Edges, null);
                     
                     using var img = surface.Snapshot();
                     using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                     var pdf = new CdrGraph.Infrastructure.Services.PdfReportService();
                     pdf.GenerateReport(saveDialog.FileName, vm.Nodes, vm.Edges, data.ToArray());
                     MessageBox.Show("Export Successful!");
                 } catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
             }
        }
    }