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


    // لیست فایل‌های انتخاب شده
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

    public RelayCommand AddFileCommand { get; }
    public RelayCommand RemoveFileCommand { get; }
    public RelayCommand AnalyzeCommand { get; }

    public ImportViewModel(IExcelReaderService excelService, MainViewModel mainViewModel)
    {
        _excelService = excelService;
        _mainViewModel = mainViewModel;

        AddFileCommand = new RelayCommand(_ => AddFiles());
        RemoveFileCommand = new RelayCommand(_ => RemoveFile(), _ => SelectedFile != null);
        AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAllAsync(), _ => ImportFiles.Any() && !IsAnalyzing);
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Filter = "Excel Files|*.xlsx;*.xls", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!ImportFiles.Any(f => f.FilePath == file))
                {
                    var wrapper = new ExcelFileWrapper(file);
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

            // تشخیص خودکار ستون‌ها
            wrapper.SelectedSource =
                headers.FirstOrDefault(h => h.ToLower().Contains("source") || h.ToLower().Contains("from"));
            wrapper.SelectedTarget =
                headers.FirstOrDefault(h => h.ToLower().Contains("target") || h.ToLower().Contains("to"));
            wrapper.SelectedDuration =
                headers.FirstOrDefault(h => h.ToLower().Contains("duration") || h.ToLower().Contains("time"));

            wrapper.SelectedDate =
                headers.FirstOrDefault(h => h.ToLower().Contains("date") || h.ToLower().Contains("تاریخ"));
            wrapper.SelectedTime =
                headers.FirstOrDefault(h => h.ToLower().Contains("time") || h.ToLower().Contains("ساعت"));

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
        await _mainViewModel.StartMultiFileGraphProcessingAsync(ImportFiles.ToList());
        IsAnalyzing = false;
    }
}

// کلاس کمکی برای هر فایل در لیست
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

    public ExcelFileWrapper(string path)
    {
        FilePath = path;
    }
}