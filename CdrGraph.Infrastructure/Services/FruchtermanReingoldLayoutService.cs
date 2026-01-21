using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;

namespace CdrGraph.Infrastructure.Services;

public class FruchtermanReingoldLayoutService : IGraphLayoutService
{
    private const int MaxIterations = 100; // تعداد تکرار الگوریتم
    private const float InitialTemp = 100f; // دمای اولیه برای جابجایی
    private const float Padding = 50f;

    public Task ApplyLayoutAsync(List<GraphNode> nodes, List<GraphEdge> edges,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (nodes == null || !nodes.Any()) return;

            // 1. تنظیم فضای اولیه (Canvas)
            float width = 2000f;
            float height = 2000f;
            float area = width * height;

            // فرمول محاسبه فاصله ایده‌آل بین نودها (k)
            // k = C * sqrt(area / number_of_nodes)
            float k = (float)(0.75 * Math.Sqrt(area / nodes.Count));

            float t = InitialTemp;

            // دیکشنری برای نگهداری جابجایی‌ها در هر مرحله
            var displacements = nodes.ToDictionary(n => n.Id, n => new Vector2(0, 0));

            // 2. شروع حلقه تکرار
            for (int i = 0; i < MaxIterations; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // الف) محاسبه نیروی دافعه (Repulsive Force) بین همه نودها
                foreach (var v in nodes)
                {
                    displacements[v.Id] = new Vector2(0, 0); // ریست کردن جابجایی
                    foreach (var u in nodes)
                    {
                        if (v == u) continue;

                        var delta = new Vector2(v.X - u.X, v.Y - u.Y);
                        float dist = delta.Length();
                        if (dist < 0.01f) dist = 0.01f; // جلوگیری از تقسیم بر صفر

                        // فرمول دافعه: Fr = k^2 / dist
                        float force = (k * k) / dist;

                        var displacement = delta.Normalize() * force;
                        displacements[v.Id] += displacement;
                    }
                }

                // ب) محاسبه نیروی جاذبه (Attractive Force) برای نودهای متصل
                foreach (var edge in edges)
                {
                    var v = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                    var u = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                    if (v == null || u == null) continue;

                    var delta = new Vector2(v.X - u.X, v.Y - u.Y);
                    float dist = delta.Length();
                    if (dist < 0.01f) dist = 0.01f;

                    // فرمول جاذبه: Fa = dist^2 / k
                    // * وزن تماس‌ها باعث نزدیکی بیشتر می‌شود
                    float weightFactor = (float)(1 + edge.CalculatedWeight);
                    float force = (dist * dist) / k * weightFactor;

                    var displacement = delta.Normalize() * force;

                    displacements[v.Id] -= displacement;
                    displacements[u.Id] += displacement;
                }

                // ج) اعمال جابجایی و کاهش دما
                foreach (var v in nodes)
                {
                    var disp = displacements[v.Id];
                    float dist = disp.Length();

                    // محدود کردن حرکت با دما (Simulated Annealing)
                    float limit = Math.Min(dist, t);
                    var move = disp.Normalize() * limit;

                    v.X += move.X;
                    v.Y += move.Y;

                    // نگه داشتن نود داخل کادر (Optional)
                    v.X = Math.Min(width - Padding, Math.Max(Padding, v.X));
                    v.Y = Math.Min(height - Padding, Math.Max(Padding, v.Y));
                }

                // سرد کردن سیستم
                t *= 0.95f;
            }
        }, cancellationToken);
    }

    // کلاس کمکی داخلی برای محاسبات برداری
    private struct Vector2
    {
        public float X, Y;

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length() => (float)Math.Sqrt(X * X + Y * Y);

        public Vector2 Normalize()
        {
            float len = Length();
            return len == 0 ? new Vector2(0, 0) : new Vector2(X / len, Y / len);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.X * d, a.Y * d);
    }
}