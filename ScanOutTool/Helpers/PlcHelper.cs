using McpXLib.Enums;
using McpXLib;
using System.Text.RegularExpressions;
using System;

public class PlcHelper : IDisposable
{
    private readonly McpX _plc;

    public PlcHelper(string ip, int port, bool isAscii = false)
    {
        _plc = new McpX(ip, port, null, isAscii);
    }

    public bool CheckConnection()
    {
        try
        {
            _ = _plc.BatchRead<ushort>(Prefix.D, "0", 1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public ushort ReadWord(string address)
    {
        ParseAddress(address, out var prefix, out var addr);
        var result = _plc.BatchRead<ushort>(prefix, addr.ToString(), 1);
        return result[0];
    }

    public ushort[] ReadWords(string address, ushort count)
    {
        ParseAddress(address, out var prefix, out var addr);
        return _plc.BatchRead<ushort>(prefix, addr.ToString(), count);
    }

    public bool ReadBit(string address)
    {
        ParseAddress(address, out var prefix, out var addr);
        var result = _plc.BatchRead<bool>(prefix, addr.ToString(), 1);
        return result[0];
    }

    public bool[] ReadBits(string address, ushort count)
    {
        ParseAddress(address, out var prefix, out var addr);
        return _plc.BatchRead<bool>(prefix, addr.ToString(), count);
    }

    public void WriteWord(string address, ushort value)
    {
        ParseAddress(address, out var prefix, out var addr);
        _plc.BatchWrite(prefix, addr.ToString(), new ushort[] { value });
    }

    public void WriteBit(string address, bool value)
    {
        ParseAddress(address, out var prefix, out var addr);
        _plc.BatchWrite(prefix, addr.ToString(), new bool[] { value });
    }

    private void ParseAddress(string address, out Prefix prefix, out int number)
    {
        var match = Regex.Match(address.ToUpper(), @"^([DMXYB])(\d+)$");
        if (!match.Success)
            throw new ArgumentException($"Địa chỉ không hợp lệ: {address}");

        prefix = match.Groups[1].Value switch
        {
            "D" => Prefix.D,
            "M" => Prefix.M,
            "X" => Prefix.X,
            "Y" => Prefix.Y,
            "B" => Prefix.B,
            _ => throw new NotSupportedException($"Loại thiết bị không hỗ trợ: {match.Groups[1].Value}")
        };

        number = int.Parse(match.Groups[2].Value);
    }

    public void Dispose()
    {
        _plc?.Dispose();
    }
}
