using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using EAATrainingManager.Services;

namespace EAATrainingManager;

public partial class App : Application
{
    private Window? _window;

    public static DatabaseService DatabaseService { get; } = new();
    public static ExcelSyncService ExcelSyncService { get; } = new(DatabaseService);
    public static BackupService BackupService { get; } = new(DatabaseService.GetDatabasePath());
    public static ExcelMirrorService ExcelMirrorService { get; } = new(DatabaseService, ExcelSyncService);
    public static UpdateService UpdateService { get; } = new();

    public App()
    {
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
        EnsureMainWindow();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        EnsureMainWindow();
    }

    private void EnsureMainWindow()
    {
        if (_window != null) return;
        _window = new MainWindow();
        _window.Activate();
    }
}
