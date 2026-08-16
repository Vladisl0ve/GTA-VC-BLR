using System.ComponentModel;

namespace GTA_GXT_Editor.Models;

public sealed class UiLanguageOption(string cultureName, string nativeName) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string CultureName { get; } = cultureName;

    public string NativeName { get; } = nativeName;

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
