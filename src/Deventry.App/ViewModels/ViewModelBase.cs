using CommunityToolkit.Mvvm.ComponentModel;

namespace Deventry.App.ViewModels;

public abstract class ViewModelBase : ObservableObject;

/// <summary>A page that shows a message while something runs.</summary>
public sealed class BusyViewModel(string message) : ViewModelBase
{
    public string Message { get; } = message;
}
