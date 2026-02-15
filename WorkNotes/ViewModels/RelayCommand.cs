using System;
using System.Windows.Input;

namespace WorkNotes.ViewModels
{
    /// <summary>
    /// Minimal ICommand implementation for MVVM command binding.
    /// Used by new features (view modes, tab management) while existing
    /// code-behind RoutedCommand handlers remain untouched.
    /// 
    /// Why Hybrid: We're avoiding breaking P0 editor behaviors (context menus,
    /// selection, spell check, Ctrl+Click) while building a maintainable
    /// structure for new features.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
