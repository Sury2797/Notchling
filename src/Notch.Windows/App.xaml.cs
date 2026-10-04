using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Windows.Services;
using System.Runtime.InteropServices;

namespace Notch.Windows;

public partial class App : Application
{
    private MainWindow? _window;
    private Mutex? _instance;
    private Mutex? _installerMutex;
    public App()
    {
        // Register before loading XAML so resource and constructor failures leave a local diagnostic.
        UnhandledException += (_, args) => StartupDiagnostics.Write("Application.UnhandledException", args.Exception, args.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            StartupDiagnostics.Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        StartStartupResourceTracing();
        try
        {
            InitializeComponent();
        }
        catch (Exception error)
        {
            StartupDiagnostics.Write("App.InitializeComponent", error);
            StopStartupResourceTracing();
            throw;
        }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _instance = new Mutex(true, "Local\\Notch.Desktop." + Environment.UserName, out var ownsInstance);
            if (!ownsInstance)
            {
                var existing = FindWindow(null, ProductIdentity.WindowTitle);
                // An earlier version can still own the stable single-instance mutex during an upgrade.
                if (existing == 0) existing = FindWindow(null, "Notchling — Desktop companion");
                if (existing == 0) existing = FindWindow(null, "Notch — Desktop companion");
                if (existing != 0) { ShowWindow(existing, 4); PostMessage(existing, 0x8001, 0, 0); }
                Exit(); return;
            }
            _installerMutex = new Mutex(false, "Notch.Desktop.Running");
            _window = new MainWindow();
            _window.Activate();
            _window.Start();
        }
        catch (Exception error)
        {
            StartupDiagnostics.Write("App.OnLaunched", error);
            throw;
        }
        finally
        {
            StopStartupResourceTracing();
        }
    }
    private void StartStartupResourceTracing()
    {
        try
        {
            DebugSettings.XamlResourceReferenceFailed += OnStartupResourceReferenceFailed;
            DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        }
        catch (Exception error)
        {
            StartupDiagnostics.Write("App.StartupResourceTracing", error);
        }
    }
    private void StopStartupResourceTracing()
    {
        try
        {
            DebugSettings.IsXamlResourceReferenceTracingEnabled = false;
            DebugSettings.XamlResourceReferenceFailed -= OnStartupResourceReferenceFailed;
        }
        catch (Exception error)
        {
            StartupDiagnostics.Write("App.StopStartupResourceTracing", error);
        }
    }
    private void OnStartupResourceReferenceFailed(DebugSettings sender, XamlResourceReferenceFailedEventArgs args) =>
        StartupDiagnostics.Write("XAML resource reference", null, args.Message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
