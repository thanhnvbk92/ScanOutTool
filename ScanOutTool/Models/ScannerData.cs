using System;

namespace ScanOutTool.Models
{
    /// <summary>
    /// Represents parsed scan data from scanner device
    /// Format: PID|slot_qty (e.g., "509HS123456|18")
    /// </summary>
    public class ScannerData
    {
        public string PID { get; set; } = string.Empty;
        public int SlotQuantity { get; set; }
        public string RawData { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Parse scanner input data in format: PID|slot_qty
        /// </summary>
        public static ScannerData Parse(string rawData)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawData))
                {
                    return new ScannerData
                    {
                        RawData = rawData ?? "",
                        IsValid = false,
                        ErrorMessage = "Empty or null data"
                    };
                }

                var trimmedData = rawData.Trim();
                
                // Check if data contains pipe separator
                if (!trimmedData.Contains('|'))
                {
                    // Backward compatibility: treat as PID only
                    return new ScannerData
                    {
                        PID = trimmedData,
                        SlotQuantity = 0, // Default quantity
                        RawData = rawData,
                        IsValid = true,
                        ErrorMessage = ""
                    };
                }

                var parts = trimmedData.Split('|');
                if (parts.Length != 2)
                {
                    return new ScannerData
                    {
                        RawData = rawData,
                        IsValid = false,
                        ErrorMessage = $"Invalid format. Expected: PID|quantity, got: {rawData}"
                    };
                }

                var pid = parts[0].Trim();
                var qtyString = parts[1].Trim();

                if (string.IsNullOrEmpty(pid))
                {
                    return new ScannerData
                    {
                        RawData = rawData,
                        IsValid = false,
                        ErrorMessage = "PID cannot be empty"
                    };
                }

                if (!int.TryParse(qtyString, out int quantity) || quantity < 0)
                {
                    return new ScannerData
                    {
                        RawData = rawData,
                        IsValid = false,
                        ErrorMessage = $"Invalid quantity: {qtyString}. Must be a non-negative integer."
                    };
                }

                return new ScannerData
                {
                    PID = pid,
                    SlotQuantity = quantity,
                    RawData = rawData,
                    IsValid = true,
                    ErrorMessage = ""
                };
            }
            catch (Exception ex)
            {
                return new ScannerData
                {
                    RawData = rawData ?? "",
                    IsValid = false,
                    ErrorMessage = $"Parse error: {ex.Message}"
                };
            }
        }

        public override string ToString()
        {
            return IsValid ? $"PID: {PID}, Qty: {SlotQuantity}" : $"Invalid: {ErrorMessage}";
        }
    }
}