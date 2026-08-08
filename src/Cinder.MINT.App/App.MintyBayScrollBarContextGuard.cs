using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Cinder.MINT;

public partial class App
{
    static App()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollBar),
            ContextMenuService.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(SuppressNativeScrollBarContextMenu),
            handledEventsToo: true);
    }

    private static void SuppressNativeScrollBarContextMenu(object sender, ContextMenuEventArgs e)
    {
        // WPF injects a bright built-in ScrollBar menu containing commands such as
        // "Scroll Here", "Page Left", etc. MintyBay owns all right-click interaction,
        // so native scrollbar context menus are intentionally disabled app-wide.
        e.Handled = true;
    }
}
