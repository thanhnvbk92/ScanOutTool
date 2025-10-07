using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Configuration;
using System.Diagnostics;
using System.Windows;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ScanOutTool.Services;
using ScanOutLogLib.Helpers;
using ScanOutLogLib.Interfaces;
using ScanOutLogLib.Services;
using ScanOutTool.Views;
using ScanOutTool.Services.Orchestration;
using ScanOutTool.Services.Logging;


namespace ScanOutTool
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private static EventWaitHandle _event;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static IServiceProvider Services { get; private set; }
        public static IConfiguration Configuration { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            string MutexName = Process.GetCurrentProcess().ProcessName +"_Mutex";
            string EventName = Process.GetCurrentProcess().ProcessName + "_Event";
            bool isNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out isNew);
            }
            catch (AbandonedMutexException)
            {
                // Một tiến trình đã crash mà không release mutex → vẫn cho chạy tiếp
                _mutex = new Mutex(true, MutexName);
                isNew = true; // Treat as new instance
            }

            if (!isNew)
            {
                // Gửi tín hiệu yêu cầu hiển thị lại
                try
                {
                    EventWaitHandle.OpenExisting(EventName).Set();
                }
                catch { }

                Shutdown();
                return;
            }

            // Tạo sự kiện chờ yêu cầu hiển thị lại
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            Task.Run(() =>
            {
                while (true)
                {
                    _event.WaitOne(); // chờ tín hiệu
                    Dispatcher.Invoke(() =>
                    {
                        var mw = Current.MainWindow;
                        if (mw != null)
                        {
                            mw.Show();
                            mw.WindowState = WindowState.Normal;
                            mw.Activate();
                        }
                    });
                }
            });

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            // ✅ IMPROVED: Better exception handling
            SetupGlobalExceptionHandling();
            
            // Tạo MainWindow từ DI
            var mainWindow = Services.GetRequiredService<Views.MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string exeDirectory = Path.GetDirectoryName(exePath);
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(Path.Combine(exeDirectory, "Resources/appsettings.json"), optional: true, reloadOnChange: true)
                .Build();

            Configuration = config;
            services.AddSingleton<IConfiguration>(config);

            // Logging
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Logging Services
            services.AddSingleton<ILoggingService, LoggingService>();
            
            // ✅ NEW: Register custom logger provider to bridge ILogger with ILoggingService
            services.AddLogging(builder =>
            {
                builder.ClearProviders(); // Remove default providers
                builder.Services.AddSingleton<ILoggerProvider>(serviceProvider => 
                    new LoggingServiceProvider(serviceProvider.GetRequiredService<ILoggingService>()));
            });

            // Business Services
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IBlockRFService, BlockRFService>();
            services.AddSingleton<IPLCServiceFactory, PLCServiceFactory>();
            services.AddSingleton<IHMESService, HMESService>(); // ✅ DataExecuter-based HMES service
            
            // ✅ NEW: Workflow orchestration services
            services.AddTransient<SerialDataProcessor>();
            services.AddTransient<ScannerFeedbackService>();
            
            services.AddSingleton<IScanWorkflowService, ScanWorkflowService>();

            // Infrastructure Services
            services.AddSingleton<Services.INavigationService, Services.NavigationService>();
            services.AddSingleton<Services.ILoggingService, Services.LoggingService>();
            services.AddSingleton<Services.IUpdateService, Services.UpdateService>();
            services.AddSingleton<Services.IConfigService, Services.ConfigService>();
            services.AddSingleton<Models.IAppState, Models.AppState>();
            services.AddSingleton<IPLCServiceFactory, PLCServiceFactory>();
            services.AddSingleton<IBlockRFService, BlockRFService>();

            // ScanOut related services
            services.AddSingleton<IScanResultDispatcher, ScanResultDispatcher>();
            services.AddSingleton<IScanResultAwaiter, ScanResultAwaiter>();
            services.AddSingleton<IScanResultService, ScanResultService>();

            // ViewModels (now much simpler)
            services.AddTransient<ViewModels.MainViewModel>();
            services.AddSingleton<ViewModels.DashboardViewModel>(); // Singleton for state preservation
            services.AddTransient<ViewModels.SettingsViewModel>();
            services.AddTransient<ViewModels.AboutViewModel>();

            // Views
            services.AddTransient<Views.MainWindow>();
            services.AddTransient<Views.DashboardPage>();
            services.AddTransient<Views.SettingsPage>();
            services.AddTransient<Views.AboutPage>();
            services.AddTransient<Views.RescanInfoWindow>();
        }

        private void SetupGlobalExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            
            // WPF specific exception handling
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var logger = Services?.GetService<ILogger<App>>();
                logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled domain exception");
                
                // Only show UI if not terminating
                if (!e.IsTerminating)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show("A critical error occurred. Please check the logs.", "Critical Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            catch
            {
                // Fallback logging
                File.AppendAllText("crash.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {e.ExceptionObject}\n");
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var logger = Services?.GetService<ILogger<App>>();
                logger?.LogError(e.Exception, "Unobserved task exception");
                
                e.SetObserved(); // Prevent app crash
            }
            catch
            {
                File.AppendAllText("crash.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: Unobserved Task Exception: {e.Exception}\n");
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var logger = Services?.GetService<ILogger<App>>();
                logger?.LogError(e.Exception, "Unhandled dispatcher exception");
                
                MessageBox.Show($"An error occurred: {e.Exception.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                
                e.Handled = true; // Prevent app crash
            }
            catch
            {
                // Let it crash if we can't handle it properly
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            if (trayIcon != null)
            {
                trayIcon.TrayMouseDoubleClick += TrayIcon_TrayMouseDoubleClick;
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }

        private void Menu_Show_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }

        private void Menu_Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BringExistingWindowToFront()
        {
            var current = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(current.ProcessName);

            foreach (var process in processes)
            {
                if (process.Id != current.Id)
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);       // nếu đang minimize
                        SetForegroundWindow(hWnd);         // đưa lên foreground
                    }
                    break;
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Services?.GetService<IScanWorkflowService>()?.Dispose();
                (Services as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                var logger = Services?.GetService<ILogger<App>>();
                logger?.LogWarning(ex, "Error during application shutdown");
            }
            finally
            {
                base.OnExit(e);
            }
        }
    }
}
