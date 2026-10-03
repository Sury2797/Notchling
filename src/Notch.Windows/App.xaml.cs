using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Notch.Windows;

public partial class App : Application
{
    private MainWindow? _window;
    private Mutex? _instance;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new Mutex(true, "Local\\Notch.Desktop." + Environment.UserName, out var ownsInstance);
        if (!ownsInstance)
        {
            var existing = FindWindow(null, "Notch — Desktop companion");
            if (existing != 0) { ShowWindow(existing, 4); PostMessage(existing, 0x8001, 0, 0); }
            Exit(); return;
        }
        _window = new MainWindow();
        _window.Activate();
        _window.Start();
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
