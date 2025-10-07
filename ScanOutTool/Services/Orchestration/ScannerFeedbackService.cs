using Microsoft.Extensions.Logging;
using SerialProxyLib;
using ScanOutTool.Services.Orchestration;
using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services.Orchestration
{
    /// <summary>
    /// Handles scanner feedback logic - extracted from ScanWorkflowService
    /// </summary>
    public class ScannerFeedbackService
    {
        private readonly ILogger<ScannerFeedbackService> _logger;
        private readonly IConfigService _configService;

        public ScannerFeedbackService(
            ILogger<ScannerFeedbackService> logger,
            IConfigService configService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Send feedback to scanner based on scan result
        /// </summary>
        public async Task SendFeedbackAsync(SerialProxyManager? serialProxyManager, bool isOK)
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

                if (serialProxyManager == null)
                {
                    _logger.LogWarning("SerialProxyManager is not initialized, cannot send feedback");
                    return;
                }

                // Add configurable delay before sending feedback
                if (config.FeedbackDelayMs > 0)
                {
                    await Task.Delay(config.FeedbackDelayMs);
                }

                // Use configured feedback messages
                string feedbackMessage = isOK ? config.OkFeedbackMessage : config.NgFeedbackMessage;
                
                // Add carriage return for proper serial communication
                var feedbackData = feedbackMessage + "\r";

                // Send feedback to scanner (device port)
                await serialProxyManager.SendToDeviceAsync(feedbackData);

                _logger.LogInformation("Sent feedback to scanner: {Feedback} (Result: {Result})", 
                    feedbackMessage, isOK ? "OK" : "NG");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending feedback to scanner");
                // Don't throw - feedback failure shouldn't stop the workflow
            }
        }

        /// <summary>
        /// Send NG feedback for blocked or invalid data
        /// </summary>
        public async Task SendNGFeedbackAsync(SerialProxyManager? serialProxyManager, string reason)
        {
            _logger.LogWarning("Sending NG feedback: {Reason}", reason);
            await SendFeedbackAsync(serialProxyManager, false);
        }
    }
}