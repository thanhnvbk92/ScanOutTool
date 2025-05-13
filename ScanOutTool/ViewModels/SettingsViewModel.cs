using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanOutTool.Models;
using ScanOutTool.Services;

namespace ScanOutTool.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly IAppState _appState;

        [ObservableProperty]
        private SerialPortSettingViewModel scannerPortSettingVM;      

        [ObservableProperty]
        private SerialPortSettingViewModel shopFloorPortSettingVM;

        [ObservableProperty]
        private bool isRobotMode;

        [ObservableProperty]
        private string serverIP;

        public bool CanEdit => !_appState.IsRunning;

        [RelayCommand]
        private void Save()
        {
            _configService.Config.ScannerPortSettingVM = ScannerPortSettingVM;
            _configService.Config.ShopFloorPortSettingVM = ShopFloorPortSettingVM;
            _configService.Config.IsRobotMode = IsRobotMode;
            _configService.Config.ServerIP = ServerIP;
            _configService.Save();
        }


        public SettingsViewModel(IConfigService configService,IAppState appState)
        {
            _configService = configService;
            _appState = appState;
            LoadSettings();
        }

        private void LoadSettings()
        {
            ScannerPortSettingVM = _configService.Config.ScannerPortSettingVM?? new SerialPortSettingViewModel();
            ShopFloorPortSettingVM = _configService.Config.ShopFloorPortSettingVM?? new SerialPortSettingViewModel();
            IsRobotMode = _configService.Config.IsRobotMode;
            ServerIP = _configService.Config.ServerIP;
        }
    }
}
