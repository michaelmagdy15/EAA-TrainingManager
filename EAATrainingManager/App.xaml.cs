using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using EAATrainingManager.Services;

namespace EAATrainingManager;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindowInstance { get; private set; }
    private static System.Threading.Mutex? _singleInstanceMutex;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static DatabaseService DatabaseService { get; } = new();
    public static ExcelSyncService ExcelSyncService { get; } = new(DatabaseService);
    public static BackupService BackupService { get; } = new(DatabaseService.GetDatabasePath());
    public static ExcelMirrorService ExcelMirrorService { get; } = new(DatabaseService, ExcelSyncService);
    public static UpdateService UpdateService { get; } = new();

    public App()
    {
        // 1. Single-Instance Guard: Prevent launching duplicate copies
        const string mutexName = "Global\\EAATrainingManager_SingleInstance_Mutex";
        _singleInstanceMutex = new System.Threading.Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            // Another instance is already running - bring it to focus and exit
            BringExistingInstanceToFront();
            Environment.Exit(0);
            return;
        }

        // Egyptian Civil Aviation Thread Culture (Western Arabic Numerals 1, 2, 3)
        var culture = new CultureInfo("ar-EG");
        culture.NumberFormat.DigitSubstitution = DigitShapes.None; // Enforces 1, 2, 3 instead of ١, ٢, ٣
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Register global safety handlers
        UnhandledException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[WinUI UnhandledException] {e.Message} \n {e.Exception}");
            e.Handled = true; // Prevents 0xc000027b fail-fast crash
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[AppDomain UnhandledException] {e.ExceptionObject}");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[TaskScheduler UnobservedTaskException] {e.Exception}");
            e.SetObserved();
        };

        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        EnsureMainWindow();
    }

    private void EnsureMainWindow()
    {
        if (_window != null) return;
        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
    }

    private static void BringExistingInstanceToFront()
    {
        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
            foreach (var p in processes)
            {
                if (p.Id != currentProcess.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE = 9
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
        }
        catch { }
    }
}
