using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FunderFinder.Mac;

/// <summary>
/// A yes/no confirmation in the app's own style. Cancel is the safe default: it has focus,
/// and Esc or closing the dialog counts as "no".
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();

    private ConfirmDialog(string title, string message, string confirmText, bool destructive) : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        // Brick red for things that remove or reset (see Button.action.destructive.filled in Styles.axaml).
        if (destructive) ConfirmButton.Classes.AddRange(new[] { "destructive", "filled" });
        Opened += (_, _) => CancelButton.Focus();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>; true if you confirmed.</summary>
    public static Task<bool> AskAsync(Window owner, string title, string message, string confirmText, bool destructive = false)
        => new ConfirmDialog(title, message, confirmText, destructive).ShowDialog<bool>(owner);

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Banner_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);
}
