using Cinder.MINT.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cinder.MINT.Controls;

public sealed class ConfirmPatchRemovalDialog : Window
{
    public ConfirmPatchRemovalDialog(Window owner, string description)
    {
        Owner = owner;
        Title = "Remove from MintyBay";
        Width = 460;
        Height = 230;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(7, 14, 18));
        Foreground = Brushes.White;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "REMOVE PATCH ITEM?",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 95, 170)),
            FontSize = 15,
            FontWeight = FontWeights.Black
        };
        root.Children.Add(title);

        var message = new TextBlock
        {
            Text = $"{description}\n\nThis changes the MintyBay graph. Esc and Enter both default to KEEP.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 225, 225)),
            Margin = new Thickness(0, 14, 0, 12),
            FontSize = 12
        };
        Grid.SetRow(message, 1);
        root.Children.Add(message);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var remove = new Button
        {
            Content = "REMOVE",
            MinWidth = 105,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(78, 22, 52)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 95, 170))
        };
        remove.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var keep = new Button
        {
            Content = "KEEP",
            MinWidth = 105,
            Padding = new Thickness(14, 8, 14, 8),
            IsDefault = true,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(13, 37, 37)),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 255, 214)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(125, 255, 214))
        };

        buttons.Children.Add(remove);
        buttons.Children.Add(keep);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }
}

public sealed class PatchbayHotkeyDialog : Window
{
    private readonly HotkeyCaptureBox _addNode;
    private readonly HotkeyCaptureBox _removeHovered;
    private readonly HotkeyCaptureBox _openControls;
    private readonly HotkeyCaptureBox _toggleBypass;
    private readonly TextBlock _validation;

    public PatchbayHotkeySettings Settings { get; private set; }

    public PatchbayHotkeyDialog(Window owner, PatchbayHotkeySettings settings)
    {
        Owner = owner;
        Settings = settings.Clone();
        Title = "MintyFilter MintyBay Shortcuts";
        Width = 520;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(7, 14, 18));
        Foreground = Brushes.White;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "MINTYBAY SHORTCUTS",
            Foreground = new SolidColorBrush(Color.FromRgb(125, 255, 214)),
            FontSize = 16,
            FontWeight = FontWeights.Black
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Click a field, then press the key or key combination you want. Escape closes without saving.",
            Foreground = new SolidColorBrush(Color.FromRgb(143, 167, 170)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 16)
        });
        root.Children.Add(heading);

        var form = new Grid { Margin = new Thickness(0, 6, 0, 8) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

        _addNode = AddRow(form, 0, "Add node at cursor", Settings.AddNode);
        _removeHovered = AddRow(form, 1, "Remove hovered node / cable", Settings.RemoveHovered);
        _openControls = AddRow(form, 2, "Open controls for hovered / selected node", Settings.OpenControls);
        _toggleBypass = AddRow(form, 3, "Toggle bypass for hovered / selected node", Settings.ToggleBypass);

        Grid.SetRow(form, 1);
        root.Children.Add(form);

        _validation = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 211, 107)),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 28,
            Margin = new Thickness(0, 4, 0, 8)
        };
        Grid.SetRow(_validation, 2);
        root.Children.Add(_validation);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var save = new Button
        {
            Content = "SAVE SHORTCUTS",
            MinWidth = 140,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(13, 44, 40)),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 255, 214)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(125, 255, 214))
        };
        save.Click += (_, _) => SaveAndClose();

        var cancel = new Button
        {
            Content = "CANCEL",
            MinWidth = 100,
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };

        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
    }

    private HotkeyCaptureBox AddRow(Grid form, int row, string label, string value)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(222, 239, 237)),
            Margin = new Thickness(0, 0, 14, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        form.Children.Add(text);

        var box = new HotkeyCaptureBox
        {
            Text = value,
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(11, 24, 29)),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 255, 214)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 94, 96)),
            FontWeight = FontWeights.Bold,
            ToolTip = "Click, then press a key combination."
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        form.Children.Add(box);
        return box;
    }

    private void SaveAndClose()
    {
        string[] values =
        [
            _addNode.Text.Trim(),
            _removeHovered.Text.Trim(),
            _openControls.Text.Trim(),
            _toggleBypass.Text.Trim()
        ];

        if (values.Any(value => !PatchbayGesture.TryParse(value, out _)))
        {
            _validation.Text = "One or more shortcuts are invalid. Escape is reserved for cancelling an in-progress patch action.";
            return;
        }

        string[] normalized =
        [
            PatchbayGesture.Normalize(values[0], "N"),
            PatchbayGesture.Normalize(values[1], "R"),
            PatchbayGesture.Normalize(values[2], "Enter"),
            PatchbayGesture.Normalize(values[3], "B")
        ];

        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            _validation.Text = "Each action needs a different shortcut.";
            return;
        }

        Settings = new PatchbayHotkeySettings
        {
            AddNode = normalized[0],
            RemoveHovered = normalized[1],
            OpenControls = normalized[2],
            ToggleBypass = normalized[3]
        };

        DialogResult = true;
        Close();
    }

    private sealed class HotkeyCaptureBox : TextBox
    {
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                Window.GetWindow(this)?.Close();
                e.Handled = true;
                return;
            }

            if (PatchbayGesture.IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            Text = PatchbayGesture.Format(key, Keyboard.Modifiers);
            SelectAll();
            e.Handled = true;
        }
    }
}

