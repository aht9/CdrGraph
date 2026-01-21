using Microsoft.EntityFrameworkCore;

namespace CdrGraph.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<ProjectMetadata> Projects { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // دیتابیس در کنار فایل اجرایی ساخته می‌شود
        optionsBuilder.UseSqlite("Data Source=cdrgraph.db");
    }
}

public class ProjectMetadata
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string FilePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // تنظیمات جیسون شده برای مپینگ ستون‌ها
    public string ColumnMappingJson { get; set; }
}