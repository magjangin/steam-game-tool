using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace steam_game_tool;

/// <summary>긴 안내문과 확인·닫기 버튼만 있는 대화 상자. Avalonia 에는 기본 메시지 상자가 없다.</summary>
public sealed class TextDialog : Window
{
    private TextDialog(string title, string message, string? confirmText, string closeText)
    {
        Title = title;
        Width = 620;
        Height = 520;
        MinWidth = 400;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        if (confirmText is not null)
        {
            var confirm = new Button { Content = confirmText };
            confirm.Click += (_, _) => Close(true);
            buttons.Children.Add(confirm);
        }
        var close = new Button { Content = closeText, IsCancel = true };
        close.Click += (_, _) => Close(false);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttons,
                new ScrollViewer
                {
                    Content = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
    }

    /// <summary>확인 버튼을 누르면 true. 닫기·Esc·창 닫기는 false.</summary>
    public static Task<bool> ShowAsync(Window owner, string title, string message,
        string? confirmText = null, string closeText = "닫기") =>
        new TextDialog(title, message, confirmText, closeText).ShowDialog<bool>(owner);
}
