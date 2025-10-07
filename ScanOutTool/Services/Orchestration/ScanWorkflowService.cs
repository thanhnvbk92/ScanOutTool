using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScanOutTool.Services.Orchestration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SerialProxyLib;

namespace ScanOutTool.Services.Orchestration
{
    /// <summary>
    /// Implementation của ScanWorkflowService - tách toàn bộ business logic ra khỏi ViewModel
    /// 
    /// RESPONSIBILITY SEPARATION:
    /// - ScanWorkflowService: Orchestrates workflow, manages UI integration, handles serial data processing
    /// - HMESService: Handles HMES integration logic, RunMode routing to Database/Web
    /// - DataExecuter: Actual Database and Web communication
    /// </summary>
    public class ScanWorkflowService : IScanWorkflowService
    {
        private readonly ILogger<ScanWorkflowService> _logger;
        private readonly IConfigService _configService;
        private readonly IPLCServiceFactory _plcFactory;
        private readonly IBlockRFService _blockRFService;
        private readonly IHMESService _hmesService;
        private readonly SerialDataProcessor _serialDataProcessor; // ✅ NEW
        private readonly ScannerFeedbackService _feedbackService; // ✅ NEW

        private SerialProxyManager? _serialProxyManager;
        private IPLCService? _plcService;
        private readonly IAutoScanOutUI _autoScanOutUI; // ✅ CHANGED: Inject instead of creating
        private CancellationTokenSource _cancellationTokenSource = new();
        private Task? _serialProxyTask; // ✅ NEW: Track background task

        private WorkflowStatus _currentStatus = WorkflowStatus.Stopped;
        private bool _disposed;

        public event EventHandler<WorkflowStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ScanDataReceivedEventArgs>? ScanDataReceived;
        public event EventHandler<ErrorOccurredEventArgs>? ErrorOccurred;

        public bool IsRunning => _currentStatus == WorkflowStatus.Running;
        public WorkflowStatus CurrentStatus => _currentStatus;

        public ScanWorkflowService(
            ILogger<ScanWorkflowService> logger,
            IConfigService configService,
            IPLCServiceFactory plcFactory,
            IBlockRFService blockRFService,
            IHMESService hmesService,
            SerialDataProcessor serialDataProcessor,
            ScannerFeedbackService feedbackService,
            IAutoScanOutUI autoScanOutUI) // ✅ CHANGED: Inject IAutoScanOutUI
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _plcFactory = plcFactory ?? throw new ArgumentNullException(nameof(plcFactory));
            _blockRFService = blockRFService ?? throw new ArgumentNullException(nameof(blockRFService));
            _hmesService = hmesService ?? throw new ArgumentNullException(nameof(hmesService));
            _serialDataProcessor = serialDataProcessor ?? throw new ArgumentNullException(nameof(serialDataProcessor));
            _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
            _autoScanOutUI = autoScanOutUI ?? throw new ArgumentNullException(nameof(autoScanOutUI)); // ✅ CHANGED
        }

        public async Task<WorkflowResult> StartAsync(CancellationToken cancellationToken = default)
        {
            // ✅ IMPROVED: Better state checking and reset on error
            if (_currentStatus == WorkflowStatus.Starting)
            {
                return WorkflowResult.CreateFailure("Workflow is currently starting, please wait");
            }
            
            if (_currentStatus == WorkflowStatus.Running)
            {
                return WorkflowResult.CreateFailure("Workflow is already running");
            }

            // ✅ IMPROVED: Reset error state to allow retry
            if (_currentStatus == WorkflowStatus.Error)
            {
                _logger.LogInformation("Resetting workflow from error state to allow retry");
                ChangeStatus(WorkflowStatus.Stopped, "Reset from error state");
            }

            if (_currentStatus != WorkflowStatus.Stopped)
            {
                return WorkflowResult.CreateFailure($"Cannot start workflow from current state: {_currentStatus}");
            }

            // ✅ NEW: Reset cancellation token for fresh start
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                _logger.LogInformation("Reset cancellation token for fresh start");
            }

            try
            {
                _logger.LogInformation("=========================== STARTING WORKFLOW ===========================");
                ChangeStatus(WorkflowStatus.Starting, "Starting workflow...");

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token);

                // ✅ IMPROVED: Start services one by one with detailed error reporting
                var startupResults = new List<(string Service, bool Success, string Error)>();

                // Start Serial Proxy
                try
                {
                    _logger.LogInformation("[1/3] Starting Serial Proxy Service...");
                    await StartSerialProxyAsync(linkedCts.Token).ConfigureAwait(false);
                    startupResults.Add(("SerialProxy", true, ""));
                    _logger.LogInformation("[1/3] Serial Proxy Service: SUCCESS");
                }
                catch (Exception ex)
                {
                    var error = $"Serial Proxy startup failed: {ex.Message}";
                    startupResults.Add(("SerialProxy", false, error));
                    _logger.LogError("[1/3] Serial Proxy Service: FAILED - {Error}", error);
                    
                    // ✅ IMPROVED: Reset to stopped state on failure
                    ChangeStatus(WorkflowStatus.Stopped, "Failed to start - ready for retry");
                    throw new InvalidOperationException(error, ex);
                }

                // Start PLC Service (optional)
                try
                {
                    _logger.LogInformation("[2/3] Starting PLC Service...");
                    await StartPLCServiceAsync(linkedCts.Token).ConfigureAwait(false);
                    startupResults.Add(("PLC", true, ""));
                    _logger.LogInformation("[2/3] PLC Service: SUCCESS");
                }
                catch (Exception ex)
                {
                    var error = $"PLC Service startup failed: {ex.Message}";
                    startupResults.Add(("PLC", false, error));
                    _logger.LogError("[2/3] PLC Service: FAILED - {Error}", error);
                    
                    // ✅ IMPROVED: PLC is always optional now, just log the failure
                    _logger.LogWarning("[2/3] PLC Service failed but continuing (PLC is optional)");
                }

                // Start ScanOut UI
                try
                {
                    _logger.LogInformation("[3/3] Starting ScanOut UI Service...");
                    await StartScanOutUIAsync(linkedCts.Token).ConfigureAwait(false);
                    startupResults.Add(("ScanOutUI", true, ""));
                    _logger.LogInformation("[3/3] ScanOut UI Service: SUCCESS");
                }
                catch (Exception ex)
                {
                    var error = $"ScanOut UI startup failed: {ex.Message}";
                    startupResults.Add(("ScanOutUI", false, error));
                    _logger.LogError("[3/3] ScanOut UI Service: FAILED - {Error}", error);
                    
                    // ✅ IMPROVED: Reset to stopped state on failure
                    ChangeStatus(WorkflowStatus.Stopped, "Failed to start - ready for retry");
                    throw new InvalidOperationException(error, ex);
                }

                // ✅ NEW: Initialize HMES Web session if needed
                try
                {
                    _logger.LogInformation("[4/4] Initializing HMES Web Session...");
                    var hmesService = _hmesService as HMESService;
                    if (hmesService != null)
                    {
                        var hmesSuccess = await hmesService.InitializeWebSessionAsync();
                        if (hmesSuccess)
                        {
                            _logger.LogInformation("[4/4] HMES Web Session: SUCCESS");
                        }
                        else
                        {
                            _logger.LogWarning("[4/4] HMES Web Session: FAILED (continuing anyway)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[4/4] HMES Web Session initialization failed (continuing anyway)");
                }

                ChangeStatus(WorkflowStatus.Running, "Workflow started successfully");
                
                // Log summary
                var successCount = startupResults.Count(r => r.Success);
                var totalCount = startupResults.Count;
                _logger.LogInformation("========================= WORKFLOW STARTED SUCCESSFULLY =========================");
                _logger.LogInformation("Services Summary: {SuccessCount}/{TotalCount} started successfully", successCount, totalCount);
                
                foreach (var result in startupResults)
                {
                    var status = result.Success ? "SUCCESS" : "FAILED";
                    _logger.LogInformation("   {Service}: {Status}", result.Service, status);
                }
                
                _logger.LogInformation("Workflow is now RUNNING and ready to process scan data");
                _logger.LogInformation("==============================================================================");

                return WorkflowResult.CreateSuccess($"Workflow started successfully ({successCount}/{totalCount} services)");
            }
            catch (Exception ex)
            {
                _logger.LogError("========================= WORKFLOW STARTUP FAILED =========================");
                _logger.LogError("Error: {ErrorMessage}", ex.Message);
                _logger.LogError("Exception Type: {ExceptionType}", ex.GetType().Name);
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner Exception: {InnerException}", ex.InnerException.Message);
                }
                _logger.LogError("==============================================================================");
                
                // ✅ IMPROVED: Set to stopped state instead of error to allow retry
                ChangeStatus(WorkflowStatus.Stopped, "Startup failed - ready for retry");
                return WorkflowResult.CreateFailure($"Failed to start workflow: {ex.Message}", ex);
            }
        }

        public async Task<WorkflowResult> StopAsync(CancellationToken cancellationToken = default)
        {
            if (_currentStatus == WorkflowStatus.Stopped)
            {
                return WorkflowResult.CreateSuccess("Workflow is already stopped");
            }

            try
            {
                _logger.LogInformation("=========================== STOPPING WORKFLOW ===========================");
                ChangeStatus(WorkflowStatus.Stopping, "Stopping workflow...");

                // ✅ IMPROVED: Cancel internal operations
                _cancellationTokenSource.Cancel();

                // Stop services
                var stopTasks = new[]
                {
                    StopServiceSafely(() => Task.Run(() => 
                    {
                        _serialProxyManager?.Stop();
                        _serialProxyTask?.Wait(TimeSpan.FromSeconds(5));
                    })),
                    StopServiceSafely(() => Task.Run(() => _plcService?.Dispose())),
                    StopServiceSafely(() => Task.CompletedTask) // ScanOutUI cleanup if needed
                };

                await Task.WhenAll(stopTasks).ConfigureAwait(false);

                // ✅ UPDATED: Clean up references (except injected services)
                _serialProxyManager = null;
                _serialProxyTask = null;
                _plcService = null;
                // Don't null out _autoScanOutUI - it's an injected dependency

                ChangeStatus(WorkflowStatus.Stopped, "Workflow stopped successfully");
                _logger.LogInformation("========================= WORKFLOW STOPPED SUCCESSFULLY =========================");

                return WorkflowResult.CreateSuccess("Workflow stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ScanWorkflowService shutdown");
                ChangeStatus(WorkflowStatus.Error, $"Error during stop: {ex.Message}");
                return WorkflowResult.CreateFailure("Error during shutdown", ex);
            }
        }

        private async Task StartSerialProxyAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("StartSerialProxyAsync: Method entered");
                
                // ✅ NEW: Stop existing proxy if running
                if (_serialProxyManager != null)
                {
                    _logger.LogInformation("StartSerialProxyAsync: Stopping existing proxy...");
                    _serialProxyManager.Stop();
                    _serialProxyTask?.Wait(TimeSpan.FromSeconds(2)); // Wait for graceful shutdown
                    _serialProxyManager = null;
                    _serialProxyTask = null;
                }
                
                var comDevice = _configService.Config.ScannerPortSettingVM;
                var comApp = _configService.Config.ShopFloorPortSettingVM;

                if (comDevice == null || comApp == null)
                {
                    throw new InvalidOperationException("COM device or app settings are not configured. Please check Settings.");
                }

                if (string.IsNullOrEmpty(comDevice.SelectedPort) || string.IsNullOrEmpty(comApp.SelectedPort))
                {
                    throw new InvalidOperationException($"COM ports not selected. Device: {comDevice.SelectedPort}, App: {comApp.SelectedPort}");
                }

                // ✅ NEW: Validate COM ports exist before starting
                _logger.LogInformation("StartSerialProxyAsync: Checking available COM ports...");
                var availablePorts = System.IO.Ports.SerialPort.GetPortNames();
                if (!availablePorts.Contains(comDevice.SelectedPort))
                {
                    throw new InvalidOperationException($"Scanner COM port '{comDevice.SelectedPort}' is not available. Available ports: {string.Join(", ", availablePorts)}");
                }

                _logger.LogInformation("Starting Serial Proxy: Device={DevicePort}, App={AppPort}, BaudRate={BaudRate}",
                    comDevice.SelectedPort, comApp.SelectedPort, comDevice.SelectedBaudRate);
                _logger.LogInformation("Available COM ports: {AvailablePorts}", string.Join(", ", availablePorts));

                _logger.LogInformation("StartSerialProxyAsync: Creating SerialProxyManager...");
                
                // ✅ Need to get ILoggingService for SerialProxyManager via constructor injection
                var serviceProvider = App.Services;
                var loggingService = serviceProvider.GetRequiredService<ILoggingService>();
                
                _serialProxyManager = new SerialProxyManager
                {
                    Logger = new SerilogProxyLogger(loggingService),
                    WaitForGuiProcessAsync = ProcessSerialDataAsync
                };

                _logger.LogInformation("StartSerialProxyAsync: Subscribing to OnDataForwardingAsync event...");
                _serialProxyManager.OnDataForwardingAsync += OnSerialDataForwarding;

                _logger.LogInformation("StartSerialProxyAsync: About to call StartProxy...");
                
                // ✅ FIXED: Use internal cancellation token instead of passed token for initialization delay
                _serialProxyTask = Task.Run(async () =>
                {
                    try
                    {
                        await _serialProxyManager.StartProxy(
                            comDevice.SelectedPort, 
                            comApp.SelectedPort, 
                            comDevice.SelectedBaudRate, 
                            comDevice.SelectedParity, 
                            comDevice.SelectedDataBits, 
                            comDevice.SelectedStopBits,
                            _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "SerialProxyManager background task failed");
                    }
                });

                // ✅ Give it a moment to initialize - use internal token
                await Task.Delay(500, _cancellationTokenSource.Token);

                _logger.LogInformation("StartSerialProxyAsync: StartProxy started in background");
                _logger.LogInformation("Serial proxy started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartSerialProxyAsync: Exception occurred");
                throw new InvalidOperationException($"Serial proxy startup failed: {ex.Message}", ex);
            }
        }

        private async Task StartPLCServiceAsync(CancellationToken cancellationToken)
        {
            var config = _configService.Config;
            
            // ✅ NEW: Check if PLC is enabled
            if (!config.UsePLC)
            {
                _logger.LogInformation("PLC Service skipped (Use PLC disabled)");
                return;
            }
            
            if (!config.IsRobotMode)
            {
                _logger.LogInformation("PLC Service skipped (Robot mode disabled)");
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(config.PLCIP) || config.PLCPort == 0)
                {
                    throw new InvalidOperationException($"PLC configuration is invalid. IP: '{config.PLCIP}', Port: {config.PLCPort}");
                }

                _logger.LogInformation("Starting PLC Service: IP={PLCIP}, Port={PLCPort}", config.PLCIP, config.PLCPort);

                _plcService = _plcFactory.Create(config.PLCIP, config.PLCPort, true);
                
                _plcService.OnConnectionChanged += (connected) =>
                {
                    var message = connected ? "PLC connected" : "PLC disconnected";
                    _logger.LogInformation("{Message}", message);
                };

                _logger.LogInformation("PLC service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PLC service initialization failed");
                throw new InvalidOperationException($"PLC service initialization failed: {ex.Message}", ex);
            }
        }

        private async Task StartScanOutUIAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting ScanOut UI...");
                
                // ✅ FIXED: Just validate the injected service instead of creating new one
                if (!_autoScanOutUI.IsScanoutUI())
                {
                    throw new InvalidOperationException("ScanOut application is not running or not accessible");
                }
                
                _logger.LogInformation("ScanOut UI initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScanOut UI initialization failed");
                throw new InvalidOperationException($"ScanOut UI initialization failed: {ex.Message}", ex);
            }
        }

        private async Task<bool> ProcessSerialDataAsync(string sentData)
        {
            try
            {
                // ✅ DELEGATED: Use SerialDataProcessor instead of handling logic here
                var result = await _serialDataProcessor.ProcessDataAsync(sentData);
                
                if (!result.Success)
                {
                    _logger.LogError("Serial data processing failed: {Error}", result.ErrorMessage);
                    return false;
                }

                if (result.IsSpecialCommand)
                {
                    _logger.LogInformation("Special command processed: {Message}", result.Message);
                    return true;
                }

                // Send feedback to scanner
                await _feedbackService.SendFeedbackAsync(_serialProxyManager, result.ScanSuccess);

                // Notify ViewModel with result data
                ScanDataReceived?.Invoke(this, new ScanDataReceivedEventArgs
                {
                    PID = result.PID,
                    WorkOrder = result.WorkOrder,
                    PartNumber = result.PartNumber,
                    Result = result.Result,
                    Message = result.Message
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing serial data: {Data}", sentData);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
                {
                    ErrorMessage = $"Error processing serial data: {ex.Message}",
                    Exception = ex,
                    Severity = ErrorSeverity.Error
                });
                return false;
            }
        }

        /// <summary>
        /// ✅ WORKFLOW LAYER: Contains App State logic and calls appropriate HMES service methods
        /// </summary>
        private async Task SendToHMESAsync(string pid)
        {
            try
            {
                var config = _configService.Config;
                _logger.LogInformation("SendToHMESAsync: Sending PID to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                
                bool success = false;

                // ✅ WORKFLOW LAYER: App State logic determines which service method to call
                switch (config.SelectedRunMode)
                {
                    case AppConfig.RunMode.ScanOutOnly:
                        _logger.LogInformation("SendToHMESAsync: ScanOutOnly mode - calling SendToDatabaseAsync");
                        if (_autoScanOutUI != null)
                        {
                            var workOrder = _autoScanOutUI.ReadWO();
                            var partNumber = _autoScanOutUI.ReadEBR();
                            var result = _autoScanOutUI.ReadResult();
                            var message = _autoScanOutUI.ReadMessage();
                            
                            success = await _hmesService.SendToDatabaseAsync(pid, workOrder, partNumber, result, message);
                        }
                        else
                        {
                            _logger.LogWarning("SendToHMESAsync: AutoScanOutUI not available for ScanOutOnly mode");
                        }
                        break;

                    case AppConfig.RunMode.RescanOnly:
                        _logger.LogInformation("SendToHMESAsync: RescanOnly mode - calling SendToWebAsync");
                        success = await _hmesService.SendToWebAsync(pid);
                        break;

                    case AppConfig.RunMode.ScanOut_Rescan:
                        _logger.LogInformation("SendToHMESAsync: ScanOut_Rescan mode - calling SendToDatabaseAndWebAsync");
                        if (_autoScanOutUI != null)
                        {
                            var workOrder = _autoScanOutUI.ReadWO();
                            var partNumber = _autoScanOutUI.ReadEBR();
                            var result = _autoScanOutUI.ReadResult();
                            var message = _autoScanOutUI.ReadMessage();
                            
                            success = await _hmesService.SendToDatabaseAndWebAsync(pid, workOrder, partNumber, result, message);
                        }
                        else
                        {
                            // Fallback to web only if AutoScanOutUI not available
                            _logger.LogWarning("SendToHMESAsync: AutoScanOutUI not available, falling back to Web only");
                            success = await _hmesService.SendToWebAsync(pid);
                        }
                        break;

                    default:
                        _logger.LogWarning("SendToHMESAsync: Unknown RunMode {RunMode}, defaulting to ScanOut_Rescan", config.SelectedRunMode);
                        goto case AppConfig.RunMode.ScanOut_Rescan;
                }
                
                if (success)
                {
                    _logger.LogInformation("SendToHMESAsync: Successfully sent to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                }
                else
                {
                    _logger.LogWarning("SendToHMESAsync: Failed to send to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send data to HMES for PID: {PID}", pid);
                // Don't throw - HMES failure shouldn't stop the workflow
            }
        }

        /// <summary>
        /// ✅ RESTORED: Send feedback to scanner port based on scan result
        /// </summary>
        private async Task SendFeedbackToScannerAsync(bool isOK)
        {
            try
            {
                var config = _configService.Config;
                
                // Check if feedback is enabled
                if (!config.EnableScannerFeedback)
                {
                    _logger.LogDebug("Scanner feedback is disabled in configuration");
                    return;
                }

                if (_serialProxyManager == null)
                {
                    _logger.LogWarning("SerialProxyManager is not initialized, cannot send feedback");
                    return;
                }

                // Add configurable delay before sending feedback
                if (config.FeedbackDelayMs > 0)
                {
                    await Task.Delay(config.FeedbackDelayMs).ConfigureAwait(false);
                }

                // Use configured feedback messages
                string feedbackMessage = isOK ? config.OkFeedbackMessage : config.NgFeedbackMessage;
                
                // Add carriage return for proper serial communication
                var feedbackData = feedbackMessage + "\r";

                // Send feedback to scanner (device port)
                await _serialProxyManager.SendToDeviceAsync(feedbackData).ConfigureAwait(false);

                _logger.LogInformation("Sent feedback to scanner: {Feedback} (Result: {Result})", 
                    feedbackMessage, isOK ? "OK" : "NG");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending feedback to scanner");
                ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
                {
                    ErrorMessage = $"Failed to send feedback to scanner: {ex.Message}",
                    Exception = ex,
                    Severity = ErrorSeverity.Warning
                });
            }
        }

        private async Task OnSerialDataForwarding(object sender, SerialDataEventArgs e)
        {
            try
            {
                _logger.LogInformation("[Event] {Source} sent: {Data}", 
                    e.FromDevice ? "Device" : "App", e.Data);

                // Block RF check
                if (_configService.Config.IsBlockRFMode)
                {
                    var rfInfo = await _blockRFService.IsBlock(e.Data).ConfigureAwait(false);
                    if (rfInfo != null)
                    {
                        e.Cancel = true;
                        
                        // ✅ DELEGATED: Use ScannerFeedbackService for feedback
                        await _feedbackService.SendNGFeedbackAsync(_serialProxyManager, 
                            $"PID blocked due to NG RF at jig {rfInfo.MachineIP}");
                        
                        ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
                        {
                            ErrorMessage = $"PID {e.Data} is blocked due to NG RF at jig {rfInfo.MachineIP}",
                            Severity = ErrorSeverity.Warning
                        });
                        return;
                    }
                }

                // Handle special commands
                await HandleSpecialCommands(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in serial data forwarding");
                ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
                {
                    ErrorMessage = $"Serial forwarding error: {ex.Message}",
                    Exception = ex,
                    Severity = ErrorSeverity.Error
                });
            }
        }

        private async Task HandleSpecialCommands(SerialDataEventArgs e)
        {
            var data = e.Data.ToUpper();
            
            if (data.Contains("CHOOSE"))
            {
                // Handle choose EBR logic
                e.Cancel = true;
                _logger.LogInformation("Choose EBR mode activated");
            }
            else if (data.Contains("TRACE"))
            {
                // Handle trace logic
                e.Cancel = true;
                _logger.LogInformation("Trace command received");
            }
            else if (data.Contains("CLEAR"))
            {
                // Handle clear logic
                e.Cancel = true;
                _logger.LogInformation("Clear command received");
            }
            else if (!data.Contains("CLEAR") && !data.Contains("TRACE") && 
                     e.Data.Trim().Length != 11 && e.Data.Trim().Length != 22)
            {
                e.Cancel = true;
                _logger.LogInformation("Invalid data length: {Length}", e.Data.Trim().Length);
                
                // ✅ DELEGATED: Use ScannerFeedbackService for feedback
                await _feedbackService.SendNGFeedbackAsync(_serialProxyManager, "Invalid data length");
            }
        }

        /// <summary>
        /// ✅ IMPROVED: Return nullable bool to indicate success/failure/timeout
        /// </summary>
        private async Task<bool?> ReadScanOutResultAsync(string pid)
        {
            if (_autoScanOutUI == null)
            {
                _logger.LogError("AutoScanOutUI is not initialized");
                return null;
            }

            var cleanPid = pid.Trim();
            var timeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout)
            {
                if (cleanPid == _autoScanOutUI.ReadPID())
                {
                    var result = _autoScanOutUI.ReadResult();
                    _logger.LogInformation("Scan result for PID {PID}: {Result}", cleanPid, result);
                    return result == "OK";
                }

                if (_autoScanOutUI.ReadPID().Contains("PLZ Read below Message and Clear") && 
                    _autoScanOutUI.ReadMessage().Contains(cleanPid))
                {
                    _logger.LogInformation("Scan failed for PID {PID}: {Message}", cleanPid, _autoScanOutUI.ReadMessage());
                    return false;
                }

                if (_autoScanOutUI.ReadMessage().Contains($"Scan Data : [{cleanPid}]") )
                {
                    _logger.LogInformation("Scan failed for PID {PID}: {Message}", cleanPid, _autoScanOutUI.ReadMessage());
                    return false;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            _logger.LogError("Timeout reading scan result for PID: {PID}", cleanPid);
            return null; // Timeout case
        }

        private void ChangeStatus(WorkflowStatus newStatus, string? message = null)
        {
            var previousStatus = _currentStatus;
            _currentStatus = newStatus;

            StatusChanged?.Invoke(this, new WorkflowStatusChangedEventArgs
            {
                PreviousStatus = previousStatus,
                CurrentStatus = newStatus,
                Message = message
            });
        }

        private async Task StopServiceSafely(Func<Task> stopAction)
        {
            try
            {
                await stopAction().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping service");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _logger.LogInformation("=========================== DISPOSING WORKFLOW SERVICE ===========================");
                
                _cancellationTokenSource.Cancel();
                
                // Stop services
                _serialProxyManager?.Stop();
                _serialProxyTask?.Wait(TimeSpan.FromSeconds(2));
                _plcService?.Dispose();
                
                // Dispose resources
                _cancellationTokenSource.Dispose();
                
                _logger.LogInformation("ScanWorkflowService disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during disposal");
            }

            _disposed = true;
        }

        // Helper class for logging
        private class SerilogProxyLogger : IProxyLogger
        {
            private readonly ILoggingService _logger;
            public SerilogProxyLogger(ILoggingService logger) => _logger = logger;
            public void Log(string message) => _logger.LogInformation(message);
        }
    }
}