using System;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IPLCService : IDisposable
    {

        event Action<bool> OnConnectionChanged;

        bool IsConnected { get; }

        int GetTotalTray();

        int GetCurrentTray();

        int GetTotalSlot();

        int GetTraySlot();

        int GetCurrentModelNumber();

        int GetCurrentSlot();


        string ReadPID();

        /// <summary>
        /// Gửi tín hiệu pass bằng cách ghi 1 vào B0 rồi sau 1 giây xóa về 0
        /// </summary>
        Task SetPassSignalAsync();

    }
}
