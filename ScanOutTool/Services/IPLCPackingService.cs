using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using McpXLib;
using McpXLib.Enums;



namespace ScanOutTool.Services
{
    public interface IPLCPackingService
    {
        public ILoggingService LoggingService { get; set; }
        public bool IsConnected { get; set; }
        void TryConnect();
        int GetTotalTray(); // VD: "D100"
        string GetPID();
        int GetTray();
        int GetCurrentSlot();
        void Dispose();

    }
}
