// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BIBIM_MVP
{
    public class RelayCommand : ICommand
    {
        private readonly Func<object, Task> _executeAsync;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null)
        {
            if (executeAsync == null)
            {
                throw new ArgumentNullException("executeAsync");
            }
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public async void Execute(object parameter)
        {
            try
            {
                await _executeAsync(parameter);
            }
            catch (Exception ex)
            {
                Logger.LogError("RelayCommand", ex);
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
