using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Configuration;
using System.Windows;


namespace ScanOutTool
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }
        public static IConfiguration Configuration { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            // Tạo MainWindow từ DI
            var mainWindow = Services.GetRequiredService<Views.MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("Resources/appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            Configuration = config;
            services.AddSingleton<IConfiguration>(config);

            services.AddLogging(builder => builder.AddConsole());

            // ViewModels
            services.AddTransient<ViewModels.MainViewModel>();
            services.AddTransient<ViewModels.DashboardViewModel>();
            services.AddTransient<ViewModels.SettingsViewModel>();
            services.AddTransient<ViewModels.AboutViewModel>();

            // Services
            services.AddSingleton<Services.INavigationService, Services.NavigationService>();
            services.AddSingleton<Services.ILoggingService, Services.LoggingService>();
            services.AddSingleton<Services.IUpdateService, Services.UpdateService>();
            services.AddSingleton<Services.IConfigService, Services.ConfigService>();

            // Views
            services.AddTransient<Views.MainWindow>();
            services.AddTransient<Views.DashboardPage>();
            services.AddTransient<Views.SettingsPage>();
            services.AddTransient<Views.AboutPage>();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            trayIcon.TrayMouseDoubleClick += TrayIcon_TrayMouseDoubleClick;
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        private void Menu_Show_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        private void Menu_Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
