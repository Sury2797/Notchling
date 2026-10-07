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
        UnhandledException += (_, args) => StartupDiagnostics.ReportFatal("Application.UnhandledException", args.Exception, args.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error) StartupDiagnostics.ReportFatal("AppDomain.UnhandledException", error);
            else StartupDiagnostics.Write("AppDomain.UnhandledException", null);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => StartupDiagnostics.Write("TaskScheduler.UnobservedTaskException", args.Exception);
        StartupDiagnostics.BeginSession();
        StartStartupResourceTracing();
        try
        {
            InitializeComponent();
        }
        catch (Exception error)
        {
            StartupDiagnostics.ReportFatal("App.InitializeComponent", error);
            StopStartupResourceTracing();
            throw;
        }
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _instance = new Mutex(true, "Local\\Notch.Desktop." + Environment.UserName, out var ownsInstance);
            if (!ownsInstance)
            {
                // A second launch can arrive while the first process is still creating its HWND.
                // Wait without blocking WinUI rather than silently exiting before it can be reopened.
                nint existing = 0;
                for (var attempt = 0; attempt < 40 && existing == 0; attempt++)
                {
                    existing = FindWindow(null, ProductIdentity.WindowTitle);
                    // Older versions retain the stable mutex during an upgrade.
                    if (existing == 0) existing = FindWindow(null, "Notchling — Desktop companion");
                    if (existing == 0) existing = FindWindow(null, "Notch — Desktop companion");
                    if (existing == 0) await Task.Delay(50);
                }
                if (existing == 0 || !PostMessage(existing, 0x8001, 0, 0))
                    throw new InvalidOperationException("Notchling is already running but its window could not be reopened. Close Notchling in Task Manager, then launch it again.");
                Exit(); return;
            }
            _installerMutex = new Mutex(false, "Notch.Desktop.Running");
            _window = new MainWindow();
            _window.Start();
        }
        catch (Exception error)
        {
            StartupDiagnostics.ReportFatal("App.OnLaunched", error);
            Exit();
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
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
