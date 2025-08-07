using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataExcuter;
using ScanOutLogLib.Interfaces;
using ScanOutLogLib.Services;
using ScanOutTool.Models;
using ScanOutTool.Services;
using ScanOutTool.Views;
using SerialProxyLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ScanOutTool.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {        
        public enum RunMode
        {
            ScanOutOnly,
            RescanOnly,
            ScanOut_Rescan,
            None
        }

        private readonly IAppState _appState;
        private readonly ILoggingService _loggingService;
        private readonly IConfigService _configService;
        private readonly IPLCServiceFactory _plcFactory;
        private readonly IScanResultService _resultService;
        private readonly IScanResultDispatcher _dispatcher;
        private readonly IShowRescanResultService _showRescanResultService;
        private readonly IBlockRFService _blockRFService;

        private IPLCService? _plcService;

        private IAutoScanOutUI _autoScanOutUI;
        private SerialProxyManager _serialProxyManager;
        private bool _isPLCConnected = false;

        private bool IsScanOutOnly => SelectedRunMode == RunMode.ScanOutOnly;
        private bool IsRescanOnly => SelectedRunMode == RunMode.RescanOnly;
        private bool IsScanOutRescan => SelectedRunMode == RunMode.ScanOut_Rescan;
        private bool IsNone => SelectedRunMode == RunMode.None;

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
        private Stopwatch mainSW = new Stopwatch();

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
        [ObservableProperty] private int magazineQty;


        [RelayCommand]
        private async  Task Start()
        {
            if (!IsStarted)
            {
                IsStarted = true;
                StartBtnText = "STOP";
                _appState.IsRunning = true;
                //string logRoot = _configService.Config.ShopFloorLogPath;
                //if (!Directory.Exists(logRoot))
                //{
                //    _loggingService.LogError($"Log path does not exist: {logRoot}");
                //    return;
                //}
                //_dispatcher.Start(logRoot);
                //_dispatcher.OnLog += (log) =>
                //{
                //    _loggingService.LogInformation(log);
                //};
                //_resultService.OnLog += (log) =>
                //{
                //    _loggingService.LogInformation(log);
                //};
                //_resultService.Start(logRoot);
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
                _dispatcher.Stop();
                StopSerialProxy();
                _plcService?.Dispose();
                _isPLCConnected = false;

            }
        }

        public DashboardViewModel(ILoggingService loggingService, IConfigService configService, IAppState appState, IPLCServiceFactory plcFactory, IScanResultService scanResultService, IScanResultDispatcher dispatcher,IBlockRFService blockRFService)
        {
            _loggingService = loggingService;
            _configService = configService;
            _appState = appState;
            _plcFactory = plcFactory;
            _resultService = scanResultService;
            _dispatcher = dispatcher;
            _blockRFService = blockRFService;

            _loggingService.OnNewLog += LoggerService_OnNewLog;

            _loggingService.LogInformation("Initializing DashboardViewModel...");
            //Task.Run(async () =>
            //{
            //    var data = await _blockRFService.IsBlock("506HS1V1889");
            //});
            
            InitializeServices();
        }

        private void InitializeServices()
        {
            IsStarted = false;

            var cfg = _configService.Config;
            _dataExecuter = new DataExecuter(new DataExecuterConfig
            {
                DbHost = cfg.ServerIP,
                WebIpAddress = cfg.ServerIP
            });

            
            _dataExecuter.StatusChanged += OnDataExcuteStatusChanged;

            RunModes = Enum.GetValues(typeof(RunMode)).Cast<RunMode>().ToList();
            SelectedRunMode = RunMode.ScanOut_Rescan;
        }

        public void InitializePLC()
        {
            
            if (_configService.Config.IsRobotMode && _configService.Config.PLCIP != null && _configService.Config.PLCPort != 0)
            {
                _plcService?.Dispose(); // nếu đã có instance cũ thì bỏ
                string ip = _configService.Config.PLCIP;
                int port = _configService.Config.PLCPort;
                bool isAsciiMode = true;

                _plcService = _plcFactory.Create(ip, port, isAsciiMode);

                _plcService.OnConnectionChanged += (conn) =>
                {
                    _isPLCConnected = conn;
                    if (conn)
                    {
                        _loggingService.LogInformation("PLC connected.");
                    }
                    else
                    {
                        _loggingService.LogError("PLC disconnected.");
                    }
                };
            }
            else
            {
                _loggingService.LogError("PLC service is not configured or IP/Port is invalid.");
            }
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
                    if(sentData.Contains("CLEAR") || sentData.Contains("TRACE") || IsNone )
                    {
                        return true;
                    }

                    if (!_autoScanOutUI.IsScanoutUI()) return true;
                    var readScanOutTask = ReadScanOutResult(sentData);
                    bool result = await readScanOutTask;

                    var (isHSMESBlocked, reason) = _dataExecuter.IsBlocked(sentData);
                    if (isHSMESBlocked)
                    {                       
                        PlayNgSound();
                        InformationMessage = $"PID {sentData} đã bị block do {reason} , vui lòng kiểm tra lại";
                        IsMessageOn = true;
                        return true;
                    }

                    if (result && !isInChooseEBRMode)
                    {
                        bool executeResult = await SendDataExecuteAsync();
                        if (_configService.Config.IsRobotMode && executeResult)
                        {
                            await ReadPCBInfoFromPCB();
                        }
                    }                       
                    else if(result && isInChooseEBRMode)
                    {
                        if(_configService.Config.IsWOMode)
                        {
                            InformationMessage = $"Bạn có muốn chọn {WorkOrder} để Rescan không?\r\n Nếu có scan TRACE";
                        }
                        else
                        {
                            InformationMessage = $"Bạn có muốn chọn {PartNo} để Rescan không?\r\n Nếu có scan TRACE";
                        }

                        IsMessageOn = true;
                        IsConfirmEBRStep = true;
                    }   

                    // Nếu cần: đọc GUI tại đây rồi return true/false
                    return true;
                }
            };

            _serialProxyManager.OnDataForwardingAsync += async (s, e) =>
            {
                PlayBeepSound();
                mainSW.Restart(); //Restart main stop watch
                IsMessageOn = false;
                _loggingService.LogInformation($"[Event] {(e.FromDevice ? "Device" : "App")} gửi: {e.Data}");
              
                if(_configService.Config.IsBlockRFMode )
                {             
                    RFInfo rFInfo = await _blockRFService.IsBlock(e.Data);
                    if(rFInfo != null)
                    {
                        PlayNgSound();
                        e.Cancel = true; // Ngăn không cho dữ liệu đi tiếp
                        InformationMessage = $"PID {e.Data} đã bị chặn do NG RF ở jig {rFInfo.MachineIP} band {rFInfo.Band} sinal{rFInfo.Signpath}, vui lòng kiểm tra lại";
                        IsMessageOn = true;
                        return;
                    }    
                    
                }                

                _loggingService.LogInformation($"Check block done,{mainSW.ElapsedMilliseconds}ms");
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
                    _loggingService.LogInformation($"[Event] {e.Data} - Is in Choose Mode: {IsInChooseEBRMode}");
                    e.Cancel=true;
                }
                else if (e.Data.ToUpper().Contains("TRACE") && IsConfirmEBRStep)
                {
                    if(_configService.Config.IsWOMode)
                    {
                        SelectedEBR = WorkOrder;
                    }
                    else
                    {
                        SelectedEBR = PartNo;
                    }                    
                    InformationMessage = $"";
                    IsMessageOn = false;
                    IsConfirmEBRStep = false;
                    IsInChooseEBRMode=false;
                    await SendDataExecuteAsync();
                    _loggingService.LogInformation($"[Event] {e.Data} - Is in Choose Mode: {IsInChooseEBRMode}");
                    e.Cancel = true; // Ngăn không cho dữ liệu đi tiếp
                }
                else if (e.Data.ToUpper().Contains("CLEAR") && IsInChooseEBRMode)
                {     
                    InformationMessage = $"";
                    IsMessageOn = false;
                    IsConfirmEBRStep = false;
                    IsInChooseEBRMode = false;
                    _loggingService.LogInformation($"[Event] {e.Data} - Is in Choose Mode: {IsInChooseEBRMode}");
                    e.Cancel = true; // Ngăn không cho dữ liệu đi tiếp
                }
                else if (!e.Data.Contains("CLEAR") && !e.Data.Contains("TRACE") && e.Data.Trim().Length != 11 && e.Data.Trim().Length != 22)
                {
                    _loggingService.LogInformation($"[Event] {e.Data.Trim()} - Data length: {e.Data.Trim().Length}");
                    e.Cancel = true;
                }
                _loggingService.LogInformation($"Check logic done {mainSW.ElapsedMilliseconds}");
                if (SelectedRunMode == RunMode.RescanOnly)
                {           
                    _loggingService.LogInformation($"[Event] {e.Data} - Rescan only mode");
                    e.Cancel = true;
                    await _dataExecuter.SendDatatoRescanAsync(e.Data);
                    int currentPCBQty = await _dataExecuter.GetPackQty();
                    int totalPCBQty = await _dataExecuter.GetMagazineQty();

                    _loggingService.LogInformation($"Current PCB Qty: {currentPCBQty}, Total PCB Qty: {totalPCBQty}, Magazine Qty: {MagazineQty}, {mainSW.ElapsedMilliseconds}ms");
                    if (MagazineQty != 0 && MagazineQty != totalPCBQty && currentPCBQty == MagazineQty)
                    {
                        if (MagazineQty > totalPCBQty && currentPCBQty >= (totalPCBQty - 1))
                            MessageBox.Show("Số lượng PCB trên hệ thống HMES đang nhỏ hơn số lượng cài đặt", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        await _dataExecuter.PrintManual();
                    }
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
                _autoScanOutUI = new AutoScanOutUI(_loggingService);
                //var rect = _autoScanOutUI.GetResultElementBounds();
                //Application.Current.Dispatcher.Invoke(() =>
                //{
                //    _showRescanResultService.ShowRescanResult(rect.X, rect.Y, rect.Width, rect.Height);
                //});

                //_showRescanResultService.SetRescanResult("NG", "10/20", "OK added");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to initialize AutoScanOutUI: {ex.Message}");
                return;
            }
            await ReadScanOutGUI();
        }

        private async Task StartPLCAsync()
        {
            if (!_configService.Config.IsRobotMode) return;
            InitializePLC();
            _loggingService.LogInformation($"Starting PLC: IP={_configService.Config.PLCIP}, Port={_configService.Config.PLCPort}");
            _loggingService.LogInformation($"Total Tray: {_plcService.GetTotalTray()}");
            _loggingService.LogInformation($"Current Tray: {_plcService.GetCurrentTray()}");
            _loggingService.LogInformation($"Total Slot: {_plcService.GetTotalSlot()}");
            _loggingService.LogInformation($"Current Slot: {_plcService.GetCurrentSlot()}");
            _loggingService.LogInformation($"Current Model Number: {_plcService.GetCurrentModelNumber()}");
            _loggingService.LogInformation($"Current PID: {_plcService.ReadPID()}");

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

        private async Task<bool> SendDataExecuteAsync()
        {
            bool finalResult = false;
            if (IsInChooseEBRMode) return false;
            
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
                    int currentPCBQty = await _dataExecuter.GetPackQty();
                    int totalPCBQty = await _dataExecuter.GetMagazineQty();

                    _loggingService.LogInformation($"Current PCB Qty: {currentPCBQty}, Total PCB Qty: {totalPCBQty}, Magazine Qty: {MagazineQty}");
                    if (MagazineQty != 0 && MagazineQty != totalPCBQty && currentPCBQty == MagazineQty)
                    {
                        if (MagazineQty > totalPCBQty && currentPCBQty >= (totalPCBQty - 1))
                            MessageBox.Show("Số lượng PCB trên hệ thống HMES đang nhỏ hơn số lượng cài đặt", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        await _dataExecuter.PrintManual();
                    }
                }
                else
                {
                    if(_configService.Config.IsWOMode)
                    {
                        result = await SendInWOMode();
                    }
                    else
                    {
                        result = await SendInEBRMode();
                    }                    
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
                else
                {
                    finalResult = true;
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
            return finalResult;
        }

        private async Task<ProcessResult> SendInWOMode()
        {            
            ProcessResult result = new ProcessResult();
            if (string.IsNullOrEmpty(workOrder)) return result;
            if (string.IsNullOrEmpty(SelectedEBR))
            {
                MessageBox.Show("Vui lòng chọn Part No trước khi scan", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                result = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNo, pID);
            }
            else
            {
                if (workOrder != SelectedEBR)
                {
                    _loggingService.LogInformation($"PartNo miss match: {workOrder} != {SelectedEBR}");
                    result = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNo, pID);
                }
                else
                {
                    result = await _dataExecuter.ProcessDataAsync(workOrder, partNo, pID);
                }
            }
            return result;
        }

        private async Task<ProcessResult> SendInEBRMode()
        {
            ProcessResult result = new ProcessResult();
            if(string.IsNullOrEmpty(partNo)) return result;
            if (partNo != SelectedEBR && !string.IsNullOrEmpty(SelectedEBR))
            {
                _loggingService.LogInformation($"PartNo miss match: {partNo} != {SelectedEBR}");
                int PackQty = await _dataExecuter.GetPackQty();
                if (PackQty == 0)
                {                    
                    result = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNo, pID);
                }
                else
                {
                    result = await _dataExecuter.ProcessDataAsync(workOrder, partNo, pID);
                }
            }
            else
            {
                result = await _dataExecuter.ProcessDataAsync(workOrder, partNo, pID);
            }

            int currentPCBQty = await _dataExecuter.GetPackQty();
            int totalPCBQty = await _dataExecuter.GetMagazineQty();

            if(MagazineQty !=0 && MagazineQty != totalPCBQty && currentPCBQty == MagazineQty)
            {
                if(MagazineQty>totalPCBQty && currentPCBQty >= (totalPCBQty-1)) 
                    MessageBox.Show("Số lượng PCB trên hệ thống HMES đang nhỏ hơn số lượng cài đặt", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await _dataExecuter.PrintManual();
            }

            return result;
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
                    _loggingService.LogInformation($"ScanOut NG:{pid},{PartNo},{WorkOrder},{Result},{ResultMessage}:{sw.ElapsedMilliseconds}ms");
                    sw.Stop();
                    sw.Reset();
                    return false;
                }
                if (sw.ElapsedMilliseconds > 5000)
                {
                    _loggingService.LogError($"Timeout to get scanout PID {pid} result: {_autoScanOutUI.ReadPID()} - {_autoScanOutUI.ReadMessage()}:{sw.ElapsedMilliseconds}ms");
                    sw.Stop();
                    sw.Reset();
                    return false;
                }
                await Task.Delay(50); // Delay to avoid busy waiting
            }
            
            
            try
            {
                PID = _autoScanOutUI.ReadPID();
                WorkOrder = _autoScanOutUI.ReadWO();
                PartNo = _autoScanOutUI.ReadEBR();
                Result = _autoScanOutUI.ReadResult();
                ResultMessage = _autoScanOutUI.ReadMessage(); 

                _loggingService.LogInformation($"Data from ScanOut GUI:{PID},{PartNo},{WorkOrder},{Result}:{sw.ElapsedMilliseconds}ms");

                if (Result == "OK")
                {                    
                    //_loggingService.LogInformation($"Scan out OK: PID: {PID}, WorkOrder: {WorkOrder}, PartNo: {PartNo}, Result: {Result}:{sw.ElapsedMilliseconds}ms");                    
                    sw.Stop();
                    sw.Reset();
                    return true;
                }
                else
                {
                    //_loggingService.LogError($"Scan out NG: PID: {PID}, WorkOrder: {WorkOrder}, PartNo: {PartNo}, Result: {Result}, {ResultMessage}:{sw.ElapsedMilliseconds}ms");
                    sw.Stop();
                    sw.Reset();
                    return false;
                }
            }
            catch (Exception ex)
            {
                //_loggingService.LogError($"Failed to read scan out result: {ex.Message}:{sw.ElapsedMilliseconds}ms");
                sw.Stop();
                sw.Reset();
                return false;
            }

        }

        public async Task<bool> ReadScanOutResultByLog(string pid)
        {
            if(_resultService == null)
            {
                _loggingService.LogError("Result service is not initialized.");
                return false;
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var result = await _resultService.RequestResultAsync(pid.Trim(),3000);

            //PID = result.PID;
            //WorkOrder = result.WorkOrder;
            //PartNo = result.Model;
            //Result = result.Result;
            _loggingService.LogInformation($"Data from ScanOut LOG:{result.PID},{result.Model.TrimEnd('.')},{result.WorkOrder},{result.Result}:{sw.ElapsedMilliseconds}ms");
            sw.Stop();
            sw.Reset();
            return result.Result == "OK" ? true : false;
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
            if (_plcService == null)
            {
                _loggingService.LogError("PLC service is not initialized.");
                return;
            }
            if (!_isPLCConnected)
            {
                _loggingService.LogError("PLC is not connected.");
                return;
            }
            await _plcService.SetPassSignalAsync();
            _loggingService.LogInformation("Reading PCB info from PLC...");
            int modelNumber = _plcService.GetCurrentModelNumber();
            int CurrentSlot = _plcService.GetCurrentSlot();
            int TotalSlot = _plcService.GetTotalSlot();
            int TraySlot = _plcService.GetTraySlot();
            int TotalTray = _plcService.GetTotalTray();
            int CurrentTray = _plcService.GetCurrentTray();

            int currentPCBQty = CurrentSlot + CurrentTray * TraySlot;
            int totalPCBQty = TraySlot * TotalTray;
            _loggingService.LogInformation($"DataReading from PLC: Model Number: {modelNumber}, Current Slot: {CurrentSlot}, Total Slot: {TotalSlot}");
            PCBLocation = $"{currentPCBQty}/{totalPCBQty}";
            MagazineQty = TotalSlot;
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

        void PlayNgSound()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "scan_fail.wav");
                var player = new SoundPlayer(filePath); // đường dẫn tới file âm thanh NG
                player.Play(); // hoặc PlaySync() nếu muốn đợi âm thanh phát xong
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Lỗi phát âm thanh: " + ex.Message);
            }
        }

        void PlayBeepSound()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ScannerBeepSound.wav");
                var player = new SoundPlayer(filePath); // đường dẫn tới file âm thanh NG
                player.Play(); // hoặc PlaySync() nếu muốn đợi âm thanh phát xong
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Lỗi phát âm thanh: " + ex.Message);
            }
        }

    }
}
