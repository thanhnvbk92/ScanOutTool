using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanOutTool.Services;
using SerialProxyLib;
using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ScanOutTool.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {        
        private readonly ILoggingService _loggingService;
        private readonly IConfigService _configService;

        private IAutoScanOutUI _autoScanOutUI;
        private SerialProxyManager _serialProxyManager;
        private IPlcService _plcService;

        private bool _isStarted = false;

        [ObservableProperty]
        private string logs;

        [ObservableProperty]
        private string startBtnText;

        [ObservableProperty]
        private string pID;

        [ObservableProperty]
        private string partNo;

        [ObservableProperty]
        private string workOrder;

        [ObservableProperty]
        private string result;

        [ObservableProperty]
        private string pCBLocation;


        [RelayCommand]
        private void Start()
        {
            if (StartBtnText == "START")
            {
                StartBtnText = "STOP";
                Task.Run(async () =>
                {
                    StartSerialProxy(_configService.Config.ScannerPortSettingVM.SelectedPort, _configService.Config.ShopFloorPortSettingVM.SelectedPort, _configService.Config.ScannerPortSettingVM.SelectedBaudRate, _configService.Config.ScannerPortSettingVM.SelectedParity, _configService.Config.ScannerPortSettingVM.SelectedDataBits, _configService.Config.ScannerPortSettingVM.SelectedStopBits).ConfigureAwait(false);
                });
                
                try
                {
                    _autoScanOutUI = new AutoScanOutUI();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to initialize AutoScanOutUI: {ex.Message}");
                }
                Task.Run(async () =>
                {
                    await ReadScanOutResult();
                });
                Task.Run(async () =>
                {
                    await ReadPCBInfoFromPCB();
                });
                _isStarted = true;
            }
            else
            {
                StartBtnText = "START";
                StopSerialProxy();
                _isStarted = false;
            }
            
        }

        public DashboardViewModel(ILoggingService loggingService, IConfigService configService)
        {
            _loggingService = loggingService;
            _configService = configService;
            _loggingService.OnNewLog += LoggerService_OnNewLog;

            _loggingService.LogInformation("DashboardViewModel initialized");
            startBtnText = "START";
        }


        private void LoggerService_OnNewLog(object? sender, LogEntry e)
        {
            const int MaxLines = 100;
            string log = Logs;
            log = log + "\r\n" + $"{e.Timestamp}\t[{e.Level}]\t{e.Message}";
            var lines = log.Split('\n');
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (lines.Length > MaxLines)
                    Logs = string.Join("\n", lines.Skip(lines.Length - MaxLines));
                else
                    Logs = log;
            });
        }

        private async Task StartSerialProxy(string comDevice, string comApp, int baudrate, Parity parity, int databit, StopBits stopBits)
        {
            _serialProxyManager = new SerialProxyManager
            {
                Logger = new SerilogProxyLogger(_loggingService),
                WaitForGuiProcessAsync = async (sentData) =>
                {
                    Console.WriteLine($"[UI-Auto] Đang giả lập kiểm tra GUI cho: {sentData}");

                    // Giả lập chờ GUI hiển thị (thay bằng gọi UIAutomation thực tế)
                    await Task.Delay(1000);

                    // Nếu cần: đọc GUI tại đây rồi return true/false
                    return true;
                }
            };

            _serialProxyManager.OnDataForwarding += (s, e) =>
            {
                _loggingService.LogInformation($"[Event] {(e.FromDevice ? "Device" : "App")} gửi: {e.Data}");

                // Nếu muốn block dữ liệu có chứa từ khóa
                if (e.Data.Contains("BLOCK"))
                {
                    Console.WriteLine(">> Dữ liệu bị chặn!");
                    e.Cancel = true;
                }
            };


            // Bắt đầu proxy (COM3 là thiết bị, COM5 là app chính, sniff sẽ tự tạo là COM4)
            await _serialProxyManager.StartProxy(comDevice, comApp, baudrate,parity,databit,stopBits);
        }

        private async Task StopSerialProxy()
        {
            // Dừng proxy
            _serialProxyManager.Stop();
        }

        public class SerilogProxyLogger : IProxyLogger
        {
            private readonly ILoggingService _logger;
            public SerilogProxyLogger(ILoggingService logger)
            {
                _logger = logger;
            }

            public void Log(string message)
            {
                _logger.LogInformation(message);
            }
        }

        public async Task<bool> ReadScanOutResult()
        {
            if (_autoScanOutUI == null)
            {
                _loggingService.LogError("AutoScanOutUI is not initialized.");
                return false;
            }
            try
            {
                PID = _autoScanOutUI.ReadPID();
                WorkOrder = _autoScanOutUI.ReadWO();
                PartNo = _autoScanOutUI.ReadEBR();
                Result = _autoScanOutUI.ReadResult();
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to read scan out result: {ex.Message}");
                return false;
            }
        }

        private async Task ReadPCBInfoFromPCB()
        {
            _plcService = new MitsubishiPlcService();
            while (_isStarted)
            {
                if (_plcService.Connect("192.168.100.100", 2001))
                {
                    var TrayNo = _plcService.ReadInt16("D156");
                    var TrayQty = _plcService.ReadInt16("D8082");
                    var slot1 = _plcService.ReadInt16("M341");
                    var slot2 = _plcService.ReadInt16("M342");
                    var slot3 = _plcService.ReadInt16("M343");
                    var slot4 = _plcService.ReadInt16("M344");
                    var slot5 = _plcService.ReadInt16("M345");
                    var slot6 = _plcService.ReadInt16("M346");

                    string text = $"{TrayNo} - {TrayQty} {slot1 + slot2 + slot3 + slot4 + slot5 + slot6}";

                    if (PCBLocation != text)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                         {
                             PCBLocation = text;
                         });
                        await ReadScanOutResult();
                    }
                        _plcService.Disconnect();
                }
                else
                {
                    _loggingService.LogError("Failed to connect to PLC.");
                }
                await Task.Delay(2000); // Delay to avoid tight loop
            }
            

        }
        
    }
}
