using Cinder.MINT.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Cinder.MINT.Controls;

public static class MintyControlDeckHost
{
    public static void Attach(MainWindow window)
    {
        if (window.FindName("RootGrid") is not Grid root) return;
        if (window.DataContext is not MainViewModel main) return;

        var deck = new MintyControlDeck();
        deck.Attach(main);
        Grid.SetRow(deck, 1);
        Grid.SetRowSpan(deck, 3);
        Panel.SetZIndex(deck, 1000);
        root.Children.Add(deck);

        var returnButton = new Button
        {
            Content = "← CONTROL DECK",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 18, 0),
            Padding = new Thickness(13, 7, 13, 7),
            Background = new SolidColorBrush(Color.FromRgb(18, 29, 37)),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 255, 214)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(167, 107, 255)),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            ToolTip = "Return from MintyBay to the MintyFilter control deck"
        };
        Grid.SetRow(returnButton, 1);
        Panel.SetZIndex(returnButton, 1200);
        root.Children.Add(returnButton);

        deck.OpenMintyBayRequested += (_, _) =>
        {
            deck.Visibility = Visibility.Collapsed;
            returnButton.Visibility = Visibility.Visible;
        };

        returnButton.Click += (_, _) =>
        {
            returnButton.Visibility = Visibility.Collapsed;
            deck.Visibility = Visibility.Visible;
        };
    }
}
