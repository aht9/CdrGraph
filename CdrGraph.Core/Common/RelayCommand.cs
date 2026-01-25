using System;
using System.Windows.Input;

namespace CdrGraph.Core.Common;

public class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Predicate<object> _canExecute;

    // رویداد استاندارد ICommand
    public event EventHandler CanExecuteChanged;

    public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter)
    {
        return _canExecute == null || _canExecute(parameter);
    }

    public void Execute(object parameter)
    {
        _execute(parameter);
    }

    // *** متد اضافه شده برای رفع خطای ImportViewModel ***
    // این متد رویداد CanExecuteChanged را دستی صدا می‌زند تا UI متوجه تغییر وضعیت دکمه شود
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}