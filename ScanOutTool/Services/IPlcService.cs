// IPlcService.cs
public interface IPlcService
{
    bool Connect(string ip, int port);
    void Disconnect();
    short? ReadInt16(string address); // VD: "D100"
    bool WriteInt16(string address, short value);
}
