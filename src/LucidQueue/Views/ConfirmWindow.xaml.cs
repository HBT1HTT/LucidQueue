using System.Windows;
using System.Windows.Input;
using LucidQueue.Common;

namespace LucidQueue.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirmText, string cancelText, bool danger)
    {
        InitializeComponent();
        WindowCorners.Apply(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;

        if (danger)
        {
            ConfirmButton.Style = (Style)FindResource("DangerButton");
            AccentBar.Background = (System.Windows.Media.Brush)FindResource("Danger");
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 忽略。
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
