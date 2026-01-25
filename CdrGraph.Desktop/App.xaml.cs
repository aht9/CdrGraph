using System.Configuration;
using System.Data;
using System.Windows;
using CdrGraph.Core.Interfaces;
using CdrGraph.Desktop.ViewModels;
using CdrGraph.Desktop.Views;
using CdrGraph.Infrastructure.Data;
using CdrGraph.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CdrGraph.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // نگهداری Host برای مدیریت چرخه حیات سرویس‌ها
    public static IHost? AppHost { get; private set; }

    public App()
    {
        // ساختن و پیکربندی Host (کانتینر DI)
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                // 1. ثبت سرویس‌های لایه Infrastructure
                services.AddDbContext<AppDbContext>();
                services.AddSingleton<IExcelReaderService, ExcelReaderService>();
                services.AddSingleton<IGraphLayoutService, FruchtermanReingoldLayoutService>();
                services.AddSingleton<CdrDataService>(); 
                // 2. ثبت ViewModel ها
                // MainViewModel وضعیت کلی برنامه را نگه می‌دارد پس Singleton است
                services.AddSingleton<MainViewModel>();
                // ImportViewModel می‌تواند هر بار جدید ساخته شود
                services.AddTransient<ImportViewModel>();
                // GraphViewModel معمولاً داخل MainViewModel ساخته می‌شود، اما اگر نیاز به تزریق داشت می‌توان اینجا اضافه کرد

                // 3. ثبت پنجره اصلی (تا بتواند ViewModel را در سازنده خود دریافت کند)
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // شروع Host
        await AppHost!.StartAsync();

        // دریافت پنجره اصلی از DI Container
        // این کار باعث می‌شود MainViewModel به طور خودکار در سازنده MainWindow تزریق شود
        var startupForm = AppHost.Services.GetRequiredService<MainWindow>();

        // نمایش پنجره
        startupForm.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // توقف تمیز سرویس‌ها هنگام بستن برنامه
        if (AppHost != null)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
        }

        base.OnExit(e);
    }
}