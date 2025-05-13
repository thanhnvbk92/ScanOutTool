using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataExcuter;
using ScanOutTool.Models;
using ScanOutTool.Services;
using SerialProxyLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ScanOutTool.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {        
        public enum RunMode
        {
            ScanOutOnly,
            RescanOnly,
            ScanOut_Rescan
        }

        private readonly IAppState _appState;
        private readonly ILoggingService _loggingService;
        private readonly IConfigService _configService;

        private IAutoScanOutUI _autoScanOutUI;
        private SerialProxyManager _serialProxyManager;
        private IPLCPackingService _plcService;
        private bool _isPLCConnected = false;

        private bool IsScanOutOnly => SelectedRunMode == RunMode.ScanOutOnly;
        private bool IsRescanOnly => SelectedRunMode == RunMode.RescanOnly;
        private bool IsScanOutRescan => SelectedRunMode == RunMode.ScanOut_Rescan;

        private bool _isStarted;
        public bool IsStarted
        {
            get => _isStarted;
            set
            {
                SetProperty(ref _isStarted, value);
                StartBtnText = _isStarted ? "STOP" : "START";
            }
        }

        private DataExecuter _dataExecuter;

        [ObservableProperty] private bool isSessionStarting;
        [ObservableProperty] private bool isInChooseEBRMode=false;
        [ObservableProperty] private bool isPCBtoSelectEBR = false;
        [ObservableProperty] private bool isConfirmEBRStep = false;
        [ObservableProperty] private bool isDataSending;
        [ObservableProperty] private string logs;
        [ObservableProperty] private string startBtnText;
        [ObservableProperty] private string pID;
        [ObservableProperty] private string partNo;
        [ObservableProperty] private string workOrder;
        [ObservableProperty] private string result;
        [ObservableProperty] private string resultMessage;
        [ObservableProperty] private string pCBLocation;
        [ObservableProperty] private RunMode selectedRunMode;
        [ObservableProperty] private List<RunMode> runModes;
        [ObservableProperty] private string selectedEBR;
        [ObservableProperty] private string informationMessage;
        [ObservableProperty] private bool isMessageOn =false;


        [RelayCommand]
        private async  Task Start()
        {
            if (!IsStarted)
            {
                IsStarted = true;
                StartBtnText = "STOP";
                _appState.IsRunning = true;
                _ = Task.Run(StartSerialProxyAsync);
                await Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAll(
                            InitScanOutUIAndReadAsync(),
                            StartDataExecuteSessionAsync(),
                            StartPLCAsync()
                        );
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Error during StartAllAsync: {ex.Message}");
                    }
                });
            }
            else
            {                
                
                IsStarted = false;
                StartBtnText = "START";
                _appState.IsRunning = false;

                StopSerialProxy();
                _plcService?.Dispose();
                _isPLCConnected = false;

            }
        }

        public DashboardViewModel(ILoggingService loggingService, IConfigService configService, IAppState appState)
        {
            _loggingService = loggingService;
            _configService = configService;
            _appState = appState;

            InitializeServices();
        }

        private void InitializeServices()
        {
            IsStarted = false;
            _plcService = new PLCPackingService("192.168.100.100", 2001, true)
            {
                LoggingService = _loggingService
            };

            var cfg = _configService.Config;
            _dataExecuter = new DataExecuter(new DataExecuterConfig
            {
                DbHost = cfg.ServerIP,
                WebIpAddress = cfg.ServerIP
            });

            _loggingService.OnNewLog += LoggerService_OnNewLog;
            _dataExecuter.StatusChanged += OnDataExcuteStatusChanged;

            RunModes = Enum.GetValues(typeof(RunMode)).Cast<RunMode>().ToList();
            SelectedRunMode = RunMode.ScanOut_Rescan;
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

        private async Task StartSerialProxyAsync()
        {
            SerialPortSettingViewModel comDevice = _configService.Config.ScannerPortSettingVM; // SerialPortSettingViewModel comDevice
            SerialPortSettingViewModel comApp = _configService.Config.ShopFloorPortSettingVM; // SerialPortSettingViewModel comApp
            if (comDevice == null || comApp == null)
            {
                _loggingService.LogError("COM device or app settings are not configured.");
                return;
            }

            _serialProxyManager = new SerialProxyManager
            {
                Logger = new SerilogProxyLogger(_loggingService),
                WaitForGuiProcessAsync = async (sentData) =>
                {
                    if(sentData=="CLEAR" || sentData == "TRACE")
                    {
                        return true;
                    }
                    // Giả lập chờ GUI hiển thị (thay bằng gọi UIAutomation thực tế)
                    bool result = await ReadScanOutResult(sentData);
                    if (result && !isInChooseEBRMode)
                    {
                        await SendDataExecuteAsync();
                    }                       
                    else if(result && isInChooseEBRMode)
                    {
                        InformationMessage = $"Bạn có muốn chọn {PartNo} để Rescan không?\r\n Nếu có scan TRACE";
                        IsMessageOn = true;
                        IsConfirmEBRStep = true;
                    }   

                    // Nếu cần: đọc GUI tại đây rồi return true/false
                    return true;
                }
            };

            _serialProxyManager.OnDataForwarding += async (s, e) =>
            {
                _loggingService.LogInformation($"[Event] {(e.FromDevice ? "Device" : "App")} gửi: {e.Data}");
              
                // Vaof chees ddooj chonj EBR
                if(e.Data.ToUpper().Contains("CHOOSE"))
                {
                    if(!IsInChooseEBRMode)
                    {
                        IsInChooseEBRMode = true;
                        // Nếu dữ liệu có chứa từ khóa "CHOOSE", thì hiển thị cửa sổ chọn EBR
                        InformationMessage = ($"Vui lòng scan PCB PartNo mà bạn muốn Rescan");
                        IsMessageOn = true;
                    }
                    else
                    {
                        IsInChooseEBRMode = false;
                        // Nếu dữ liệu có chứa từ khóa "CHOOSE", thì hiển thị cửa sổ chọn EBR
                        InformationMessage = "";
                        IsMessageOn = false;
                    }
                    e.Cancel=true;
                }
                else if (e.Data.ToUpper().Contains("TRACE") && IsConfirmEBRStep)
                {
                    SelectedEBR = PartNo;
                    InformationMessage = $"";
                    IsMessageOn = false;
                    IsConfirmEBRStep = false;
                    IsInChooseEBRMode=false;
                    await SendDataExecuteAsync();
                    e.Cancel = true; // Ngăn không cho dữ liệu đi tiếp
                }
                else if (e.Data.ToUpper().Contains("CLEAR") && IsInChooseEBRMode)
                {     
                    InformationMessage = $"";
                    IsMessageOn = false;
                    IsConfirmEBRStep = false;
                    IsInChooseEBRMode = false;
                    e.Cancel = true; // Ngăn không cho dữ liệu đi tiếp
                }

                if (SelectedRunMode == RunMode.RescanOnly)
                {                    
                    e.Cancel = true;
                    await _dataExecuter.SendDatatoRescanAsync(e.Data);
                }
            };
            //_loggingService.LogInformation($"Starting serial proxy: Device={comDevice.SelectedPort}, App={comApp.SelectedPort}, BaudRate={comDevice.SelectedBaudRate}, Parity={comDevice.SelectedParity}, DataBits={comDevice.SelectedDataBits}, StopBits={comDevice.SelectedStopBits}");

            // Bắt đầu proxy (COM3 là thiết bị, COM5 là app chính, sniff sẽ tự tạo là COM4)
            await _serialProxyManager.StartProxy(comDevice.SelectedPort, comApp.SelectedPort, comDevice.SelectedBaudRate, comDevice.SelectedParity, comDevice.SelectedDataBits, comDevice.SelectedStopBits);
            //_loggingService.LogInformation("Serial proxy started successfully.");
        }

        private async Task StopSerialProxy()
        {
            // Dừng proxy
            _serialProxyManager?.Stop();
        }

        private async Task InitScanOutUIAndReadAsync()
        {
            try
            {
                _autoScanOutUI = new AutoScanOutUI();
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to initialize AutoScanOutUI: {ex.Message}");
            }
            await ReadScanOutGUI();
        }

        private async Task StartPLCAsync()
        {
            if (!_configService.Config.IsRobotMode) return;
            try
            {
                if (_plcService != null)
                {
                    _plcService.TryConnect();
                    _isPLCConnected = _plcService.IsConnected;
                    if (_isPLCConnected)
                    {
                        _loggingService.LogInformation("PLC connected successfully.");
                        await ReadPCBInfoFromPCB();
                    }
                    else
                    {
                        _loggingService.LogError("Failed to connect to PLC.");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error connecting to PLC: {ex.Message}");
            }
        }


        private async Task StartDataExecuteSessionAsync()
        {
            try
            {
                IsSessionStarting = true;
                bool success = await _dataExecuter.StartSessionAsync();

                if (!success)
                {
                    //MessageBox.Show("Failed to start session.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _loggingService.LogError("Failed to start data execute session.");
                }
            }
            finally
            {
                IsSessionStarting = false;
            }
        }

        private async Task SendDataExecuteAsync()
        {
            if (IsInChooseEBRMode) return;
            
            try
            {
                IsDataSending = true;               

                _loggingService.LogInformation($"Sending data to database: WorkOrder={workOrder}, ModelSuffix={partNo}, WipSerialNumber={pID}");
                ProcessResult result = new ProcessResult();
                if(IsScanOutOnly)
                {
                    result =  await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNo, pID);
                }
                else if (IsRescanOnly)
                {
                    result = await _dataExecuter.SendDatatoRescanAsync(pID);
                }
                else
                {
                    if(!string.IsNullOrEmpty(SelectedEBR) && (partNo != SelectedEBR))
                    {
                        int PackQty = await _dataExecuter.GetPackQty();
                        _loggingService.LogInformation($"PackQty: {PackQty}");
                        if(PackQty==0)
                        {
                            _loggingService.LogInformation($"PartNo miss match: {partNo} != {SelectedEBR}");
                            result = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNo, pID);
                            return;
                        }    
                    }                      
                    result = await _dataExecuter.ProcessDataAsync(workOrder, partNo, pID);
                }


                if (!result.Success)
                {
                    // 실패 시 메시지 표시
                    if (!string.IsNullOrEmpty(result.ValidationResult))
                    {
                        //MessageBox.Show(result.ValidationResult, "Data Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _loggingService.LogError($"Validation Error: {result.ValidationResult}");
                    }
                    else
                    {
                        //MessageBox.Show(result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _loggingService.LogError($"Error: {result.Message}");
                    }
                }

                // 데이터 클리어 (설정에 따라)
                if (_dataExecuter.ShouldClearAfterSend)
                {
                    ClearInputFields();
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                //MessageBox.Show("NG", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _loggingService.LogError($"Error sending data: {ex.Message}");
            }
            finally
            {
                IsDataSending = false;
            }
        }


        public async Task<bool> ReadScanOutResult(string pid)
        {
            if (_autoScanOutUI == null)
            {
                _loggingService.LogError("AutoScanOutUI is not initialized.");
                return false;
            }

            pid = pid.Trim();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (pid != _autoScanOutUI.ReadPID())
            {
                if (_autoScanOutUI.ReadPID().Contains("PLZ Read below Message and Clear") && _autoScanOutUI.ReadMessage().Contains(pid))
                {
                    break;
                }
                if (sw.ElapsedMilliseconds > 5000)
                {
                    _loggingService.LogError($"Timeout to get scanout PID {pid} result: {_autoScanOutUI.ReadPID()} - {_autoScanOutUI.ReadMessage()}");
                    return false;
                }
                Task.Delay(200).Wait();
            }
            sw.Stop();
            sw.Reset();

            try
            {
                PID = _autoScanOutUI.ReadPID();
                WorkOrder = _autoScanOutUI.ReadWO();
                PartNo = _autoScanOutUI.ReadEBR();
                Result = _autoScanOutUI.ReadResult();
                ResultMessage = _autoScanOutUI.ReadMessage(); 

                if (PID.Contains("PLZ Read below Message and Clear"))
                {
                    _loggingService.LogInformation($"Data from ScanOut GUI:{pid},{PartNo},{WorkOrder},{Result},{ResultMessage}");
                    return false;
                }     
                else
                    _loggingService.LogInformation($"Data from ScanOut GUI:{PID},{PartNo},{WorkOrder},{Result}");

                if (Result == "OK")
                {
                    _loggingService.LogInformation($"Scan out OK: PID: {PID}, WorkOrder: {WorkOrder}, PartNo: {PartNo}, Result: {Result}");
                    return true;
                }
                else
                {
                    _loggingService.LogError($"Scan out NG: PID: {PID}, WorkOrder: {WorkOrder}, PartNo: {PartNo}, Result: {Result}, {ResultMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to read scan out result: {ex.Message}");
                return false;
            }
        }

        private async Task ReadScanOutGUI()
        {
            if (_autoScanOutUI == null) return;

            PID = _autoScanOutUI.ReadPID();
            WorkOrder = _autoScanOutUI.ReadWO();
            PartNo = _autoScanOutUI.ReadEBR();
            Result = _autoScanOutUI.ReadResult();
            ResultMessage = _autoScanOutUI.ReadMessage();

        }

        private void ClearInputFields()
        {
            workOrder = "";
            partNo = "";
            pID = "";
        }


        private async Task ReadPCBInfoFromPCB()
        {            
            while (_isStarted)
            {
                if (_plcService.IsConnected)
                {
                    _loggingService.LogInformation("Reading PCB info from PLC...");
                    var TrayNo = _plcService.GetTray();
                    _loggingService.LogInformation($"TrayNo: {TrayNo}");
                    var TrayQty = _plcService.GetTotalTray();
                    //_loggingService.LogInformation($"TrayQty: {TrayQty}");
                    //var slot1 = _plcService.ReadBit("M341");
                    //_loggingService.LogInformation($"Slot1: {slot1}");
                    //var slot2 = _plcService.ReadBit("M342");
                    //_loggingService.LogInformation($"Slot2: {slot2}");
                    //var slot3 = _plcService.ReadBit("M343");
                    //_loggingService.LogInformation($"Slot3: {slot3}");
                    //var slot4 = _plcService.ReadBit("M344");
                    //_loggingService.LogInformation($"Slot4: {slot4}");
                    //var slot5 = _plcService.ReadBit("M345");
                    //_loggingService.LogInformation($"Slot5: {slot5}");
                    //var slot6 = _plcService.ReadBit("M346");
                    //_loggingService.LogInformation($"Slot6: {slot6}");

                    //_loggingService.LogInformation($"TrayNo: {TrayNo}, TrayQty: {TrayQty}, Slot1: {slot1}, Slot2: {slot2}, Slot3: {slot3}, Slot4: {slot4}, Slot5: {slot5}, Slot6: {slot6}");
                    string text = $"{TrayNo} - {TrayQty} ";
                    //MessageBox.Show(text);
                    if (PCBLocation != text)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                         {
                             PCBLocation = text;
                         });                        
                    }
                        
                }
                else
                {
                    _loggingService.LogError("Failed to connect to PLC.");
                }
                await Task.Delay(2000); // Delay to avoid tight loop
            }
        }

        private void OnDataExcuteStatusChanged(object sender, string status)
        {
            //MessageBox.Show(status);
            _loggingService.LogInformation(status);
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

    }
}
