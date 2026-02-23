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
using Serilog;

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
        // 1. تنظیمات اولیه سیستم لاگ (Logging Setup)
        // فایل لاگ در پوشه logs کنار فایل اجرایی ساخته می‌شود و روزانه چرخش می‌کند
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("logs/cdr-log-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // 2. مدیریت خطاهای مدیریت نشده (Global Exception Handling)
        // این بخش خطاهایی که باعث بسته شدن ناگهانی برنامه می‌شوند را می‌گیرد
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log.Fatal(ex, "Application crashed due to a critical system error (AppDomain).");
            MessageBox.Show($"Critical Error: {ex?.Message}\nSee logs for details.", "System Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        // خطاهای مربوط به ترد UI (مثل بایندینگ یا گرافیک)
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Fatal(e.Exception, "Application crashed on UI Thread (Dispatcher).");
            e.Handled = true; // جلوگیری از بسته شدن برنامه (اختیاری)
            MessageBox.Show($"UI Error: {e.Exception.Message}\nSee logs for details.", "UI Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        try 
        {
            Log.Information("==================================================");
            Log.Information($"Application Starting... OS: {Environment.OSVersion}");

            AppHost = Host.CreateDefaultBuilder()
                .UseSerilog() // تزریق Serilog به کانتینر مایکروسافت
                .ConfigureServices((hostContext, services) =>
                {
                    // 1. Infrastructure Services
                    services.AddDbContext<AppDbContext>();
                    services.AddSingleton<IExcelReaderService, ExcelReaderService>();
                    services.AddSingleton<IGraphLayoutService, FruchtermanReingoldLayoutService>();
                    services.AddSingleton<CdrDataService>();

                    // 2. ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<ImportViewModel>(); 

                    // 3. Views
                    services.AddSingleton<MainWindow>();
                })
                .Build();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host builder failed to initialize.");
            MessageBox.Show($"Startup Error: {ex.Message}");
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await AppHost!.StartAsync();

            var startupForm = AppHost.Services.GetRequiredService<MainWindow>();
            startupForm.Show();
            Log.Information("MainWindow displayed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during OnStartup execution.");
            MessageBox.Show($"Launch Error: {ex.Message}");
        }

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Application Exiting normally.");
        if (AppHost != null)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
        }
        // اطمینان از نوشته شدن آخرین لاگ‌ها در فایل
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}