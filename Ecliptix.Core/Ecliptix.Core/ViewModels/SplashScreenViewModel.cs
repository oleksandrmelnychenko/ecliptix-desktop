namespace Ecliptix.Core.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;


    public class SplashScreenViewModel : INotifyPropertyChanged
    {
        private string _status = "Starting...";

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }