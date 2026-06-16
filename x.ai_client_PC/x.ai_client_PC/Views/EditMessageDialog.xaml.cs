using System.Windows;

namespace x.ai_client_PC.Views;

public partial class EditMessageDialog : Window
{
    public string EditedText { get; private set; }

    public EditMessageDialog(string initialText)
    {
        InitializeComponent();
        EditBox.Text = initialText;
        EditedText = initialText;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        EditedText = EditBox.Text;
        DialogResult = true;
    }
}