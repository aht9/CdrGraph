using System.Collections.ObjectModel;
using System.Windows;
using CdrGraph.Core.Common;
using CdrGraph.Core.Interfaces;
using Microsoft.Win32;

namespace CdrGraph.Desktop.ViewModels;

public class ImportViewModel : ObservableObject
{
    private readonly IExcelReaderService _excelService;
    private readonly MainViewModel _mainViewModel;

    public ObservableCollection<ExcelFileWrapper> ImportFiles { get; } = new();

    private ExcelFileWrapper _selectedFile;

    public ExcelFileWrapper SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                OnPropertyChanged(nameof(IsFileSelected));
                RemoveFileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsFileSelected => SelectedFile != null;

    private bool _isAnalyzing;

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set => SetProperty(ref _isAnalyzing, value);
    }

    // --- ویژگی جدید: محدودیت تعداد نودها ---
    private int _maxNodeLimit = 2000; // مقدار پیش‌فرض

    public int MaxNodeLimit
    {
        get => _maxNodeLimit;
        set => SetProperty(ref _maxNodeLimit, value);
    }
    // ---------------------------------------

    //Common Targets
    // --- حالت جدید تحلیل ---
    private bool _isCommonAnalysisMode;

    public bool IsCommonAnalysisMode
    {
        get => _isCommonAnalysisMode;
        set
        {
            if (SetProperty(ref _isCommonAnalysisMode, value))
            {
                // وقتی حالت عوض می‌شود، باید دکمه آنالیز دوباره بررسی شود
                AnalyzeCommand.RaiseCanExecuteChanged();
            }
        }
    }
    // -----------------------

    public RelayCommand AddFileCommand { get; }
    public RelayCommand RemoveFileCommand { get; }
    public RelayCommand AnalyzeCommand { get; }

    // لیست رنگ‌های پیشنهادی برای انتخاب کاربر
    public List<string> AvailableColors { get; } = new List<string>
    {
        "#FF4500", // OrangeRed
        "#1E90FF", // DodgerBlue
        "#32CD32", // LimeGreen
        "#FFD700", // Gold
        "#9370DB", // MediumPurple
        "#00CED1", // DarkTurquoise
        "#DC143C", // Crimson
        "#FF69B4", // HotPink
        "#8A2BE2", // BlueViolet
        "#00FA9A" // MediumSpringGreen
    };

    public ImportViewModel(IExcelReaderService excelService, MainViewModel mainViewModel)
    {
        _excelService = excelService;
        _mainViewModel = mainViewModel;

        AddFileCommand = new RelayCommand(_ => AddFiles());
        RemoveFileCommand = new RelayCommand(_ => RemoveFile(), _ => SelectedFile != null);
        
        
        // اصلاح شرط اجرا:
        // 1. لیست خالی نباشد.
        // 2. در حال آنالیز نباشد.
        // 3. اگر حالت "مشترکات" انتخاب شده، حتماً باید بیش از 1 فایل باشد.
        AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAllAsync(), _ =>
        {
            if (!ImportFiles.Any() || IsAnalyzing) return false;

            if (IsCommonAnalysisMode && ImportFiles.Count < 2) return false;

            return true;
        });
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Filter = "Excel Files|*.xlsx;*.xls", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            int colorIndex = 0;
            foreach (var file in dialog.FileNames)
            {
                if (!ImportFiles.Any(f => f.FilePath == file))
                {
                    var wrapper = new ExcelFileWrapper(file);

                    // اختصاص رنگ پیش‌فرض چرخشی به فایل
                    if (ImportFiles.Count < AvailableColors.Count)
                        wrapper.SelectedFileColor = AvailableColors[ImportFiles.Count % AvailableColors.Count];

                    ImportFiles.Add(wrapper);
                    _ = LoadHeadersForFile(wrapper);
                }
            }

            if (ImportFiles.Any()) SelectedFile = ImportFiles.Last();
            AnalyzeCommand.RaiseCanExecuteChanged();
        }
    }

    private void RemoveFile()
    {
        if (SelectedFile != null)
        {
            ImportFiles.Remove(SelectedFile);
            AnalyzeCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task LoadHeadersForFile(ExcelFileWrapper wrapper)
    {
        try
        {
            var headers = await _excelService.GetHeadersAsync(wrapper.FilePath);
            wrapper.DetectedHeaders.Clear();
            foreach (var h in headers) wrapper.DetectedHeaders.Add(h);

            wrapper.SelectedSource =
                headers.FirstOrDefault(h => h.ToLower().Contains("source") || h.ToLower().Contains("from"));
            wrapper.SelectedTarget =
                headers.FirstOrDefault(h => h.ToLower().Contains("target") || h.ToLower().Contains("to"));
            wrapper.SelectedDuration =
                headers.FirstOrDefault(h => h.ToLower().Contains("duration") || h.ToLower().Contains("time"));

            wrapper.StatusText = "Ready";
            wrapper.StatusColor = "LightGreen";
        }
        catch
        {
            wrapper.StatusText = "Error";
            wrapper.StatusColor = "Red";
        }
    }

    private async Task AnalyzeAllAsync()
    {
        IsAnalyzing = true;
        AnalyzeCommand.RaiseCanExecuteChanged();
        
        // ارسال MaxNodeLimit به متد پردازش در MainViewModel
        await _mainViewModel.StartMultiFileGraphProcessingAsync(ImportFiles.ToList(), MaxNodeLimit, IsCommonAnalysisMode);
        
        IsAnalyzing = false;
        AnalyzeCommand.RaiseCanExecuteChanged();
    }
}

public class ExcelFileWrapper : ObservableObject
{
    public string FilePath { get; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public ObservableCollection<string> DetectedHeaders { get; } = new();

    private string _selectedSource;

    public string SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }

    private string _selectedTarget;

    public string SelectedTarget
    {
        get => _selectedTarget;
        set => SetProperty(ref _selectedTarget, value);
    }

    private string _selectedDuration;

    public string SelectedDuration
    {
        get => _selectedDuration;
        set => SetProperty(ref _selectedDuration, value);
    }

    // ستون‌های جدید اختیاری
    private string _selectedDate;

    public string SelectedDate
    {
        get => _selectedDate;
        set => SetProperty(ref _selectedDate, value);
    }

    private string _selectedTime;

    public string SelectedTime
    {
        get => _selectedTime;
        set => SetProperty(ref _selectedTime, value);
    }

    private string _statusText = "Pending...";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _statusColor = "Gray";

    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    private string _selectedFileColor = "#1E90FF";

    public string SelectedFileColor
    {
        get => _selectedFileColor;
        set => SetProperty(ref _selectedFileColor, value);
    }

    public ExcelFileWrapper(string path)
    {
        FilePath = path;
    }
}