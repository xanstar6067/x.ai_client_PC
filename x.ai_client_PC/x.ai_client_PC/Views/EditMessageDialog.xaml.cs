using System.Windows;
using x.ai_client_PC.Services;

namespace x.ai_client_PC.Views;

public partial class EditMessageDialog : Window
{
    public string EditedText { get; private set; }

    public EditMessageDialog(string initialText, LocalizationService loc)
    {
        InitializeComponent();
        Title = loc["EditMessageTitle"];
        CancelButton.Content = loc["Cancel"];
        SaveButton.Content = loc["Save"];
        EditBox.Text = initialText;
        EditedText = initialText;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        EditedText = EditBox.Text;
        DialogResult = true;
    }
}
