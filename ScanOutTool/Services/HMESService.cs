using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    /// <summary>
    /// Implementation of HMES service - provides separate methods for different integration types
    /// Does NOT contain app state logic - pure business logic only
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

        public async Task<bool> SendToDatabaseAsync(string pid, string workOrder, string partNumber, string result, string message)
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogError("DataExecuter not initialized");
                    return false;
                }

                if (!_configService.Config.EnableHMESDatabase)
                {
                    _logger.LogInformation("HMES Database disabled, skipping");
                    return true; // Not an error, just disabled
                }

                _logger.LogInformation("Sending data to HMES Database: PID={PID}, WO={WorkOrder}, PN={PartNumber}, Result={Result}", 
                    pid, workOrder, partNumber, result);

                var dbResult = await _dataExecuter.SendDataScanoutOnlyAsync(workOrder, partNumber, pid);

                if (dbResult.Success)
                {
                    _logger.LogInformation("Successfully sent data to HMES Database: PID={PID}", pid);
                }
                else
                {
                    _logger.LogWarning("Failed to send data to HMES Database: PID={PID}, Message={Message}", pid, dbResult.Message);
                }

                return dbResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to HMES Database for PID: {PID}", pid);
                return false;
            }
        }

        public async Task<bool> SendToWebAsync(string pid)
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
                    _logger.LogInformation("HMES Web disabled, skipping");
                    return true; // Not an error, just disabled
                }

                _logger.LogInformation("Sending data to HMES Web: PID={PID}", pid);

                var webResult = await _dataExecuter.SendDatatoRescanAsync(pid);

                if (webResult.Success)
                {
                    _logger.LogInformation("Successfully sent data to HMES Web: PID={PID}", pid);
                }
                else
                {
                    _logger.LogWarning("Failed to send data to HMES Web: PID={PID}, Message={Message}", pid, webResult.Message);
                }

                return webResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to HMES Web for PID: {PID}", pid);
                return false;
            }
        }

        public async Task<bool> SendToDatabaseAndWebAsync(string pid, string workOrder, string partNumber, string result, string message)
        {
            try
            {
                if (_dataExecuter == null)
                {
                    _logger.LogError("DataExecuter not initialized");
                    return false;
                }

                _logger.LogInformation("Sending data to both HMES Database and Web: PID={PID}, WO={WorkOrder}, PN={PartNumber}, Result={Result}", 
                    pid, workOrder, partNumber, result);

                bool dbSuccess = true;
                bool webSuccess = true;

                // Send to Database if enabled
                if (_configService.Config.EnableHMESDatabase && _configService.Config.EnableHMESWeb)
                {
                    // Use combined method for efficiency
                    var processResult = await _dataExecuter.ProcessDataAsync(workOrder, partNumber, pid);
                    return processResult.Success;
                }
                else
                {
                    // Send to individual systems
                    if (_configService.Config.EnableHMESDatabase)
                    {
                        dbSuccess = await SendToDatabaseAsync(pid, workOrder, partNumber, result, message);
                    }

                    if (_configService.Config.EnableHMESWeb)
                    {
                        webSuccess = await SendToWebAsync(pid);
                    }

                    var overallSuccess = dbSuccess && webSuccess;
                    
                    if (overallSuccess)
                    {
                        _logger.LogInformation("Successfully sent data to all enabled HMES systems: PID={PID}", pid);
                    }
                    else
                    {
                        _logger.LogWarning("Some HMES systems failed: PID={PID}, DB={DbResult}, Web={WebResult}", 
                            pid, dbSuccess, webSuccess);
                    }

                    return overallSuccess;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to HMES Database and Web for PID: {PID}", pid);
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