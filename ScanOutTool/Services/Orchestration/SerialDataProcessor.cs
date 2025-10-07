using Microsoft.Extensions.Logging;
using ScanOutTool.Services.Orchestration;
using ScanOutTool.Models; // ? NEW: For ScannerData
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

                // ? NEW: Parse scanner data (PID|qty format)
                var scannerData = ScannerData.Parse(sentData);
                if (!scannerData.IsValid)
                {
                    _logger.LogError("Invalid scanner data format: {Error}", scannerData.ErrorMessage);
                    return SerialProcessResult.CreateError($"Invalid data format: {scannerData.ErrorMessage}");
                }

                _logger.LogInformation("Parsed scanner data: PID={PID}, SlotQty={SlotQty}", 
                    scannerData.PID, scannerData.SlotQuantity);

                if (!_autoScanOutUI.IsScanoutUI())
                {
                    _logger.LogWarning("ScanOut UI not available");
                    return SerialProcessResult.CreateError("ScanOut UI not available");
                }

                var config = _configService.Config;
                
                // ? NEW: Process based on RunMode with quantity validation
                switch (config.SelectedRunMode)
                {
                    case AppConfig.RunMode.ScanOutOnly:
                        return await ProcessScanOutOnlyAsync(scannerData);
                        
                    case AppConfig.RunMode.RescanOnly:
                        return await ProcessRescanOnlyAsync(scannerData);
                        
                    case AppConfig.RunMode.ScanOut_Rescan:
                        return await ProcessScanOutAndRescanAsync(scannerData);
                        
                    default:
                        _logger.LogWarning("Unknown RunMode: {RunMode}, defaulting to ScanOut_Rescan", config.SelectedRunMode);
                        return await ProcessScanOutAndRescanAsync(scannerData);
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
        private async Task<SerialProcessResult> ProcessScanOutOnlyAsync(ScannerData scannerData)
        {
            try
            {
                // Read scan result from ScanOut UI
                var scanResult = await ReadScanOutResultAsync(scannerData.PID);
                
                if (scanResult == null)
                {
                    // Scan timeout/error
                    return SerialProcessResult.CreateScanResult(
                        pid: scannerData.PID,
                        workOrder: "",
                        partNumber: "",
                        result: "NG",
                        message: "Scan timeout",
                        scanSuccess: false,
                        hmesSuccess: false,
                        shouldSendFeedback: true,
                        feedbackResult: false,
                        feedbackMessage: "NG|Scanout NG"
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
                string feedbackMessage = "OK";

                if (scanResult.Value) // Scan OK
                {
                    // ? TIMING: Log before HMES operation
                    _logger.LogInformation("ScanOutOnly: Sending to HMES Database for PID: {PID}", scannerData.PID);
                    var startTime = DateTime.Now;
                    
                    // Send to HMES Database
                    hmesSuccess = await _hmesService.SendToDatabaseAsync(scannerData.PID, workOrder, partNumber, result, message);
                    
                    var duration = DateTime.Now - startTime;
                    _logger.LogInformation("ScanOutOnly: HMES Database completed in {Duration}ms, Success: {Success}", 
                        duration.TotalMilliseconds, hmesSuccess);
                    
                    if (hmesSuccess)
                    {
                        feedbackResult = true;
                        feedbackMessage = "OK";
                    }
                    else
                    {
                        feedbackResult = false;
                        feedbackMessage = "NG|Scanout NG";
                    }
                    
                    // ? SAFEGUARD: Small delay to ensure HMES operations are fully complete
                    if (hmesSuccess)
                    {
                        await Task.Delay(50); // 50ms delay for safety
                    }
                }
                else
                {
                    // Scan NG - no HMES, feedback NG
                    _logger.LogInformation("ScanOutOnly: Scan NG for PID: {PID}, skipping HMES", scannerData.PID);
                    feedbackResult = false;
                    feedbackMessage = "NG|Scanout NG";
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
                    feedbackResult: feedbackResult,
                    feedbackMessage: feedbackMessage,
                    expectedQuantity: scannerData.SlotQuantity
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessScanOutOnlyAsync");
                return SerialProcessResult.CreateError($"ScanOutOnly error: {ex.Message}");
            }
        }

        /// <summary>
        /// ? NEW: Process RescanOnly - Send feedback after HMES Web success with quantity validation
        /// </summary>
        private async Task<SerialProcessResult> ProcessRescanOnlyAsync(ScannerData scannerData)
        {
            try
            {
                // ? TIMING: Log before HMES operation
                _logger.LogInformation("RescanOnly: Sending to HMES Web for PID: {PID}", scannerData.PID);
                var startTime = DateTime.Now;
                
                // For rescan mode, send directly to HMES Web
                var hmesSuccess = await _hmesService.SendToWebAsync(scannerData.PID);
                
                var duration = DateTime.Now - startTime;
                _logger.LogInformation("RescanOnly: HMES Web completed in {Duration}ms, Success: {Success}", 
                    duration.TotalMilliseconds, hmesSuccess);

                // ? NEW: Quantity validation for RescanOnly mode
                bool quantityMatch = true;
                int actualQuantity = 0;
                string feedbackMessage = "OK";
                bool feedbackResult = hmesSuccess;

                if (hmesSuccess && scannerData.SlotQuantity > 0) // Only check if scanner provided quantity
                {
                    try
                    {
                        // Get actual quantity from HMES
                        actualQuantity = await _hmesService.GetPackQtyAsync();
                        quantityMatch = actualQuantity == scannerData.SlotQuantity;
                        
                        _logger.LogInformation("RescanOnly: Quantity check - Expected: {Expected}, Actual: {Actual}, Match: {Match}", 
                            scannerData.SlotQuantity, actualQuantity, quantityMatch);

                        if (!quantityMatch)
                        {
                            feedbackMessage = "NG|Miss match quantity";
                            feedbackResult = false;
                            _logger.LogWarning("RescanOnly: Quantity mismatch for PID {PID}", scannerData.PID);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "RescanOnly: Failed to get pack quantity from HMES");
                        // Continue with original result if quantity check fails
                    }
                }
                else if (!hmesSuccess)
                {
                    feedbackMessage = "NG|Scanout NG";
                    feedbackResult = false;
                }

                // ? SAFEGUARD: Small delay to ensure HMES operations are fully complete
                if (hmesSuccess)
                {
                    await Task.Delay(50); // 50ms delay for safety
                }

                return SerialProcessResult.CreateRescanResult(
                    pid: scannerData.PID,
                    hmesSuccess: hmesSuccess,
                    shouldSendFeedback: true,
                    feedbackResult: feedbackResult,
                    feedbackMessage: feedbackMessage,
                    expectedQuantity: scannerData.SlotQuantity,
                    actualQuantity: actualQuantity,
                    quantityMatch: quantityMatch
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessRescanOnlyAsync");
                return SerialProcessResult.CreateError($"RescanOnly error: {ex.Message}");
            }
        }

        /// <summary>
        /// ? NEW: Process ScanOut + Rescan - Send feedback after both operations complete with quantity validation
        /// </summary>
        private async Task<SerialProcessResult> ProcessScanOutAndRescanAsync(ScannerData scannerData)
        {
            try
            {
                // Read scan result from ScanOut UI
                var scanResult = await ReadScanOutResultAsync(scannerData.PID);
                
                if (scanResult == null)
                {
                    // Scan timeout/error
                    return SerialProcessResult.CreateScanResult(
                        pid: scannerData.PID,
                        workOrder: "",
                        partNumber: "",
                        result: "NG",
                        message: "Scan timeout",
                        scanSuccess: false,
                        hmesSuccess: false,
                        shouldSendFeedback: true,
                        feedbackResult: false,
                        feedbackMessage: "NG|Scanout NG"
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
                string feedbackMessage = "OK";
                bool quantityMatch = true;
                int actualQuantity = 0;

                if (scanResult.Value) // Scan OK
                {
                    // ? TIMING: Log before HMES operation
                    _logger.LogInformation("ScanOut_Rescan: Sending to HMES Database+Web for PID: {PID}", scannerData.PID);
                    var startTime = DateTime.Now;
                    
                    // Send to both Database and Web
                    hmesSuccess = await _hmesService.SendToDatabaseAndWebAsync(scannerData.PID, workOrder, partNumber, result, message);
                    
                    var duration = DateTime.Now - startTime;
                    _logger.LogInformation("ScanOut_Rescan: HMES Database+Web completed in {Duration}ms, Success: {Success}", 
                        duration.TotalMilliseconds, hmesSuccess);
                    
                    // ? NEW: Quantity validation for ScanOut_Rescan mode
                    if (hmesSuccess && scannerData.SlotQuantity > 0) // Only check if scanner provided quantity
                    {
                        try
                        {
                            // Get actual quantity from HMES
                            actualQuantity = await _hmesService.GetPackQtyAsync();
                            quantityMatch = actualQuantity == scannerData.SlotQuantity;
                                                
                            _logger.LogInformation("ScanOut_Rescan: Quantity check - Expected: {Expected}, Actual: {Actual}, Match: {Match}", 
                                scannerData.SlotQuantity, actualQuantity, quantityMatch);

                            if (!quantityMatch)
                            {
                                feedbackMessage = "NG|Miss match quantity";
                                feedbackResult = false;
                                _logger.LogWarning("ScanOut_Rescan: Quantity mismatch for PID {PID}", scannerData.PID);
                            }
                            else
                            {
                                feedbackResult = true; // Both HMES and quantity OK
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "ScanOut_Rescan: Failed to get pack quantity from HMES");
                            // Continue with HMES result if quantity check fails
                            feedbackResult = hmesSuccess;
                        }
                    }
                    else if (hmesSuccess)
                    {
                        feedbackResult = true; // HMES OK, no quantity to check
                    }
                    else
                    {
                        feedbackMessage = "NG|Scanout NG";
                        feedbackResult = false;
                    }
                    
                    // ? SAFEGUARD: Small delay to ensure HMES operations are fully complete
                    if (hmesSuccess)
                    {
                        await Task.Delay(50); // 50ms delay for safety
                    }
                }
                else
                {
                    // Scan NG - no HMES, feedback NG
                    _logger.LogInformation("ScanOut_Rescan: Scan NG for PID: {PID}, skipping HMES", scannerData.PID);
                    feedbackMessage = "NG|Scanout NG";
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
                    feedbackResult: feedbackResult,
                    feedbackMessage: feedbackMessage,
                    expectedQuantity: scannerData.SlotQuantity,
                    actualQuantity: actualQuantity,
                    quantityMatch: quantityMatch
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
        
        // ? NEW: Feedback control with detailed messages
        public bool ShouldSendFeedback { get; set; }
        public bool FeedbackResult { get; set; } // True = OK, False = NG
        public string FeedbackMessage { get; set; } = "OK"; // "OK", "NG|Scanout NG", "NG|Miss match quantity"
        
        // ? NEW: Quantity validation
        public int ExpectedQuantity { get; set; } // From scanner
        public int ActualQuantity { get; set; } // From HMES
        public bool QuantityMatch { get; set; } = true;
        
        // Scan data
        public string PID { get; set; } = string.Empty;
        public string WorkOrder { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public static SerialProcessResult CreateScanResult(string pid, string workOrder, string partNumber, 
            string result, string message, bool scanSuccess, bool hmesSuccess, 
            bool shouldSendFeedback = false, bool feedbackResult = false, string feedbackMessage = "OK",
            int expectedQuantity = 0, int actualQuantity = 0, bool quantityMatch = true)
        {
            return new SerialProcessResult
            {
                Success = true,
                ScanSuccess = scanSuccess,
                HMESSuccess = hmesSuccess,
                ShouldSendFeedback = shouldSendFeedback,
                FeedbackResult = feedbackResult,
                FeedbackMessage = feedbackMessage,
                ExpectedQuantity = expectedQuantity,
                ActualQuantity = actualQuantity,
                QuantityMatch = quantityMatch,
                PID = pid,
                WorkOrder = workOrder,
                PartNumber = partNumber,
                Result = result,
                Message = message
            };
        }

        public static SerialProcessResult CreateRescanResult(string pid, bool hmesSuccess, 
            bool shouldSendFeedback = false, bool feedbackResult = false, string feedbackMessage = "OK",
            int expectedQuantity = 0, int actualQuantity = 0, bool quantityMatch = true)
        {
            return new SerialProcessResult
            {
                Success = true,
                ScanSuccess = true, // Assume rescan is OK
                HMESSuccess = hmesSuccess,
                ShouldSendFeedback = shouldSendFeedback,
                FeedbackResult = feedbackResult,
                FeedbackMessage = feedbackMessage,
                ExpectedQuantity = expectedQuantity,
                ActualQuantity = actualQuantity,
                QuantityMatch = quantityMatch,
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
                FeedbackMessage = "OK",
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
                FeedbackResult = false, // NG feedback for errors
                FeedbackMessage = "NG|Processing error"
            };
        }
    }
}