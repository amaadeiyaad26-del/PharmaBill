using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public partial class SectionPageViewModel(string title) : ObservableObject
{
    [ObservableProperty]
    private string _title = title;
}
