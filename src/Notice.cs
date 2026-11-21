using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal partial class TransferWindow
{
    bool ShowNotice(string heading, string message, string? action = null)
    {
        var dialog = new Window
        {
            Owner = this, Title = heading, Icon = Icon, Width = 470,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(25, 38, 48)), Foreground = Brushes.White,
            FontFamily = FontFamily, FontSize = FontSize
        };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var style = (Style)FindResource(typeof(Button));
        if (action is not null)
            actions.Children.Add(new Button { Content = "Vazgeç", IsCancel = true, Style = style, Margin = new Thickness(0, 0, 8, 0) });
        var confirm = new Button { Content = action ?? "Tamam", IsDefault = true, Style = style };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(confirm);
        content.Children.Add(actions);
        dialog.Content = content;
        return dialog.ShowDialog() == true;
    }
}
