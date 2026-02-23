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
        // --- طیف قرمز و صورتی ---
        "#F44336", // Red
        "#E53935", // Red Dark
        "#D32F2F", // Red Darker
        "#E91E63", // Pink
        "#D81B60", // Pink Dark
        "#FF1493", // Deep Pink
        "#C2185B", // Pink Darker

        // --- طیف بنفش (بدون BlueViolet سیستمی) ---
        "#9C27B0", // Purple
        "#8E24AA", // Purple Dark
        "#7B1FA2", // Purple Darker
        "#673AB7", // Deep Purple
        "#5E35B1", // Deep Purple Dark
        "#4527A0", // Deep Purple Darker

        // --- طیف آبی و فیروزه‌ای ---
        "#3F51B5", // Indigo
        "#3949AB", // Indigo Dark
        "#283593", // Indigo Darker
        "#2196F3", // Blue
        "#1E88E5", // Blue Dark
        "#1565C0", // Blue Darker
        "#03A9F4", // Light Blue
        "#00BCD4", // Cyan
        "#00ACC1", // Cyan Dark
        "#0097A7", // Cyan Darker
        "#00CED1", // Dark Turquoise

        // --- طیف سبز ---
        "#009688", // Teal
        "#00897B", // Teal Dark
        "#00695C", // Teal Darker
        "#4CAF50", // Green
        "#43A047", // Green Dark
        "#2E7D32", // Green Darker
        "#8BC34A", // Light Green
        "#7CB342", // Light Green Dark
        "#32CD32", // Lime Green
        "#00FA9A", // Medium Spring Green

        // --- طیف زرد، کهربایی و نارنجی (بدون تداخل با رنگ انتخاب UI) ---
        "#FFEB3B", // Yellow (نرم‌تر)
        "#FDD835", // Yellow Dark
        "#FFC107", // Amber
        "#FFB300", // Amber Dark
        "#FB8C00", // Orange Soft
        "#FF5722", // Deep Orange
        "#F4511E", // Deep Orange Dark
        "#D84315", // Deep Orange Darker

        // --- طیف قهوه‌ای و خاکستری متمایل به آبی ---
        "#795548", // Brown
        "#6D4C41", // Brown Dark
        "#5D4037", // Brown Darker
        "#607D8B", // Blue Grey
        "#546E7A", // Blue Grey Dark
        "#455A64" // Blue Grey Darker
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
        AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAllAsync(), _ => CanAnalyze());
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
                    // --- بخش جدید: گوش دادن به تغییرات ستون‌ها برای فعال/غیرفعال کردن آنی دکمه ---
                    wrapper.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(ExcelFileWrapper.SelectedSource) ||
                            e.PropertyName == nameof(ExcelFileWrapper.SelectedTarget))
                        {
                            AnalyzeCommand.RaiseCanExecuteChanged();
                        }
                    };
                    // --------------------------------------------------------------------------------
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
            /*wrapper.SelectedDuration =
                headers.FirstOrDefault(h => h.ToLower().Contains("duration") || h.ToLower().Contains("time"));*/

            wrapper.SelectedDuration =
                headers.FirstOrDefault(h => h.ToLower().Contains("duration") || h.ToLower().Contains("time") || h.ToLower().Contains("weight"));
            
            wrapper.StatusText = "Ready";
            wrapper.StatusColor = "LightGreen";
        }
        catch
        {
            wrapper.StatusText = "Error";
            wrapper.StatusColor = "Red";
        }
        finally
        {
            // --- این خط اضافه شود ---
            // آپدیت وضعیت دکمه وقتی ستون‌ها به طور خودکار پیدا شدند
            Application.Current.Dispatcher.Invoke(() => AnalyzeCommand.RaiseCanExecuteChanged());
        }
    }

    private async Task AnalyzeAllAsync()
    {
        IsAnalyzing = true;
        AnalyzeCommand.RaiseCanExecuteChanged();

        // ارسال MaxNodeLimit به متد پردازش در MainViewModel
        await _mainViewModel.StartMultiFileGraphProcessingAsync(ImportFiles.ToList(), MaxNodeLimit,
            IsCommonAnalysisMode);

        IsAnalyzing = false;
        AnalyzeCommand.RaiseCanExecuteChanged();
    }

    private bool CanAnalyze()
    {
        // اگر فایلی اضافه نشده یا در حال آنالیز است، دکمه غیرفعال باشد
        if (!ImportFiles.Any() || IsAnalyzing) return false;

        if (IsCommonAnalysisMode && ImportFiles.Count < 2) return false;

        // بررسی تک‌تک فایل‌های وارد شده
        foreach (var file in ImportFiles)
        {
            // اگر حتی یکی از فایل‌ها ستون مبدا یا مقصدش انتخاب نشده باشد، دکمه غیرفعال می‌ماند
            if (string.IsNullOrWhiteSpace(file.SelectedSource) ||
                string.IsNullOrWhiteSpace(file.SelectedTarget))
            {
                return false;
            }
        }

        return true;
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