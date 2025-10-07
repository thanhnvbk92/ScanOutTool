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
        [ObservableProperty] private bool isBlockRFMode;

        [ObservableProperty] private string serverIP;

        [ObservableProperty] private string pLCIP;
        [ObservableProperty] private int pLCPort; 
        [ObservableProperty] private string shopFloorLogPath; 

        // ✅ NEW: PLC Usage Control
        [ObservableProperty] private bool usePLC;

        // Scanner Feedback Properties
        [ObservableProperty] private bool enableScannerFeedback;
        [ObservableProperty] private string okFeedbackMessage;
        [ObservableProperty] private string ngFeedbackMessage;
        [ObservableProperty] private int feedbackDelayMs;

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
            _configService.Config.IsBlockRFMode = IsBlockRFMode;
            
            // ✅ NEW: Save PLC usage setting
            _configService.Config.UsePLC = UsePLC;
            
            // Save feedback settings
            _configService.Config.EnableScannerFeedback = EnableScannerFeedback;
            _configService.Config.OkFeedbackMessage = OkFeedbackMessage;
            _configService.Config.NgFeedbackMessage = NgFeedbackMessage;
            _configService.Config.FeedbackDelayMs = FeedbackDelayMs;
            
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
            IsBlockRFMode = _configService.Config.IsBlockRFMode;
            ShopFloorLogPath = _configService.Config.ShopFloorLogPath ?? "C:\\Admin\\Documents\\LG CNS\\ezMES\\Logs";
            
            // ✅ NEW: Load PLC usage setting
            UsePLC = _configService.Config.UsePLC;
            
            // Load feedback settings
            EnableScannerFeedback = _configService.Config.EnableScannerFeedback;
            OkFeedbackMessage = _configService.Config.OkFeedbackMessage ?? "OK";
            NgFeedbackMessage = _configService.Config.NgFeedbackMessage ?? "NG";
            FeedbackDelayMs = _configService.Config.FeedbackDelayMs;
        }
    }
}
