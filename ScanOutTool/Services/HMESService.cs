using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    /// <summary>
    /// Implementation of HMES service for sending data to Hyundai Manufacturing Execution System
    /// Integrates both Database and Web components using DataExecuter
    /// </summary>
    public class HMESService : IHMESService
    {
        private readonly ILogger<HMESService> _logger;
        private readonly IConfigService _configService;
        private DataExcuter.DataExecuter? _dataExecuter;
        private DataExcuter.DataExecuterConfig? _dataConfig;

        public HMESService(ILogger<HMESService> logger, IConfigService configService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            InitializeDataExecuter();
        }

        private void InitializeDataExecuter()
        {
            try
            {
                _logger.LogInformation("Initializing DataExecuter for HMES integration...");

                // Create DataExecuter configuration
                _dataConfig = new DataExcuter.DataExecuterConfig
                {
                    // Database configuration
                    DbHost = _configService.Config.HMESDbHost,
                    DbPort = _configService.Config.HMESDbPort,
                    DbName = _configService.Config.HMESDbName,
                    DbUsername = _configService.Config.HMESDbUsername,
                    DbPassword = _configService.Config.HMESDbPassword,
                    
                    // Web configuration
                    WebIpAddress = _configService.Config.HMESWebIP,
                    WebPort = _configService.Config.HMESWebPort,
                    WebUsername = _configService.Config.HMESWebUsername,
                    WebPassword = _configService.Config.HMESWebPassword,
                    ControlId = _configService.Config.HMESControlId,
                    AutoClearAfterSend = _configService.Config.HMESAutoClear
                };

                // Create DataExecuter instance
                _dataExecuter = new DataExcuter.DataExecuter(_dataConfig);

                // Subscribe to status events
                _dataExecuter.StatusChanged += (sender, message) =>
                {
                    _logger.LogInformation("DataExecuter Status: {Message}", message);
                };

                _logger.LogInformation("DataExecuter initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize DataExecuter");
            }
        }

        public async Task<bool> SendScanDataAsync(string pid, string workOrder, string partNumber, string result, string message)
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogError("DataExecuter not initialized");
                    return false;
                }

                var config = _configService.Config;
                _logger.LogInformation("Sending scan data to HMES: PID={PID}, WO={WorkOrder}, PN={PartNumber}, Result={Result}, RunMode={RunMode}", 
                    pid, workOrder, partNumber, result, config.SelectedRunMode);

                bool success = false;

                switch (config.SelectedRunMode)
                {
                    case AppConfig.RunMode.ScanOutOnly:
                        _logger.LogInformation("RunMode: ScanOutOnly - Sending to Database only");
                        if (config.EnableHMESDatabase)
                        {
                            var dbResult = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNumber, pid);
                            success = dbResult.Success;
                            if (!success)
                            {
                                _logger.LogWarning("Database validation failed: {Message}", dbResult.Message);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("HMES Database disabled, skipping");
                            success = true;
                        }
                        break;

                    case AppConfig.RunMode.ScanOut_Rescan:
                        _logger.LogInformation("RunMode: ScanOut_Rescan - Sending to both Database and Web");
                        
                        if (config.EnableHMESDatabase && config.EnableHMESWeb)
                        {
                            // Use ProcessDataAsync for both Database + Web
                            var processResult = await _dataExecuter.ProcessDataAsync(workOrder, partNumber, pid);
                            success = processResult.Success;
                            if (!success)
                            {
                                _logger.LogWarning("Combined Database+Web process failed: {Message}", processResult.Message);
                            }
                        }
                        else if (config.EnableHMESDatabase)
                        {
                            // Database only
                            var dbResult = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNumber, pid);
                            success = dbResult.Success;
                        }
                        else if (config.EnableHMESWeb)
                        {
                            // Web only
                            var webResult = await _dataExecuter.SendDatatoRescanAsync(pid);
                            success = webResult.Success;
                        }
                        else
                        {
                            _logger.LogInformation("Both HMES Database and Web disabled, skipping");
                            success = true;
                        }
                        break;

                    case AppConfig.RunMode.RescanOnly:
                        _logger.LogInformation("RunMode: RescanOnly - Should use SendRescanDataAsync method");
                        success = await SendRescanDataAsync(pid);
                        break;

                    default:
                        _logger.LogWarning("Unknown RunMode: {RunMode}, defaulting to ScanOut_Rescan", config.SelectedRunMode);
                        goto case AppConfig.RunMode.ScanOut_Rescan;
                }

                if (success)
                {
                    _logger.LogInformation("Successfully sent scan data to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                }
                else
                {
                    _logger.LogWarning("Failed to send scan data to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send scan data to HMES for PID: {PID}", pid);
                return false;
            }
        }

        public async Task<bool> SendRescanDataAsync(string pid)
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogError("DataExecuter not initialized");
                    return false;
                }

                var config = _configService.Config;
                _logger.LogInformation("Sending rescan data to HMES: PID={PID}, RunMode={RunMode}", pid, config.SelectedRunMode);

                bool success = false;

                switch (config.SelectedRunMode)
                {
                    case AppConfig.RunMode.RescanOnly:
                        _logger.LogInformation("RunMode: RescanOnly - Sending to Web only");
                        if (config.EnableHMESWeb)
                        {
                            var webResult = await _dataExecuter.SendDatatoRescanAsync(pid);
                            success = webResult.Success;
                            if (!success)
                            {
                                _logger.LogWarning("Web rescan failed: {Message}", webResult.Message);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("HMES Web disabled, skipping");
                            success = true;
                        }
                        break;

                    case AppConfig.RunMode.ScanOut_Rescan:
                        _logger.LogInformation("RunMode: ScanOut_Rescan - Sending rescan to Web");
                        if (config.EnableHMESWeb)
                        {
                            var webResult = await _dataExecuter.SendDatatoRescanAsync(pid);
                            success = webResult.Success;
                        }
                        else
                        {
                            _logger.LogInformation("HMES Web disabled, skipping");
                            success = true;
                        }
                        break;

                    default:
                        _logger.LogInformation("RunMode {RunMode} - Sending rescan to Web", config.SelectedRunMode);
                        if (config.EnableHMESWeb)
                        {
                            var webResult = await _dataExecuter.SendDatatoRescanAsync(pid);
                            success = webResult.Success;
                        }
                        else
                        {
                            success = true;
                        }
                        break;
                }

                if (success)
                {
                    _logger.LogInformation("Successfully sent rescan data to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                }
                else
                {
                    _logger.LogWarning("Failed to send rescan data to HMES - PID: {PID}, RunMode: {RunMode}", pid, config.SelectedRunMode);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send rescan data to HMES for PID: {PID}", pid);
                return false;
            }
        }

        public async Task<bool> CheckConnectivityAsync()
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogError("DataExecuter not initialized");
                    return false;
                }

                _logger.LogInformation("Checking HMES connectivity...");

                bool webConnected = true;
                
                var config = _configService.Config;

                // Check web connectivity if enabled
                if (config.EnableHMESWeb)
                {
                    _logger.LogInformation("Checking HMES Web connectivity...");
                    webConnected = _dataExecuter.IsWebPageValid();
                    
                    if (!webConnected)
                    {
                        _logger.LogInformation("HMES Web not connected, attempting to start session...");
                        webConnected = await _dataExecuter.StartSessionAsync(true);
                    }
                }

                // For database connectivity, we assume it's OK since DataExecuter will handle errors
                bool dbConnected = true;

                var overallConnected = dbConnected && webConnected;

                if (overallConnected)
                {
                    _logger.LogInformation("HMES systems connectivity check passed");
                }
                else
                {
                    _logger.LogWarning("HMES connectivity issues - DB: {DbStatus}, Web: {WebStatus}", 
                        dbConnected, webConnected);
                }

                return overallConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HMES connectivity check failed");
                return false;
            }
        }

        /// <summary>
        /// Initialize HMES Web session
        /// </summary>
        public async Task<bool> InitializeWebSessionAsync()
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogError("DataExecuter not initialized");
                    return false;
                }

                if (!_configService.Config.EnableHMESWeb)
                {
                    _logger.LogInformation("HMES Web disabled, skipping session initialization");
                    return true;
                }

                _logger.LogInformation("Initializing HMES Web session...");
                var success = await _dataExecuter.StartSessionAsync(true);

                if (success)
                {
                    _logger.LogInformation("HMES Web session initialized successfully");
                }
                else
                {
                    _logger.LogError("Failed to initialize HMES Web session");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing HMES Web session");
                return false;
            }
        }

        /// <summary>
        /// Get pack quantity from HMES Web
        /// </summary>
        public async Task<int> GetPackQtyAsync()
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogWarning("DataExecuter not initialized");
                    return -1;
                }

                return await _dataExecuter.GetPackQty();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pack quantity from HMES");
                return -1;
            }
        }

        /// <summary>
        /// Get magazine quantity from HMES Web
        /// </summary>
        public async Task<int> GetMagazineQtyAsync()
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogWarning("DataExecuter not initialized");
                    return -1;
                }

                return await _dataExecuter.GetMagazineQty();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting magazine quantity from HMES");
                return -1;
            }
        }

        public void Dispose()
        {
            try
            {
                _logger.LogInformation("Disposing HMES Service...");
                _dataExecuter?.Dispose();
                _dataExecuter = null;
                _dataConfig = null;
                _logger.LogInformation("HMES Service disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing HMES Service");
            }
        }
    }
}