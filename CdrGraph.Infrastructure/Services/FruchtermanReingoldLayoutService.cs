using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;

namespace CdrGraph.Infrastructure.Services;

public class FruchtermanReingoldLayoutService : IGraphLayoutService
{
    private const int MaxIterations = 300; // افزایش تعداد تکرار برای نتیجه بهتر
    private const float InitialTemp = 200f;

    // ابعاد بوم فرضی برای محاسبات
    private const float CanvasWidth = 2000f;
    private const float CanvasHeight = 2000f;

    public Task ApplyLayoutAsync(List<GraphNode> nodes, List<GraphEdge> edges,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (nodes == null || !nodes.Any()) return;

            var rand = new Random();

            // 1. پخش تصادفی نودها (بسیار مهم: اگر این کار انجام نشود، نودها روی هم می‌مانند)
            foreach (var node in nodes)
            {
                node.X = (float)rand.NextDouble() * CanvasWidth;
                node.Y = (float)rand.NextDouble() * CanvasHeight;
            }

            float area = CanvasWidth * CanvasHeight;
            // فاصله ایده‌آل (k)
            float k = (float)(Math.Sqrt(area / nodes.Count));

            float t = InitialTemp;

            // دیکشنری برای نگهداری جابجایی‌ها
            var displacements = nodes.ToDictionary(n => n.Id, n => new Vector2(0, 0));

            // 2. حلقه اصلی الگوریتم
            for (int i = 0; i < MaxIterations; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // الف) محاسبه نیروی دافعه (Repulsion)
                // نودها همدیگر را دفع می‌کنند تا روی هم نیفتند
                foreach (var v in nodes)
                {
                    displacements[v.Id] = new Vector2(0, 0);
                    foreach (var u in nodes)
                    {
                        if (v == u) continue;

                        var delta = new Vector2(v.X - u.X, v.Y - u.Y);
                        float dist = delta.Length();

                        // جلوگیری از تقسیم بر صفر (اگر نودها خیلی نزدیک شدند)
                        if (dist < 0.1f) dist = 0.1f;

                        // فرمول دافعه: Fr = k^2 / dist
                        float force = (k * k) / dist;

                        var displacement = delta.Normalize() * force;
                        displacements[v.Id] += displacement;
                    }
                }

                // ب) محاسبه نیروی جاذبه (Attraction)
                // نودهای متصل همدیگر را جذب می‌کنند
                foreach (var edge in edges)
                {
                    var v = nodes.FirstOrDefault(n => n.Id == edge.SourceId);
                    var u = nodes.FirstOrDefault(n => n.Id == edge.TargetId);
                    if (v == null || u == null) continue;

                    var delta = new Vector2(v.X - u.X, v.Y - u.Y);
                    float dist = delta.Length();
                    if (dist < 0.1f) dist = 0.1f;

                    // فرمول جاذبه: Fa = dist^2 / k
                    // ضریب وزن: خطوط ضخیم‌تر (ارتباط بیشتر) جاذبه بیشتری دارند
                    float weightFactor = (float)(1 + edge.CalculatedWeight * 1.5);
                    float force = (dist * dist) / k * weightFactor;

                    var displacement = delta.Normalize() * force;

                    displacements[v.Id] -= displacement;
                    displacements[u.Id] += displacement;
                }

                // ج) اعمال جابجایی
                foreach (var v in nodes)
                {
                    var disp = displacements[v.Id];
                    float dist = disp.Length();

                    // محدود کردن حرکت با دما
                    float limit = Math.Min(dist, t);
                    var move = disp.Normalize() * limit;

                    v.X += move.X;
                    v.Y += move.Y;
                }

                // سرد کردن سیستم
                t *= 0.95f;
            }

            // 3. مرکزگرایی (Centering)
            // انتقال کل گراف به مرکز بوم تا کاربر مجبور به اسکرول نباشد
            ShiftGraphToCenter(nodes, CanvasWidth, CanvasHeight);
        }, cancellationToken);
    }

    private void ShiftGraphToCenter(List<GraphNode> nodes, float width, float height)
    {
        if (!nodes.Any()) return;

        // پیدا کردن محدوده فعلی گراف
        float minX = nodes.Min(n => n.X);
        float maxX = nodes.Max(n => n.X);
        float minY = nodes.Min(n => n.Y);
        float maxY = nodes.Max(n => n.Y);

        // محاسبه مرکز فعلی
        float centerX = (minX + maxX) / 2;
        float centerY = (minY + maxY) / 2;

        // محاسبه مرکز مطلوب
        float desiredX = width / 2;
        float desiredY = height / 2;

        // میزان جابجایی لازم
        float offsetX = desiredX - centerX;
        float offsetY = desiredY - centerY;

        // اعمال جابجایی به همه نودها
        foreach (var n in nodes)
        {
            n.X += offsetX;
            n.Y += offsetY;
        }
    }

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
            // اگر طول صفر بود (دو نود دقیقاً روی هم)، یک جهت تصادفی کوچک برگردان
            if (len < 0.001f) return new Vector2(0.1f, 0.1f);
            return new Vector2(X / len, Y / len);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.X * d, a.Y * d);
    }
}