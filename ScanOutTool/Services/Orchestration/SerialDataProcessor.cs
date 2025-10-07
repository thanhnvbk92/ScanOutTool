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

                // Read scan result
                var scanResult = await ReadScanOutResultAsync(sentData);
                
                if (scanResult != null)
                {
                    // Send to HMES based on RunMode
                    var hmesSuccess = await SendToHMESBasedOnRunMode(sentData, scanResult.Value);
                    
                    // Create result with scan data
                    return SerialProcessResult.CreateScanResult(
                        pid: _autoScanOutUI.ReadPID(),
                        workOrder: _autoScanOutUI.ReadWO(),
                        partNumber: _autoScanOutUI.ReadEBR(),
                        result: _autoScanOutUI.ReadResult(),
                        message: _autoScanOutUI.ReadMessage(),
                        scanSuccess: scanResult.Value,
                        hmesSuccess: hmesSuccess
                    );
                }
                else
                {
                    // Timeout or error case - try rescan
                    var hmesSuccess = await SendToHMESBasedOnRunMode(sentData, false);
                    
                    return SerialProcessResult.CreateRescanResult(
                        pid: sentData,
                        hmesSuccess: hmesSuccess
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing serial data: {Data}", sentData);
                return SerialProcessResult.CreateError($"Processing error: {ex.Message}");
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
        
        // Scan data
        public string PID { get; set; } = string.Empty;
        public string WorkOrder { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public static SerialProcessResult CreateScanResult(string pid, string workOrder, string partNumber, 
            string result, string message, bool scanSuccess, bool hmesSuccess)
        {
            return new SerialProcessResult
            {
                Success = true,
                ScanSuccess = scanSuccess,
                HMESSuccess = hmesSuccess,
                PID = pid,
                WorkOrder = workOrder,
                PartNumber = partNumber,
                Result = result,
                Message = message
            };
        }

        public static SerialProcessResult CreateRescanResult(string pid, bool hmesSuccess)
        {
            return new SerialProcessResult
            {
                Success = true,
                ScanSuccess = true, // Assume rescan is OK
                HMESSuccess = hmesSuccess,
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
                Message = $"Special command: {command}"
            };
        }

        public static SerialProcessResult CreateError(string errorMessage)
        {
            return new SerialProcessResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}