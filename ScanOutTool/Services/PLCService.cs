using System.Threading.Tasks;
using System;
using System.Threading;
using System.Linq;

namespace ScanOutTool.Services
{
    public class PLCService : IPLCService
    {
        private readonly PlcHelper _helper;
        private readonly Timer _connectionCheckTimer;
        private bool _isConnected;

        public event Action<bool> OnConnectionChanged;

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnConnectionChanged?.Invoke(value);
                }
            }
        }

        public PLCService(string ip, int port, bool isAscii = false)
        {
            _helper = new PlcHelper(ip, port, isAscii);
            _connectionCheckTimer = new Timer(CheckConnectionCallback, null, 0, 2000);
        }

        private void CheckConnectionCallback(object state)
        {
            IsConnected = _helper.CheckConnection();
        }

        public int GetTotalTray() => _helper.ReadWord("D8082");

        public int GetCurrentTray() => _helper.ReadWord("D156");

        public int GetTotalSlot()
        {
            return GetCurrentModelNumber() switch
            {
                1 => 64,
                2 => 36,
                _ => throw new NotSupportedException("Unknown model number")
            };
        }

        public int GetTraySlot()
        {
            return GetCurrentModelNumber() switch
            {
                1 => 8,
                2 => 6,
                _ => throw new NotSupportedException("Unknown model number")
            };
        }

        public int GetCurrentSlot()
        {
            bool[] slots = _helper.ReadBits("M341", 8);
            return slots.Sum(x => x ? 1 : 0);
        }

        public int GetCurrentModelNumber() => _helper.ReadWord("D900");

        public string ReadPID()
        {
            ushort[] pidData = _helper.ReadWords("D604", 11); // 11 từ = 22 byte = 22 ký tự            
            // Chuyển từng ushort thành 2 byte [HighByte, LowByte], rồi ghép thành mảng byte[]
            byte[] bytes = pidData
                .SelectMany(word => new[] { (byte)(word & 0xFF), (byte)(word >> 8) })
                .ToArray();

            // Chuyển từ byte[] sang chuỗi ASCII
            return System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        public async Task SetPassSignalAsync()
        {
            _helper.WriteBit("B0", true);
            await Task.Delay(1000);
            _helper.WriteBit("B0", false);
        }

        public void Dispose()
        {
            _connectionCheckTimer?.Dispose();
            _helper?.Dispose();
        }
    }
}
