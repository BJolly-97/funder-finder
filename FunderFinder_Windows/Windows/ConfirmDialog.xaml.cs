using System.Windows;
using System.Windows.Input;

namespace FunderFinder.Windows;

/// <summary>
/// A yes/no confirmation in the app's own style, replacing the stock grey MessageBox.
/// Cancel is the safe default: Esc or closing it counts as "no", and Enter only
/// confirms once you've moved focus to the confirm button.
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmText, bool destructive)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (destructive)
            ConfirmButton.Background = (System.Windows.Media.Brush)FindResource("UrgentBrush");

        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>; true if you confirmed.</summary>
    /// <param name="destructive">Colours the confirm button brick red, for things that remove or reset.</param>
    public static bool Ask(Window owner, string title, string message, string confirmText, bool destructive = false)
        => new ConfirmDialog(title, message, confirmText, destructive) { Owner = owner }.ShowDialog() == true;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void Banner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
