public class AppConfig
{
    public enum RunMode
    {
        ScanOutOnly,
        RescanOnly,
        ScanOut_Rescan,
        None
    }

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
    
    // ✅ RESTORED: RunMode configuration
    public RunMode SelectedRunMode { get; set; } = RunMode.ScanOut_Rescan;
    
    // ✅ RESTORED: EBR configuration
    public string SelectedEBR { get; set; } = string.Empty;
    
    // Feedback message configuration
    public string OkFeedbackMessage { get; set; } = "OK";
    public string NgFeedbackMessage { get; set; } = "NG";
    public bool EnableScannerFeedback { get; set; } = true;
    public int FeedbackDelayMs { get; set; } = 100; // Delay before sending feedback

    // ✅ RESTORED: HMES Database Configuration
    public string HMESDbHost { get; set; } = "10.7.10.56";
    public string HMESDbPort { get; set; } = "1521";
    public string HMESDbName { get; set; } = "HSEVNPDB";
    public string HMESDbUsername { get; set; } = "INFINITY21_JSMES";
    public string HMESDbPassword { get; set; } = "INFINITY21_JSMES";
    
    // ✅ RESTORED: HMES Web Configuration
    public string HMESWebIP { get; set; } = "10.7.10.56";
    public string HMESWebPort { get; set; } = "8080";
    public string HMESWebUsername { get; set; } = "AOI";
    public string HMESWebPassword { get; set; } = "123456";
    public string HMESControlId { get; set; } = "P4_BARCODE";
    public bool HMESAutoClear { get; set; } = true;
    public bool EnableHMESWeb { get; set; } = true;
    public bool EnableHMESDatabase { get; set; } = true;
}
