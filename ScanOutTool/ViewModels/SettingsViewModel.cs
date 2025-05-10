using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanOutTool.Services;

namespace ScanOutTool.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;

        [ObservableProperty]
        private SerialPortSettingViewModel scannerPortSettingVM;      

        [ObservableProperty]
        private SerialPortSettingViewModel shopFloorPortSettingVM;

        [RelayCommand]
        private void Save()
        {
            _configService.Config.ScannerPortSettingVM = ScannerPortSettingVM;
            _configService.Config.ShopFloorPortSettingVM = ShopFloorPortSettingVM;
            _configService.Save();
        }


        public SettingsViewModel(IConfigService configService)
        {
            _configService = configService;
            LoadSettings();
        }

        private void LoadSettings()
        {
            ScannerPortSettingVM = _configService.Config.ScannerPortSettingVM?? new SerialPortSettingViewModel();
            ShopFloorPortSettingVM = _configService.Config.ShopFloorPortSettingVM?? new SerialPortSettingViewModel();
        }
    }
}
