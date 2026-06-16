using System.Windows;
using System.Windows.Input;
using x.ai_client_PC.Models;
using x.ai_client_PC.ViewModels;

namespace x.ai_client_PC;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            try
            {
                await viewModel.InitializeAsync();
                ApiKeyBox.PasswordChanged += (_, _) => viewModel.ApiKeyInput = ApiKeyBox.Password;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Initialization error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            if (DataContext is MainViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Focus handled via binding search; could focus search box here
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || DataContext is not MainViewModel vm)
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        await vm.AddDroppedFilesCommand.ExecuteAsync(files);
    }

    private async void ChatItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatSession chat } && DataContext is MainViewModel vm)
        {
            await vm.SelectChatCommand.ExecuteAsync(chat);
        }
    }
}