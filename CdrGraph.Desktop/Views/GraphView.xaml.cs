using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using CdrGraph.Desktop.ViewModels;
using CdrGraph.Core.Domain.Models;
using Microsoft.Win32;

namespace CdrGraph.Desktop.Views;

public partial class GraphView : UserControl
{
    // --- وضعیت دوربین ---
    private float _scale = 1.0f;
    private SKPoint _offset = new SKPoint(0, 0);

    // --- وضعیت تعامل ---
    private bool _isPanning;
    private GraphNode _draggedNode;
    private GraphEdge _draggedEdge;
    private Point _lastMousePos;
    private SKPoint _lastMouseWorldPos;

    private GraphEdge _hoveredEdge;
    private GraphNode _hoveredNode;

    // --- وضعیت انیمیشن (Visual State) ---
    // نگهداری آفست انیمیشنی برای نودها (بدون تغییر در مدل اصلی)
    private readonly Dictionary<string, SKPoint> _nodeAnimatedOffsets = new Dictionary<string, SKPoint>();


    // --- تنظیمات تم و قلم‌ها ---
    private SKColor _bgColor = SKColor.Parse("#1E1E1E");
    private SKColor _defaultEdgeColor = SKColors.Gray.WithAlpha(100);
    private SKColor _defaultTextColor = SKColors.White;

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
        InitializePaints();

        // فعال‌سازی حلقه انیمیشن (60 FPS)
        CompositionTarget.Rendering += OnRendering;

        // اتصال متد تولید تصویر به ViewModel
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is GraphViewModel vm)
        {
            vm.SubGraphImageGenerator = GenerateSubGraphImage;
            // *** اتصال متد فوکوس برای جستجو ***
            vm.RequestFocusOnNode = FocusOnNode;
        }
    }

    // *** متد جدید: زوم و تمرکز روی نود خاص ***
    private void FocusOnNode(GraphNode node)
    {
        if (node == null) return;

        // دریافت مختصات نهایی نود (شامل انیمیشن)
        SKPoint anim = GetNodeAnimOffset(node.Id);
        float nodeX = node.X + anim.X;
        float nodeY = node.Y + anim.Y;

        float viewWidth = (float)GraphCanvas.ActualWidth;
        float viewHeight = (float)GraphCanvas.ActualHeight;

        if (viewWidth <= 0 || viewHeight <= 0) return;

        // زوم هدف (اگر زوم فعلی کم است، زیادش کن. اگر زیاد است، حفظ کن)
        float targetScale = Math.Max(_scale, 1.5f);

        // فرمول محاسبه آفست برای مرکز قرار دادن نود:
        // CenterX = NodeX * Scale + OffsetX
        // OffsetX = CenterX - NodeX * Scale
        float newOffsetX = (viewWidth / 2) - (nodeX * targetScale);
        float newOffsetY = (viewHeight / 2) - (nodeY * targetScale);

        _scale = targetScale;
        _offset = new SKPoint(newOffsetX, newOffsetY);

        // بروزرسانی کمبوباکس زوم در ViewModel
        if (DataContext is GraphViewModel vm)
        {
            vm.SelectedZoom = $"{(int)(_scale * 100)}%";
        }

        GraphCanvas.InvalidateVisual();
    }

    // =========================================================
    //                 PHYSICS & ANIMATION LOOP
    // =========================================================
    private void OnRendering(object sender, EventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null || vm.Nodes == null) return;

        bool needsRedraw = false;
        float repulsionRadius = 150f;
        float repulsionStrength = 80f;
        float smoothing = 0.2f;

        object repellerGeometry = null;
        var exemptNodes = new HashSet<string>();

        // تعیین منبع دافعه (فقط نودها)
        if (_hoveredNode != null)
        {
            repellerGeometry = _hoveredNode;
            exemptNodes.Add(_hoveredNode.Id);
            // همسایگان نود هاور شده ثابت بمانند
            foreach (var edge in vm.Edges)
            {
                if (edge.SourceId == _hoveredNode.Id) exemptNodes.Add(edge.TargetId);
                if (edge.TargetId == _hoveredNode.Id) exemptNodes.Add(edge.SourceId);
            }
        }
        else if (vm.SelectedNode != null)
        {
            repellerGeometry = vm.SelectedNode;
            exemptNodes.Add(vm.SelectedNode.Id);
        }

        // 1. فیزیک نودها (برای جلوگیری از تراکم)
        foreach (var node in vm.Nodes)
        {
            float targetX = 0;
            float targetY = 0;

            if (repellerGeometry != null && !exemptNodes.Contains(node.Id))
            {
                if (repellerGeometry is GraphNode rNode)
                {
                    float dx = node.X - rNode.X;
                    float dy = node.Y - rNode.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < repulsionRadius * repulsionRadius && distSq > 1)
                    {
                        float dist = (float)Math.Sqrt(distSq);
                        float force = (1 - (dist / repulsionRadius)) * repulsionStrength;
                        targetX = (dx / dist) * force;
                        targetY = (dy / dist) * force;
                    }
                }
            }

            if (!_nodeAnimatedOffsets.ContainsKey(node.Id)) _nodeAnimatedOffsets[node.Id] = new SKPoint(0, 0);
            var current = _nodeAnimatedOffsets[node.Id];

            float newX = current.X + (targetX - current.X) * smoothing;
            float newY = current.Y + (targetY - current.Y) * smoothing;

            if (Math.Abs(newX - current.X) > 0.01f || Math.Abs(newY - current.Y) > 0.01f)
            {
                _nodeAnimatedOffsets[node.Id] = new SKPoint(newX, newY);
                needsRedraw = true;
            }
        }

        // *** حذف فیزیک دافعه خطوط برای ثبات ***
        // فقط ریست کردن مقادیر قبلی
        foreach (var edge in vm.Edges)
        {
            if (edge.AnimatedOffsetX != 0 || edge.AnimatedOffsetY != 0)
            {
                edge.AnimatedOffsetX += (0 - edge.AnimatedOffsetX) * smoothing;
                edge.AnimatedOffsetY += (0 - edge.AnimatedOffsetY) * smoothing;
                if (Math.Abs(edge.AnimatedOffsetX) < 0.1f) edge.AnimatedOffsetX = 0;
                if (Math.Abs(edge.AnimatedOffsetY) < 0.1f) edge.AnimatedOffsetY = 0;
                needsRedraw = true;
            }
        }

        if (needsRedraw) GraphCanvas.InvalidateVisual();
    }
    // =========================================================
    //                 RENDERING
    // =========================================================

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var vm = DataContext as GraphViewModel;

        canvas.Clear(_bgColor);

        if (vm == null || vm.Nodes == null) return;

        float density = 1.0f;
        if (GraphCanvas.ActualWidth > 0) density = (float)(e.Info.Width / GraphCanvas.ActualWidth);

        canvas.Save();
        canvas.Scale(density);
        canvas.Translate(_offset.X, _offset.Y);
        canvas.Scale(_scale);

        var focusNode = _draggedNode ?? _hoveredNode ?? vm.SelectedNode;

        DrawGraphSmart(canvas, vm.Nodes, vm.Edges, focusNode);

        if (vm.SelectedNodes != null && vm.SelectedNodes.Any())
        {
            float strokeWidth = 3f / _scale;
            using (var selectionPaint = new SKPaint
                   {
                       Style = SKPaintStyle.Stroke, Color = SKColors.Yellow, StrokeWidth = strokeWidth,
                       IsAntialias = true
                   })
            {
                foreach (var node in vm.SelectedNodes)
                {
                    SKPoint animOffset = GetNodeAnimOffset(node.Id);
                    float finalX = node.X + animOffset.X;
                    float finalY = node.Y + animOffset.Y;
                    float radius = (10 + (float)(node.Weight * 2)) * 1.8f;
                    canvas.DrawCircle(finalX, finalY, radius + 4, selectionPaint);
                }
            }
        }

        canvas.Restore();

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

    private byte[] GenerateSubGraphImage(List<GraphNode> nodes, List<GraphEdge> edges, bool showDuration)
    {
        if (nodes == null || !nodes.Any()) return null;

        float minX = nodes.Min(n => n.X);
        float minY = nodes.Min(n => n.Y);
        float maxX = nodes.Max(n => n.X);
        float maxY = nodes.Max(n => n.Y);

        float padding = 100f;
        float graphWidth = maxX - minX + (padding * 2);
        float graphHeight = maxY - minY + (padding * 2);
        float centerX = (minX + maxX) / 2;
        float centerY = (minY + maxY) / 2;

        int imgWidth = 1200;
        int imgHeight = 900;
        float scale = Math.Min(imgWidth / graphWidth, imgHeight / graphHeight);

        // سایز فونت داینامیک برای خوانایی در خروجی
        float optimalFontSize = 16f / scale;
        if (optimalFontSize < 12) optimalFontSize = 12;

        using var surface = SKSurface.Create(new SKImageInfo(imgWidth, imgHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        canvas.Translate(imgWidth / 2, imgHeight / 2);
        canvas.Scale(scale);
        canvas.Translate(-centerX, -centerY);

        foreach (var edge in edges)
        {
            var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
            if (s == null || t == null) continue;

            var paint = _edgePaint.Clone();
            paint.Color = SKColors.Black.WithAlpha(180);
            paint.StrokeWidth = Math.Max(edge.Thickness, 2);

            var cp = GetControlPoint(s.X, s.Y, t.X, t.Y, edge.ControlPointOffsetX, edge.ControlPointOffsetY);
            using (var path = new SKPath())
            {
                path.MoveTo(s.X, s.Y);
                path.QuadTo(cp.X, cp.Y, t.X, t.Y);
                canvas.DrawPath(path, paint);
            }

            string labelText = showDuration ? $"{edge.TotalDurationMinutes:N0}m" : $"{edge.CallCount}c";
            DrawEdgeLabel(canvas, s, t, edge, labelText, optimalFontSize);
        }

        foreach (var node in nodes)
        {
            float radius = 15 + (float)(node.Weight * 5);
            var nodePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.DodgerBlue, IsAntialias = true };

            var vm = DataContext as GraphViewModel;
            if (vm != null && vm.SelectedNodes.Any(sn => sn.Id == node.Id))
            {
                nodePaint.Color = SKColors.Orange;
            }
            else if (!string.IsNullOrEmpty(node.Color))
            {
                nodePaint.Color = SKColor.Parse(node.Color);
            }

            canvas.DrawCircle(node.X, node.Y, radius, nodePaint);

            var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = optimalFontSize,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };
            canvas.DrawText(node.Id, node.X, node.Y + radius + optimalFontSize, textPaint);
        }

        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void DrawEdgeLabel(SKCanvas canvas, GraphNode s, GraphNode t, GraphEdge edge, string text, float fontSize)
    {
        float midX = (s.X + t.X) / 2;
        float midY = (s.Y + t.Y) / 2;
        float curveFactor = 0.15f;
        float deltaX = t.X - s.X;
        float deltaY = t.Y - s.Y;
        float cpX = midX - (deltaY * curveFactor) + edge.ControlPointOffsetX;
        float cpY = midY + (deltaX * curveFactor) + edge.ControlPointOffsetY;

        float labelX = 0.25f * s.X + 0.5f * cpX + 0.25f * t.X;
        float labelY = 0.25f * s.Y + 0.5f * cpY + 0.25f * t.Y;

        var textPaint = new SKPaint
        {
            Color = SKColors.DarkRed,
            TextSize = fontSize,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        float textWidth = textPaint.MeasureText(text);
        float padding = fontSize * 0.4f;

        var bgRect = new SKRect(
            labelX - textWidth / 2 - padding,
            labelY - fontSize,
            labelX + textWidth / 2 + padding,
            labelY + (fontSize * 0.3f)
        );

        var bgPaint = new SKPaint { Color = SKColors.White.WithAlpha(230), Style = SKPaintStyle.Fill };

        canvas.DrawRoundRect(bgRect, 5, 5, bgPaint);
        canvas.DrawText(text, labelX, labelY, textPaint);
    }

    private SKPoint GetNodeAnimOffset(string nodeId)
    {
        if (_nodeAnimatedOffsets.TryGetValue(nodeId, out var p)) return p;
        return SKPoint.Empty;
    }

    private void DrawGraphSmart(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges, GraphNode focusNode)
    {
        SKPoint GetPos(string id)
        {
            var n = nodes.FirstOrDefault(x => x.Id == id);
            if (n == null) return SKPoint.Empty;
            var anim = GetNodeAnimOffset(id);
            return new SKPoint(n.X + anim.X, n.Y + anim.Y);
        }

        if (focusNode == null)
        {
            foreach (var edge in edges)
            {
                var p1 = GetPos(edge.SourceId);
                var p2 = GetPos(edge.TargetId);
                if (p1.IsEmpty || p2.IsEmpty) continue;
                var paint = (edge == _hoveredEdge) ? _activeEdgePaint : _edgePaint;
                if (edge != _hoveredEdge) paint.StrokeWidth = edge.Thickness;
                else paint.StrokeWidth = edge.Thickness + 2;
                DrawCurvedLine(canvas, p1.X, p1.Y, p2.X, p2.Y, edge, paint);
            }

            foreach (var node in nodes) DrawSingleNode(canvas, node, false);
        }
        else
        {
            var connectedNodeIds = new HashSet<string> { focusNode.Id };
            foreach (var edge in edges)
            {
                if (edge.SourceId == focusNode.Id || edge.TargetId == focusNode.Id)
                    connectedNodeIds.Add(edge.SourceId == focusNode.Id ? edge.TargetId : edge.SourceId);
            }

            foreach (var edge in edges)
            {
                var p1 = GetPos(edge.SourceId);
                var p2 = GetPos(edge.TargetId);
                if (p1.IsEmpty || p2.IsEmpty) continue;
                bool isConnected = edge.SourceId == focusNode.Id || edge.TargetId == focusNode.Id;
                var paint = isConnected ? _activeEdgePaint : _dimmedEdgePaint;
                if (!isConnected) paint.StrokeWidth = 1;
                else paint.StrokeWidth = edge.Thickness + 2;
                DrawCurvedLine(canvas, p1.X, p1.Y, p2.X, p2.Y, edge, paint);
            }

            foreach (var node in nodes)
            {
                bool isConnected = connectedNodeIds.Contains(node.Id);
                DrawSingleNode(canvas, node, !isConnected, node.Id == focusNode.Id);
            }
        }
    }


    private void DrawCurvedLine(SKCanvas canvas, float x1, float y1, float x2, float y2, GraphEdge edge, SKPaint paint)
    {
        float totalOffsetX = edge.ControlPointOffsetX + edge.AnimatedOffsetX;
        float totalOffsetY = edge.ControlPointOffsetY + edge.AnimatedOffsetY;
        var cp = GetControlPoint(x1, y1, x2, y2, totalOffsetX, totalOffsetY);
        using (var path = new SKPath())
        {
            path.MoveTo(x1, y1);
            path.QuadTo(cp.X, cp.Y, x2, y2);
            canvas.DrawPath(path, paint);
        }
    }

    private SKPoint GetControlPoint(float x1, float y1, float x2, float y2, float offsetX, float offsetY)
    {
        float midX = (x1 + x2) / 2;
        float midY = (y1 + y2) / 2;
        float deltaX = x2 - x1;
        float deltaY = y2 - y1;
        return new SKPoint(midX - (deltaY * 0.15f) + offsetX, midY + (deltaX * 0.15f) + offsetY);
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


    // --- Mouse Interaction ---

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;
        if (e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            float wx = ((float)pos.X - _offset.X) / _scale;
            float wy = ((float)pos.Y - _offset.Y) / _scale;

            GraphNode clickedNode = null;
            for (int i = vm.Nodes.Count - 1; i >= 0; i--)
            {
                var n = vm.Nodes[i];
                SKPoint anim = GetNodeAnimOffset(n.Id);
                float dist = (float)Math.Sqrt(Math.Pow((n.X + anim.X) - wx, 2) + Math.Pow((n.Y + anim.Y) - wy, 2));
                if (dist < (15 + n.Weight * 2))
                {
                    clickedNode = n;
                    break;
                }
            }

            if (clickedNode != null)
            {
                bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                vm.ToggleNodeSelection(clickedNode, isCtrl);
                _draggedNode = clickedNode;
                _lastMousePos = pos;
                GraphCanvas.CaptureMouse();
                Cursor = Cursors.SizeAll;
            }
            else
            {
                var clickedEdge = FindEdgeAt(vm.Nodes, vm.Edges, wx, wy);
                if (clickedEdge != null)
                {
                    _draggedEdge = clickedEdge;
                    _lastMousePos = pos;
                    GraphCanvas.CaptureMouse();
                    Cursor = Cursors.Hand;
                }
                else
                {
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

    // ... (OnMouseMove, OnMouseUp, OnMouseWheel - مشابه قبل) ...
    // تغییر مهم: در FindNodeAt باید آفست انیمیشن را لحاظ کنیم

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        float wx = ((float)pos.X - _offset.X) / _scale;
        float wy = ((float)pos.Y - _offset.Y) / _scale;
        _lastMouseWorldPos = new SKPoint(wx, wy);

        if (_draggedNode != null)
        {
            float dx = (float)(pos.X - _lastMousePos.X) / _scale;
            float dy = (float)(pos.Y - _lastMousePos.Y) / _scale;
            _draggedNode.X += dx;
            _draggedNode.Y += dy;
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else if (_draggedEdge != null)
        {
            float dx = (float)(pos.X - _lastMousePos.X) / _scale;
            float dy = (float)(pos.Y - _lastMousePos.Y) / _scale;
            _draggedEdge.ControlPointOffsetX += dx;
            _draggedEdge.ControlPointOffsetY += dy;
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else if (_isPanning)
        {
            _offset.X += (float)(pos.X - _lastMousePos.X);
            _offset.Y += (float)(pos.Y - _lastMousePos.Y);
            _lastMousePos = pos;
            GraphCanvas.InvalidateVisual();
        }
        else
        {
            var vm = DataContext as GraphViewModel;
            if (vm != null && vm.Nodes != null)
            {
                GraphNode found = null;
                for (int i = vm.Nodes.Count - 1; i >= 0; i--)
                {
                    var n = vm.Nodes[i];
                    SKPoint a = GetNodeAnimOffset(n.Id);
                    if (Math.Sqrt(Math.Pow((n.X + a.X) - wx, 2) + Math.Pow((n.Y + a.Y) - wy, 2)) < (15 + n.Weight * 2))
                    {
                        found = n;
                        break;
                    }
                }

                if (found != _hoveredNode)
                {
                    _hoveredNode = found;
                    GraphCanvas.InvalidateVisual();
                }

                if (_hoveredNode == null)
                {
                    var foundEdge = FindEdgeAt(vm.Nodes, vm.Edges, wx, wy);
                    if (foundEdge != _hoveredEdge)
                    {
                        _hoveredEdge = foundEdge;
                        GraphCanvas.InvalidateVisual();
                    }
                }
                else if (_hoveredEdge != null)
                {
                    _hoveredEdge = null;
                    GraphCanvas.InvalidateVisual();
                }

                Cursor = (_hoveredNode != null || _hoveredEdge != null) ? Cursors.Hand : Cursors.Arrow;
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


    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        float zf = 1.1f;
        float ns = e.Delta > 0 ? _scale * zf : _scale / zf;
        ns = Math.Clamp(ns, 0.1f, 10.0f);
        var vm = DataContext as GraphViewModel;
        if (vm != null) vm.SelectedZoom = $"{(int)(ns * 100)}%";
        float r = ns / _scale;
        _offset.X = (float)pos.X - ((float)pos.X - _offset.X) * r;
        _offset.Y = (float)pos.Y - ((float)pos.Y - _offset.Y) * r;
        _scale = ns;
        GraphCanvas.InvalidateVisual();
    }

    // --- Helpers Updated for Animation ---

    private GraphNode FindNodeAt(List<GraphNode> nodes, float x, float y)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            var vm = DataContext as GraphViewModel;
            bool isActive = vm?.SelectedNode == n;
            float radiusMultiplier = isActive ? 1.8f : 1.0f;

            // استفاده از مختصات انیمیشنی برای تشخیص کلیک دقیق
            SKPoint anim = GetNodeAnimOffset(n.Id);
            float nx = n.X + anim.X;
            float ny = n.Y + anim.Y;

            float dist = (float)Math.Sqrt(Math.Pow(nx - x, 2) + Math.Pow(ny - y, 2));
            if (dist < (15 + n.Weight * 2) * radiusMultiplier) return n;
        }

        return null;
    }

    private GraphEdge FindEdgeAt(List<GraphNode> nodes, List<GraphEdge> edges, float x, float y)
    {
        float tolerance = 15f / _scale;
        foreach (var edge in edges)
        {
            var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
            if (s == null || t == null) continue;

            SKPoint animS = GetNodeAnimOffset(s.Id);
            SKPoint animT = GetNodeAnimOffset(t.Id);

            float sx = s.X + animS.X;
            float sy = s.Y + animS.Y;
            float tx = t.X + animT.X;
            float ty = t.Y + animT.Y;

            float ox = edge.ControlPointOffsetX + edge.AnimatedOffsetX;
            float oy = edge.ControlPointOffsetY + edge.AnimatedOffsetY;

            var cp = GetControlPoint(sx, sy, tx, ty, ox, oy);
            float dist = GetDistanceToBezier(new SKPoint(x, y), new SKPoint(sx, sy), cp, new SKPoint(tx, ty));

            if (dist < (edge.Thickness / 2 + tolerance)) return edge;
        }

        return null;
    }

    // ... (بقیه متدهای کمکی مانند GetDistanceToBezier, PointToSegmentDist, DrawTooltip, ExportGraph بدون تغییر) ...

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

        float lineHeight = 28;
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

    private string GetNodeInfo(GraphNode node) =>
        $"Number: {node.Id}\nTotal Calls: {node.TotalCalls}\nDuration: {node.TotalDurationMinutes:N1} min";

    private string GetEdgeInfo(GraphEdge edge, GraphViewModel vm) =>
        $"Calls: {edge.CallCount}\nDuration: {edge.TotalDurationMinutes:N1} min";

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null) return;

        if (vm.SelectedTheme == "Light Mode")
        {
            _bgColor = SKColors.White;
            _defaultEdgeColor = SKColors.Black.WithAlpha(150);
            _defaultTextColor = SKColors.Black;
            _tooltipBgPaint.Color = SKColors.White.WithAlpha(240);
            _tooltipBorderPaint.Color = SKColors.Black.WithAlpha(50);
            _tooltipTextPaint.Color = SKColors.Black;
        }
        else
        {
            _bgColor = SKColor.Parse("#1E1E1E");
            _defaultEdgeColor = SKColors.Gray.WithAlpha(100);
            _defaultTextColor = SKColors.White;
            _tooltipBgPaint.Color = SKColors.Black.WithAlpha(240);
            _tooltipBorderPaint.Color = SKColors.White.WithAlpha(150);
            _tooltipTextPaint.Color = SKColors.White;
        }

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
            string zoomStr = vm.SelectedZoom.Replace("%", "");
            if (float.TryParse(zoomStr, out float zoomVal))
            {
                _scale = zoomVal / 100f;
                GraphCanvas.InvalidateVisual();
            }
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportGraph();

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
        _activeNodePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Orange, IsAntialias = true };

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

    public async void ExportGraph()
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null || !vm.Nodes.Any()) return;

        // 1. تولید تصویر گراف
        byte[] imageBytes = null;
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
            c.Clear(_bgColor);
            c.Translate(imgSize / 2, imgSize / 2);
            c.Scale(scale);
            c.Translate(-centerX, -centerY);

            DrawGraphSmart(c, vm.Nodes, vm.Edges, null);

            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            imageBytes = data.ToArray();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Image Generation Error: " + ex.Message);
            return;
        }

        // 2. درخواست گزارش کامل
        await vm.ExportComprehensiveReportAsync(imageBytes);
    }
}