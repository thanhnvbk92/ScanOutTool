using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using ScanOutTool.Models;
using ScanOutTool.Services;

namespace ScanOutTool.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly IAppState _appState;

        [ObservableProperty] private SerialPortSettingViewModel scannerPortSettingVM;      

        [ObservableProperty] private SerialPortSettingViewModel shopFloorPortSettingVM;

        [ObservableProperty] private bool isRobotMode;

        [ObservableProperty] private bool isWOMode;

        [ObservableProperty] private string serverIP;

        [ObservableProperty] private string pLCIP;
        [ObservableProperty] private int pLCPort; 
        [ObservableProperty] private string shopFloorLogPath; 

        public bool CanEdit => !_appState.IsRunning;

        [RelayCommand]
        private void Save()
        {
            _configService.Config.ScannerPortSettingVM = ScannerPortSettingVM;
            _configService.Config.ShopFloorPortSettingVM = ShopFloorPortSettingVM;
            _configService.Config.IsRobotMode = IsRobotMode;
            _configService.Config.ServerIP = ServerIP;
            _configService.Config.IsWOMode = IsWOMode;
            _configService.Config.PLCIP = PLCIP;
            _configService.Config.PLCPort = PLCPort;
            _configService.Config.ShopFloorLogPath = ShopFloorLogPath;
            _configService.Save();
        }

        [RelayCommand]
        private void LogBrowser()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                ShopFloorLogPath = dialog.FileName;
            }
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
            IsWOMode = _configService.Config.IsWOMode;
            ServerIP = _configService.Config.ServerIP;
            PLCIP = _configService.Config.PLCIP;
            PLCPort = _configService.Config.PLCPort;
            ShopFloorLogPath = _configService.Config.ShopFloorLogPath ?? "C:\\Admin\\Documents\\LG CNS\\ezMES\\Logs";
        }
    }
}
