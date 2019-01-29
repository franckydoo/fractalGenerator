﻿using System;
using System.Windows.Input;

namespace AvaloniaFractalGenerator {
    internal class DelegateCommand : ICommand {
        private readonly Action _execute;

        public DelegateCommand(Action execute) {
            _execute = execute;
        }
        public bool CanExecute(object parameter) {
            return true;
        }
        public void Execute(object parameter) {
            _execute();
        }
        public event EventHandler CanExecuteChanged;
    }
}
