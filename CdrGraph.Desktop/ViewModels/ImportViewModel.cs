using System.Collections.ObjectModel;
using System.Windows;
using CdrGraph.Core.Common;
using CdrGraph.Core.Interfaces;
using Microsoft.Win32;

namespace CdrGraph.Desktop.ViewModels;

public class ImportViewModel : ObservableObject
{
    private readonly IExcelReaderService _excelService;
    private readonly MainViewModel _mainViewModel; // برای تغییر صفحه

    // وضعیت UI
    private string _filePath;
    private bool _isAnalyzing;

    // داده‌های بایندینگ برای کامبوباکس‌ها
    public ObservableCollection<string> DetectedHeaders { get; } = new();

    private string _selectedSource;
    private string _selectedTarget;
    private string _selectedDuration;

    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set => SetProperty(ref _isAnalyzing, value);
    }

    public string SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }

    public string SelectedTarget
    {
        get => _selectedTarget;
        set => SetProperty(ref _selectedTarget, value);
    }

    public string SelectedDuration
    {
        get => _selectedDuration;
        set => SetProperty(ref _selectedDuration, value);
    }

    // دستورات (Commands)
    public RelayCommand BrowseCommand { get; }
    public RelayCommand AnalyzeCommand { get; }

    // Constructor Injection
    public ImportViewModel(IExcelReaderService excelService, MainViewModel mainViewModel)
    {
        _excelService = excelService;
        _mainViewModel = mainViewModel;

        BrowseCommand = new RelayCommand(_ => BrowseFile());
        AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAsync(), _ => CanAnalyze());
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog { Filter = "Excel Files|*.xlsx;*.xls" };
        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
            _ = LoadHeadersAsync(); // Fire and forget (safe in UI context)
        }
    }

    private async Task LoadHeadersAsync()
    {
        try
        {
            var headers = await _excelService.GetHeadersAsync(FilePath);
            DetectedHeaders.Clear();
            foreach (var h in headers) DetectedHeaders.Add(h);

            // تلاش برای تشخیص هوشمند ستون‌ها
            SelectedSource = headers.FirstOrDefault(h =>
                h.ToLower().Contains("source") || h.ToLower().Contains("origin") || h.ToLower().Contains("from"));
            SelectedTarget = headers.FirstOrDefault(h =>
                h.ToLower().Contains("target") || h.ToLower().Contains("destination") || h.ToLower().Contains("to"));
            SelectedDuration = headers.FirstOrDefault(h =>
                h.ToLower().Contains("duration") || h.ToLower().Contains("sec") || h.ToLower().Contains("time"));
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Error reading file: {ex.Message}");
        }
    }

    private bool CanAnalyze()
    {
        return !string.IsNullOrEmpty(SelectedSource) &&
               !string.IsNullOrEmpty(SelectedTarget) &&
               !IsAnalyzing;
    }

    private async Task AnalyzeAsync()
    {
        {
            IsAnalyzing = true;

            // ایجاد Mapping Profile
            var mapping = new ColumnMapping
            {
                SourceColumn = SelectedSource,
                TargetColumn = SelectedTarget,
                DurationColumn = SelectedDuration
            };

            // ارسال اطلاعات به MainViewModel برای شروع پردازش و تغییر صفحه به گراف
            // نکته: منطق پردازش در MainViewModel مدیریت می‌شود تا بین صفحات به اشتراک گذاشته شود
            await _mainViewModel.StartGraphProcessingAsync(FilePath, mapping);

            IsAnalyzing = false;
        }
    }
}