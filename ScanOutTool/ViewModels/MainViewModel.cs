using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using ScanOutTool.Helpers;
using ScanOutTool.Services;
using System.Linq;

namespace ScanOutTool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly INavigationService _navigationService;
        private readonly IUpdateService _updateService;

        [ObservableProperty]
        private string title;

        [ObservableProperty]
        private bool isSidebarOpen;

        [ObservableProperty]
        private int selectedMenuIndex;

        [ObservableProperty]
        private bool isDarkTheme;

        [ObservableProperty]
        private string pageName;

        public MainViewModel(INavigationService navigationService, IUpdateService updateService)
        {
            _navigationService = navigationService;
            _updateService = updateService;
            IsSidebarOpen = true;
            //SelectedMenuIndex = 0;

            // Khởi tạo trạng thái Theme hiện tại
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            IsDarkTheme = theme.GetBaseTheme() == BaseTheme.Dark;

            Title = $"Scan Out Tool v{getVersion()}";
            KillProcess.KillChromeDriver();
        }


        partial void OnIsDarkThemeChanged(bool value)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            if (value)
            {
                theme.SetBaseTheme(BaseTheme.Dark);
            }
            else
            {
                theme.SetBaseTheme(BaseTheme.Light);
                var swatch = new SwatchesProvider().Swatches.FirstOrDefault(s=>s.Name== "deeppurple");

                if (swatch != null)
                {
                    theme.SetPrimaryColor(swatch.PrimaryHues[5].Color); // index 5 = shade 500
                }
            }
            paletteHelper.SetTheme(theme);
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarOpen = !IsSidebarOpen;
        }

        partial void OnSelectedMenuIndexChanged(int value)
        {
            switch (value)
            {
                case 0:
                    _navigationService.NavigateTo<Views.DashboardPage>();
                    PageName = "DASHBOARD";
                    break;
                case 1:
                    _navigationService.NavigateTo<Views.SettingsPage>();
                    PageName = "SETTING";
                    break;
                case 2:
                    _navigationService.NavigateTo<Views.AboutPage>();
                    PageName = "ABOUT";
                    break;
                case 3:
                    _updateService.CheckForUpdatesAsync();
                    break;
            }
        }

        private string getVersion()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

            return assembly.GetName().Version.ToString();
        }

    }
}
