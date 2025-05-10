// MitsubishiPlcService.cs
using HslCommunication.Profinet.Melsec;
using HslCommunication;

public class MitsubishiPlcService : IPlcService
{
    private MelsecMcNet _plc;

    public bool Connect(string ip, int port)
    {
        _plc = new MelsecMcNet(ip, port);
        var result = _plc.ConnectServer();
        return result.IsSuccess;
    }

    public void Disconnect()
    {
        _plc?.ConnectClose();
    }

    public short? ReadInt16(string address)
    {
        var result = _plc.ReadInt16(address);
        return result.IsSuccess ? result.Content : null;
    }

    public bool WriteInt16(string address, short value)
    {
        var result = _plc.Write(address, value);
        return result.IsSuccess;
    }
}
