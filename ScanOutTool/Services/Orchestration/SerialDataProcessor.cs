using Microsoft.Extensions.Logging;
using ScanOutTool.Services.Orchestration;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services.Orchestration
{
    /// <summary>
    /// Handles serial data processing logic - extracted from ScanWorkflowService
    /// </summary>
    public class SerialDataProcessor
    {
        private readonly ILogger<SerialDataProcessor> _logger;
        private readonly IConfigService _configService;
        private readonly IHMESService _hmesService;
        private readonly IAutoScanOutUI _autoScanOutUI;

        public SerialDataProcessor(
            ILogger<SerialDataProcessor> logger,
            IConfigService configService,
            IHMESService hmesService,
            IAutoScanOutUI autoScanOutUI)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _hmesService = hmesService ?? throw new ArgumentNullException(nameof(hmesService));
            _autoScanOutUI = autoScanOutUI ?? throw new ArgumentNullException(nameof(autoScanOutUI));
        }

        /// <summary>
        /// Process incoming serial data based on current RunMode
        /// </summary>
        public async Task<SerialProcessResult> ProcessDataAsync(string sentData)
        {
            try
            {
                _logger.LogInformation("Processing serial data: {Data}", sentData);
                
                // Handle special commands
                if (sentData.Contains("CLEAR") || sentData.Contains("TRACE"))
                {
                    _logger.LogInformation("Special command detected: {Data}", sentData);
                    return SerialProcessResult.CreateSpecialCommand(sentData);
                }

                if (!_autoScanOutUI.IsScanoutUI())
                {
                    _logger.LogWarning("ScanOut UI not available");
                    return SerialProcessResult.CreateError("ScanOut UI not available");
                }

                var config = _configService.Config;
                
                // ? NEW: Process based on RunMode with proper feedback timing
                switch (config.SelectedRunMode)
                {
                    case AppConfig.RunMode.ScanOutOnly:
                        return await ProcessScanOutOnlyAsync(sentData);
                        
                    case AppConfig.RunMode.RescanOnly:
                        return await ProcessRescanOnlyAsync(sentData);
                        
                    case AppConfig.RunMode.ScanOut_Rescan:
                        return await ProcessScanOutAndRescanAsync(sentData);
                        
                    default:
                        _logger.LogWarning("Unknown RunMode: {RunMode}, defaulting to ScanOut_Rescan", config.SelectedRunMode);
                        return await ProcessScanOutAndRescanAsync(sentData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing serial data: {Data}", sentData);
                return SerialProcessResult.CreateError($"Processing error: {ex.Message}");
            }
        }

        /// <summary>
        /// ? NEW: Process ScanOutOnly - Send feedback after database validation
        /// </summary>
        private async Task<SerialProcessResult> ProcessScanOutOnlyAsync(string sentData)
        {
            try
            {
                // Read scan result from ScanOut UI
                var scanResult = await ReadScanOutResultAsync(sentData);
                
                if (scanResult == null)
                {
                    // Scan timeout/error
                    return SerialProcessResult.CreateScanResult(
                        pid: sentData,
                        workOrder: "",
                        partNumber: "",
                        result: "NG",
                        message: "Scan timeout",
                        scanSuccess: false,
                        hmesSuccess: false,
                        shouldSendFeedback: true,
                        feedbackResult: false  // NG feedback
                    );
                }

                // Get scan data
                var workOrder = _autoScanOutUI.ReadWO();
                var partNumber = _autoScanOutUI.ReadEBR();
                var result = _autoScanOutUI.ReadResult();
                var message = _autoScanOutUI.ReadMessage();

                // ? KEY: Only send to HMES if scan is OK, then feedback based on HMES result
                bool hmesSuccess = false;
                bool shouldSendFeedback = true;
                bool feedbackResult = false;

                if (scanResult.Value) // Scan OK
                {
                    // Send to HMES Database
                    hmesSuccess = await _hmesService.SendToDatabaseAsync(sentData, workOrder, partNumber, result, message);
                    feedbackResult = hmesSuccess; // Feedback based on HMES result
                }
                else
                {
                    // Scan NG - no HMES, feedback NG
                    feedbackResult = false;
                }

                return SerialProcessResult.CreateScanResult(
                    pid: _autoScanOutUI.ReadPID(),
                    workOrder: workOrder,
                    partNumber: partNumber,
                    result: result,
                    message: message,
                    scanSuccess: scanResult.Value,
                    hmesSuccess: hmesSuccess,
                    shouldSendFeedback: shouldSendFeedback,
                    feedbackResult: feedbackResult
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessScanOutOnlyAsync");
                return SerialProcessResult.CreateError($"ScanOutOnly error: {ex.Message}");
            }
        }

        /// <summary>
        /// ? NEW: Process RescanOnly - Send feedback after HMES Web success
        /// </summary>
        private async Task<SerialProcessResult> ProcessRescanOnlyAsync(string sentData)
        {
            try
            {
                // For rescan mode, send directly to HMES Web
                var hmesSuccess = await _hmesService.SendToWebAsync(sentData);

                return SerialProcessResult.CreateRescanResult(
                    pid: sentData,
                    hmesSuccess: hmesSuccess,
                    shouldSendFeedback: true,
                    feedbackResult: hmesSuccess  // ? KEY: Feedback based on HMES Web result
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessRescanOnlyAsync");
                return SerialProcessResult.CreateError($"RescanOnly error: {ex.Message}");
            }
        }

        /// <summary>
        /// ? NEW: Process ScanOut + Rescan - Send feedback after both operations complete
        /// </summary>
        private async Task<SerialProcessResult> ProcessScanOutAndRescanAsync(string sentData)
        {
            try
            {
                // Read scan result from ScanOut UI
                var scanResult = await ReadScanOutResultAsync(sentData);
                
                if (scanResult == null)
                {
                    // Scan timeout/error
                    return SerialProcessResult.CreateScanResult(
                        pid: sentData,
                        workOrder: "",
                        partNumber: "",
                        result: "NG",
                        message: "Scan timeout",
                        scanSuccess: false,
                        hmesSuccess: false,
                        shouldSendFeedback: true,
                        feedbackResult: false
                    );
                }

                // Get scan data
                var workOrder = _autoScanOutUI.ReadWO();
                var partNumber = _autoScanOutUI.ReadEBR();
                var result = _autoScanOutUI.ReadResult();
                var message = _autoScanOutUI.ReadMessage();

                bool hmesSuccess = false;
                bool shouldSendFeedback = true;
                bool feedbackResult = false;

                if (scanResult.Value) // Scan OK
                {
                    // Send to both Database and Web
                    hmesSuccess = await _hmesService.SendToDatabaseAndWebAsync(sentData, workOrder, partNumber, result, message);
                    feedbackResult = hmesSuccess; // ? KEY: Feedback based on both DB + Web result
                }
                else
                {
                    // Scan NG - no HMES, feedback NG
                    feedbackResult = false;
                }

                return SerialProcessResult.CreateScanResult(
                    pid: _autoScanOutUI.ReadPID(),
                    workOrder: workOrder,
                    partNumber: partNumber,
                    result: result,
                    message: message,
                    scanSuccess: scanResult.Value,
                    hmesSuccess: hmesSuccess,
                    shouldSendFeedback: shouldSendFeedback,
                    feedbackResult: feedbackResult
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessScanOutAndRescanAsync");
                return SerialProcessResult.CreateError($"ScanOut_Rescan error: {ex.Message}");
            }
        }

        private async Task<bool> SendToHMESBasedOnRunMode(string pid, bool scanSuccess)
        {
            try
            {
                var config = _configService.Config;
                _logger.LogInformation("Sending to HMES: PID={PID}, RunMode={RunMode}, ScanSuccess={ScanSuccess}", 
                    pid, config.SelectedRunMode, scanSuccess);

                switch (config.SelectedRunMode)
                {
                    case AppConfig.RunMode.ScanOutOnly:
                        if (_autoScanOutUI.IsScanoutUI())
                        {
                            return await _hmesService.SendToDatabaseAsync(
                                pid,
                                _autoScanOutUI.ReadWO(),
                                _autoScanOutUI.ReadEBR(),
                                _autoScanOutUI.ReadResult(),
                                _autoScanOutUI.ReadMessage()
                            );
                        }
                        break;

                    case AppConfig.RunMode.RescanOnly:
                        return await _hmesService.SendToWebAsync(pid);

                    case AppConfig.RunMode.ScanOut_Rescan:
                        if (_autoScanOutUI.IsScanoutUI())
                        {
                            return await _hmesService.SendToDatabaseAndWebAsync(
                                pid,
                                _autoScanOutUI.ReadWO(),
                                _autoScanOutUI.ReadEBR(),
                                _autoScanOutUI.ReadResult(),
                                _autoScanOutUI.ReadMessage()
                            );
                        }
                        else
                        {
                            return await _hmesService.SendToWebAsync(pid);
                        }

                    default:
                        _logger.LogWarning("Unknown RunMode: {RunMode}", config.SelectedRunMode);
                        return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending to HMES for PID: {PID}", pid);
                return false;
            }
        }

        private async Task<bool?> ReadScanOutResultAsync(string pid)
        {
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

                if (_autoScanOutUI.ReadMessage().Contains($"Scan Data : [{cleanPid}]"))
                {
                    _logger.LogInformation("Scan failed for PID {PID}: {Message}", cleanPid, _autoScanOutUI.ReadMessage());
                    return false;
                }

                await Task.Delay(50);
            }

            _logger.LogError("Timeout reading scan result for PID: {PID}", cleanPid);
            return null;
        }
    }

    /// <summary>
    /// Result of serial data processing
    /// </summary>
    public class SerialProcessResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsSpecialCommand { get; set; }
        public bool ScanSuccess { get; set; }
        public bool HMESSuccess { get; set; }
        
        // ? NEW: Feedback control
        public bool ShouldSendFeedback { get; set; }
        public bool FeedbackResult { get; set; } // True = OK, False = NG
        
        // Scan data
        public string PID { get; set; } = string.Empty;
        public string WorkOrder { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public static SerialProcessResult CreateScanResult(string pid, string workOrder, string partNumber, 
            string result, string message, bool scanSuccess, bool hmesSuccess, 
            bool shouldSendFeedback = false, bool feedbackResult = false)
        {
            return new SerialProcessResult
            {
                Success = true,
                ScanSuccess = scanSuccess,
                HMESSuccess = hmesSuccess,
                ShouldSendFeedback = shouldSendFeedback,
                FeedbackResult = feedbackResult,
                PID = pid,
                WorkOrder = workOrder,
                PartNumber = partNumber,
                Result = result,
                Message = message
            };
        }

        public static SerialProcessResult CreateRescanResult(string pid, bool hmesSuccess, 
            bool shouldSendFeedback = false, bool feedbackResult = false)
        {
            return new SerialProcessResult
            {
                Success = true,
                ScanSuccess = true, // Assume rescan is OK
                HMESSuccess = hmesSuccess,
                ShouldSendFeedback = shouldSendFeedback,
                FeedbackResult = feedbackResult,
                PID = pid,
                Result = "OK",
                Message = "Rescan completed"
            };
        }

        public static SerialProcessResult CreateSpecialCommand(string command)
        {
            return new SerialProcessResult
            {
                Success = true,
                IsSpecialCommand = true,
                ShouldSendFeedback = false,
                Message = $"Special command: {command}"
            };
        }

        public static SerialProcessResult CreateError(string errorMessage)
        {
            return new SerialProcessResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ShouldSendFeedback = true,
                FeedbackResult = false // NG feedback for errors
            };
        }
    }
}