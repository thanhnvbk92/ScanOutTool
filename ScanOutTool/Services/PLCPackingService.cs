using McpXLib;
using McpXLib.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class PLCPackingService:IPLCPackingService
    {
        private readonly string _ip;
        private readonly int _port;
        private readonly bool _isAsciiMode;
        private McpX _client;
        private readonly Timer _monitorTimer;

        public ILoggingService LoggingService { get; set; }
        public bool IsConnected { get; set; }
        public event Action<bool> OnConnectionChanged;

        public PLCPackingService(string ip, int port, bool isAsciiMode)
        {
            _ip = ip;
            _port = port;
            _isAsciiMode = isAsciiMode;

            _monitorTimer = new Timer(CheckConnection, null, 2000, 2000);
        }

        public void TryConnect()
        {
            try
            {
                _client?.Dispose();
                _client = new McpX(_ip, _port, null, _isAsciiMode);
                SetConnected(true);
            }
            catch
            {
                SetConnected(false);
            }
        }

        private void CheckConnection(object state)
        {
            try
            {
                _client?.BatchRead<ushort>(Prefix.D, "0", 1); // Test đơn giản
                SetConnected(true);
            }
            catch
            {
                SetConnected(false);
                TryConnect(); // Tự động reconnect
            }
        }

        private void SetConnected(bool state)
        {
            if (IsConnected != state)
            {
                IsConnected = state;
                OnConnectionChanged?.Invoke(state);
            }
        }

        public ushort ReadWord(string address)
        {
            if (!IsConnected) throw new InvalidOperationException("PLC is not connected.");
            LoggingService?.LogInformation($"ReadingWord: Address={address}");
            try
            {
                var result = _client.BatchRead<ushort>(Prefix.D, address, 1);
                LoggingService?.LogInformation($"ReadWord: Address={address}, Value={result[0]}");
                return result[0];
            }
            catch (Exception ex)
            {
                LoggingService?.LogError($"ReadWord failed: Address={address}: {ex.Message}");
                throw;
            }
            
        }

        public void WriteWord(string address, ushort value)
        {
            if (!IsConnected) throw new InvalidOperationException("PLC is not connected.");
            _client.BatchWrite(Prefix.D, address, new ushort[] { value });
        }

        public void Dispose()
        {
            _monitorTimer?.Dispose();
            _client?.Dispose();
        }



        public int GetTotalTray() // VD: "D100"
        {
            return (int)ReadWord("8082");
        }

        public string GetPID()
        {
            throw new NotImplementedException();
        }

        public int GetTray()
        {
            return (int)ReadWord("156");
        }

        public int GetCurrentSlot()
        {
            throw new NotImplementedException();
        }
    }
}
