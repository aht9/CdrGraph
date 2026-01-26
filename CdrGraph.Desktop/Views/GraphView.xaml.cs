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
    }

    // =========================================================
    //                 PHYSICS & ANIMATION LOOP
    // =========================================================
    private void OnRendering(object sender, EventArgs e)
    {
        var vm = DataContext as GraphViewModel;
        if (vm == null || vm.Nodes == null) return;

        bool needsRedraw = false;
        float repulsionRadius = 250f; // شعاع تاثیر دافعه
        float repulsionStrength = 100f; // قدرت هل دادن
        float smoothing = 0.15f; // سرعت نرم شدن (0.1 = کند و نرم، 0.9 = سریع)

        // 1. شناسایی منبع دافعه (Repeller)
        // منبع می‌تواند یک نود باشد یا یک خط (که موس روی آن است)
        object repellerGeometry = null;
        var exemptNodes = new HashSet<string>(); // نودهایی که نباید حرکت کنند (خود نودهای انتخاب شده)

        if (_hoveredNode != null)
        {
            repellerGeometry = _hoveredNode;
            exemptNodes.Add(_hoveredNode.Id);
            // همسایه‌های نود هم نباید فرار کنند (چون بهش وصلن)
            foreach (var edge in vm.Edges)
            {
                if (edge.SourceId == _hoveredNode.Id) exemptNodes.Add(edge.TargetId);
                if (edge.TargetId == _hoveredNode.Id) exemptNodes.Add(edge.SourceId);
            }
        }
        else if (_hoveredEdge != null)
        {
            repellerGeometry = _hoveredEdge;
            exemptNodes.Add(_hoveredEdge.SourceId);
            exemptNodes.Add(_hoveredEdge.TargetId);
        }
        else if (vm.SelectedNode != null)
        {
            // اگر چیزی هاور نیست اما نودی انتخاب شده، آن نود دافع است
            repellerGeometry = vm.SelectedNode;
            exemptNodes.Add(vm.SelectedNode.Id);
        }

        // 2. محاسبه و اعمال نیرو به نودها
        foreach (var node in vm.Nodes)
        {
            // مقدار هدف (Target Offset)
            float targetX = 0;
            float targetY = 0;

            // اگر نود جزو معاف‌ها نیست، محاسبه دافعه انجام شود
            if (repellerGeometry != null && !exemptNodes.Contains(node.Id))
            {
                if (repellerGeometry is GraphNode rNode)
                {
                    // دافعه از یک نقطه (نود)
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
                else if (repellerGeometry is GraphEdge rEdge)
                {
                    // دافعه از یک خط (Edge)
                    // پیدا کردن مختصات دو سر خط دافع
                    var s = vm.Nodes.FirstOrDefault(n => n.Id == rEdge.SourceId);
                    var t = vm.Nodes.FirstOrDefault(n => n.Id == rEdge.TargetId);

                    if (s != null && t != null)
                    {
                        // فاصله نقطه (نود جاری) از پاره‌خط
                        float dist = PointToSegmentDist(node.X, node.Y, s.X, s.Y, t.X, t.Y);
                        if (dist < repulsionRadius && dist > 1)
                        {
                            // بردار عمود بر خط برای هل دادن
                            // ساده‌ترین روش: بردار از مرکز خط به سمت نود
                            float midX = (s.X + t.X) / 2;
                            float midY = (s.Y + t.Y) / 2;
                            float dx = node.X - midX;
                            float dy = node.Y - midY;

                            // نرمالایز کردن جهت
                            float len = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (len > 0)
                            {
                                float force = (1 - (dist / repulsionRadius)) * repulsionStrength;
                                targetX = (dx / len) * force;
                                targetY = (dy / len) * force;
                            }
                        }
                    }
                }
            }

            // مقدار فعلی
            if (!_nodeAnimatedOffsets.ContainsKey(node.Id)) _nodeAnimatedOffsets[node.Id] = new SKPoint(0, 0);
            var current = _nodeAnimatedOffsets[node.Id];

            // درون‌یابی (Lerp) برای حرکت نرم
            float newX = current.X + (targetX - current.X) * smoothing;
            float newY = current.Y + (targetY - current.Y) * smoothing;

            // اگر تغییر ناچیز بود (زیر 0.1 پیکسل)، آپدیت نکن تا CPU درگیر نشود
            if (Math.Abs(newX - current.X) > 0.01f || Math.Abs(newY - current.Y) > 0.01f)
            {
                _nodeAnimatedOffsets[node.Id] = new SKPoint(newX, newY);
                needsRedraw = true;
            }
        }

        // 3. محاسبه و اعمال نیرو به خطوط (Edges)
        // خطوط باید خم شوند تا از مسیر نود/خط انتخابی دور شوند
        foreach (var edge in vm.Edges)
        {
            float targetX = 0;
            float targetY = 0;

            // اگر خط، خط انتخابی نیست
            if (repellerGeometry != null && edge != repellerGeometry)
            {
                // محاسبه نقطه وسط خط جاری
                var s = vm.Nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                var t = vm.Nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                if (s != null && t != null)
                {
                    float midX = (s.X + t.X) / 2;
                    float midY = (s.Y + t.Y) / 2;

                    // بررسی فاصله مرکز خط از دافع
                    if (repellerGeometry is GraphEdge rEdge)
                    {
                        // فاصله وسط خط جاری از خط دافع
                        var rs = vm.Nodes.FirstOrDefault(n => n.Id == rEdge.SourceId);
                        var rt = vm.Nodes.FirstOrDefault(n => n.Id == rEdge.TargetId);
                        if (rs != null && rt != null)
                        {
                            float dist = PointToSegmentDist(midX, midY, rs.X, rs.Y, rt.X, rt.Y);
                            if (dist < repulsionRadius)
                            {
                                // جهت دافعه: از مرکز خط دافع به مرکز خط جاری
                                float rMidX = (rs.X + rt.X) / 2;
                                float rMidY = (rs.Y + rt.Y) / 2;
                                float dx = midX - rMidX;
                                float dy = midY - rMidY;
                                float len = (float)Math.Sqrt(dx * dx + dy * dy);

                                if (len > 0)
                                {
                                    float force = (1 - (dist / repulsionRadius)) * repulsionStrength;
                                    targetX = (dx / len) * force;
                                    targetY = (dy / len) * force;
                                }
                            }
                        }
                    }
                }
            }

            // Lerp برای خطوط
            float diffX = targetX - edge.AnimatedOffsetX;
            float diffY = targetY - edge.AnimatedOffsetY;

            if (Math.Abs(diffX) > 0.01f || Math.Abs(diffY) > 0.01f)
            {
                edge.AnimatedOffsetX += diffX * smoothing;
                edge.AnimatedOffsetY += diffY * smoothing;
                needsRedraw = true;
            }
        }

        if (needsRedraw)
        {
            GraphCanvas.InvalidateVisual();
        }
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

        // Highlight Selected Node
        if (vm.SelectedNode != null)
        {
            // محاسبه پوزیشن نهایی (شامل انیمیشن)
            SKPoint animOffset = GetNodeAnimOffset(vm.SelectedNode.Id);
            float finalX = vm.SelectedNode.X + animOffset.X;
            float finalY = vm.SelectedNode.Y + animOffset.Y;

            float strokeWidth = 3f / _scale;
            using (var selectionPaint = new SKPaint
                   {
                       Style = SKPaintStyle.Stroke, Color = SKColors.Yellow, StrokeWidth = strokeWidth,
                       IsAntialias = true
                   })
            {
                float radius = (10 + (float)(vm.SelectedNode.Weight * 2)) * 1.8f;
                canvas.DrawCircle(finalX, finalY, radius + 4, selectionPaint);
            }
        }

        canvas.Restore();

        // Tooltips
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

    private SKPoint GetNodeAnimOffset(string nodeId)
    {
        if (_nodeAnimatedOffsets.TryGetValue(nodeId, out var p)) return p;
        return SKPoint.Empty;
    }

    private void DrawGraphSmart(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges, GraphNode focusNode)
    {
        // تابع کمکی برای گرفتن مختصات نهایی نود (پایه + انیمیشن)
        SKPoint GetPos(string id)
        {
            var n = nodes.FirstOrDefault(x => x.Id == id);
            if (n == null) return SKPoint.Empty;
            var anim = GetNodeAnimOffset(id);
            return new SKPoint(n.X + anim.X, n.Y + anim.Y);
        }

        if (focusNode == null)
        {
            // --- حالت عادی ---
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
            // --- حالت فوکوس ---
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

            // 1. پس‌زمینه (کمرنگ)
            foreach (var edge in edges)
            {
                if (!connectedEdges.Contains(edge))
                {
                    var p1 = GetPos(edge.SourceId);
                    var p2 = GetPos(edge.TargetId);
                    if (!p1.IsEmpty && !p2.IsEmpty)
                    {
                        _dimmedEdgePaint.StrokeWidth = 1;
                        DrawCurvedLine(canvas, p1.X, p1.Y, p2.X, p2.Y, edge, _dimmedEdgePaint);
                    }
                }
            }

            foreach (var node in nodes)
            {
                if (!connectedNodeIds.Contains(node.Id)) DrawSingleNode(canvas, node, true);
            }

            // 2. پیش‌زمینه (پررنگ)
            foreach (var edge in connectedEdges)
            {
                var p1 = GetPos(edge.SourceId);
                var p2 = GetPos(edge.TargetId);
                if (!p1.IsEmpty && !p2.IsEmpty)
                {
                    _activeEdgePaint.StrokeWidth = edge.Thickness + 2;
                    DrawCurvedLine(canvas, p1.X, p1.Y, p2.X, p2.Y, edge, _activeEdgePaint);
                }
            }

            foreach (var node in nodes)
            {
                if (connectedNodeIds.Contains(node.Id))
                {
                    DrawSingleNode(canvas, node, false, node.Id == focusNode.Id);
                }
            }
        }
    }

    private void DrawCurvedLine(SKCanvas canvas, float x1, float y1, float x2, float y2, GraphEdge edge, SKPaint paint)
    {
        // آفست نهایی = دستی + انیمیشن دافعه
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

    private SKPoint GetControlPoint(float x1, float y1, float x2, float y2, float offsetX = 0, float offsetY = 0)
    {
        float midX = (x1 + x2) / 2;
        float midY = (y1 + y2) / 2;
        float defaultCurveFactor = 0.15f;
        float deltaX = x2 - x1;
        float deltaY = y2 - y1;
        float defaultCpX = midX - (deltaY * defaultCurveFactor);
        float defaultCpY = midY + (deltaX * defaultCurveFactor);
        return new SKPoint(defaultCpX + offsetX, defaultCpY + offsetY);
    }

    private void DrawSingleNode(SKCanvas canvas, GraphNode node, bool isDimmed, bool isActive = false)
    {
        // اعمال آفست انیمیشن روی مختصات نود
        SKPoint anim = GetNodeAnimOffset(node.Id);
        float drawX = node.X + anim.X;
        float drawY = node.Y + anim.Y;

        float baseRadius = 10 + (float)(node.Weight * 2);
        float radius = isActive ? baseRadius * 1.8f : baseRadius;

        SKPaint paint;
        if (isDimmed)
        {
            var nodeColor = !string.IsNullOrEmpty(node.Color) ? SKColor.Parse(node.Color) : SKColors.DodgerBlue;
            paint = new SKPaint { Style = SKPaintStyle.Fill, Color = nodeColor.WithAlpha(40), IsAntialias = true };
        }
        else if (isActive || node == _hoveredNode)
        {
            paint = _activeNodePaint;
        }
        else
        {
            var nodeColor = !string.IsNullOrEmpty(node.Color) ? SKColor.Parse(node.Color) : SKColors.DodgerBlue;
            paint = new SKPaint { Style = SKPaintStyle.Fill, Color = nodeColor, IsAntialias = true };
        }

        canvas.DrawCircle(drawX, drawY, radius, paint);

        if (!isDimmed && (_scale > 0.6f || isActive || node == _hoveredNode))
        {
            float textOffset = isActive ? 25 : 15;
            canvas.DrawText(node.Id, drawX, drawY + radius + textOffset, _textPaint);
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

            // هنگام کلیک، باید مختصات انیمیشن‌دار را چک کنیم
            // چون نودها ممکن است جابجا شده باشند
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
                var clickedEdge = FindEdgeAt(vm.Nodes, vm.Edges, worldX, worldY);
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

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        var zoomFactor = 1.1f;
        float newScale = e.Delta > 0 ? _scale * zoomFactor : _scale / zoomFactor;
        newScale = Math.Clamp(newScale, 0.1f, 10.0f);

        var vm = DataContext as GraphViewModel;
        if (vm != null) vm.SelectedZoom = $"{(int)(newScale * 100)}%";

        float scaleRatio = newScale / _scale;
        _offset.X = (float)pos.X - ((float)pos.X - _offset.X) * scaleRatio;
        _offset.Y = (float)pos.Y - ((float)pos.Y - _offset.Y) * scaleRatio;
        _scale = newScale;
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
        float screenTolerance = 15f;
        float worldTolerance = screenTolerance / _scale;

        foreach (var edge in edges)
        {
            var s = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var t = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
            if (s == null || t == null) continue;

            // استفاده از مختصات انیمیشنی نودها
            SKPoint animS = GetNodeAnimOffset(s.Id);
            SKPoint animT = GetNodeAnimOffset(t.Id);

            float sx = s.X + animS.X;
            float sy = s.Y + animS.Y;
            float tx = t.X + animT.X;
            float ty = t.Y + animT.Y;

            float totalOffsetX = edge.ControlPointOffsetX + edge.AnimatedOffsetX;
            float totalOffsetY = edge.ControlPointOffsetY + edge.AnimatedOffsetY;

            var cp = GetControlPoint(sx, sy, tx, ty, totalOffsetX, totalOffsetY);
            float dist = GetDistanceToBezier(new SKPoint(x, y), new SKPoint(sx, sy), cp, new SKPoint(tx, ty));

            if (dist < (edge.Thickness / 2 + worldTolerance)) return edge;
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
                c.Clear(_bgColor);
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