public class AppConfig
{
    public SerialPortSettingViewModel ScannerPortSettingVM { get; set; }
    public SerialPortSettingViewModel ShopFloorPortSettingVM { get; set; }
    public bool IsRobotMode { get; set; }
    public string PLCIP { get; set; }
    public int PLCPort { get; set; }
    public bool IsWOMode { get; set; }
    public bool IsBlockRFMode { get; set; }
    public string ServerIP { get; set; }
    public string ShopFloorLogPath { get; set; } = "C:\\Admin\\Documents\\LG CNS\\ezMES\\Logs";
    
    // ✅ NEW: PLC Usage Control
    public bool UsePLC { get; set; } = true;
    
    // Feedback message configuration
    public string OkFeedbackMessage { get; set; } = "OK";
    public string NgFeedbackMessage { get; set; } = "NG";
    public bool EnableScannerFeedback { get; set; } = true;
    public int FeedbackDelayMs { get; set; } = 100; // Delay before sending feedback
}
