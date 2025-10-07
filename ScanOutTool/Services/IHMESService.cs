using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    /// <summary>
    /// Interface for HMES (Hyundai Manufacturing Execution System) integration
    /// </summary>
    public interface IHMESService
    {
        /// <summary>
        /// Send scan data to HMES system
        /// </summary>
        /// <param name="pid">Product ID</param>
        /// <param name="workOrder">Work Order</param>
        /// <param name="partNumber">Part Number</param>
        /// <param name="result">Scan result (OK/NG)</param>
        /// <param name="message">Additional message</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SendScanDataAsync(string pid, string workOrder, string partNumber, string result, string message);

        /// <summary>
        /// Send rescan data to HMES system
        /// </summary>
        /// <param name="pid">Product ID</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SendRescanDataAsync(string pid);

        /// <summary>
        /// Check HMES system connectivity
        /// </summary>
        /// <returns>True if HMES is accessible, false otherwise</returns>
        Task<bool> CheckConnectivityAsync();

        /// <summary>
        /// Initialize HMES Web session
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> InitializeWebSessionAsync();

        /// <summary>
        /// Get pack quantity from HMES Web
        /// </summary>
        /// <returns>Pack quantity, -1 if error</returns>
        Task<int> GetPackQtyAsync();

        /// <summary>
        /// Get magazine quantity from HMES Web
        /// </summary>
        /// <returns>Magazine quantity, -1 if error</returns>
        Task<int> GetMagazineQtyAsync();

        /// <summary>
        /// Dispose resources
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Interface for HMES Web integration using Selenium automation
    /// </summary>
    public interface IHMESWebService
    {
        /// <summary>
        /// Start web session and login
        /// </summary>
        /// <returns>Session ID if successful, null otherwise</returns>
        Task<string?> StartSessionAsync();

        /// <summary>
        /// Send data to HMES web page
        /// </summary>
        /// <param name="pid">Product ID to send</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> TransferDataAsync(string pid);

        /// <summary>
        /// Get current pack quantity from web page
        /// </summary>
        /// <returns>Pack quantity, -1 if error</returns>
        Task<int> GetPackQtyAsync();

        /// <summary>
        /// Print current page manually
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> PrintManualAsync();

        /// <summary>
        /// Check if currently on target page
        /// </summary>
        /// <returns>True if on target page, false otherwise</returns>
        bool IsOnTargetPage();

        /// <summary>
        /// Clean up resources
        /// </summary>
        void Dispose();
    }
}