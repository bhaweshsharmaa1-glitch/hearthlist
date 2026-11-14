using System.Windows;
using System.Windows.Input;

namespace Hearthlist.Desktop;

public partial class EditTaskDialog : Window
{
    public string TaskTitle { get; private set; } = string.Empty;
    public string TaskDescription { get; private set; } = string.Empty;

    public EditTaskDialog(string title, string description)
    {
        InitializeComponent();
        TitleInput.Text = title;
        DescInput.Text = description;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TaskTitle = TitleInput.Text.Trim();
        TaskDescription = DescInput.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
