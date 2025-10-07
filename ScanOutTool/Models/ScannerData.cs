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
        /// Parse scanner input data in format: PID|data
        /// Only validates PID part (11 or 22 chars), data part can be anything
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
                    // ✅ ENHANCED: Support multiple legacy formats
                    if (trimmedData.Length == 11 || trimmedData.Length == 22)
                    {
                        // Standard PID format (11 or 22 chars)
                        return new ScannerData
                        {
                            PID = trimmedData,
                            SlotQuantity = 0,
                            RawData = rawData,
                            IsValid = true,
                            ErrorMessage = ""
                        };
                    }
                    else if (trimmedData.Length > 11)
                    {
                        // ✅ NEW: PID(11)+suffix format (e.g., "12345678901I14")
                        var pidPart = trimmedData.Substring(0, 11);
                        var suffixPart = trimmedData.Substring(11);
                        
                        // Try to extract quantity from suffix
                        int quantityValue = 0;
                        var numericPart = System.Text.RegularExpressions.Regex.Match(suffixPart, @"\d+").Value;
                        if (!string.IsNullOrEmpty(numericPart))
                        {
                            int.TryParse(numericPart, out quantityValue);
                        }
                        
                        return new ScannerData
                        {
                            PID = pidPart,
                            SlotQuantity = quantityValue,
                            RawData = rawData,
                            IsValid = true,
                            ErrorMessage = ""
                        };
                    }
                    else if (trimmedData.Length > 22)
                    {
                        // ✅ NEW: PID(22)+suffix format
                        var pidPart22 = trimmedData.Substring(0, 22);
                        var suffixPart22 = trimmedData.Substring(22);
                        
                        // Try to extract quantity from suffix
                        int quantityValue22 = 0;
                        var numericPart22 = System.Text.RegularExpressions.Regex.Match(suffixPart22, @"\d+").Value;
                        if (!string.IsNullOrEmpty(numericPart22))
                        {
                            int.TryParse(numericPart22, out quantityValue22);
                        }
                        
                        return new ScannerData
                        {
                            PID = pidPart22,
                            SlotQuantity = quantityValue22,
                            RawData = rawData,
                            IsValid = true,
                            ErrorMessage = ""
                        };
                    }
                    else
                    {
                        return new ScannerData
                        {
                            RawData = rawData,
                            IsValid = false,
                            ErrorMessage = $"Invalid data length: {trimmedData.Length}. Expected >= 11 characters."
                        };
                    }
                }

                var parts = trimmedData.Split('|');
                if (parts.Length < 2)
                {
                    return new ScannerData
                    {
                        RawData = rawData,
                        IsValid = false,
                        ErrorMessage = $"Invalid format. Expected: PID|data, got: {rawData}"
                    };
                }

                var pid = parts[0].Trim();
                var dataPart = parts[1].Trim();

                // ✅ SIMPLIFIED: Only validate PID length (11 or 22 chars)
                if (pid.Length != 11 && pid.Length != 22)
                {
                    return new ScannerData
                    {
                        RawData = rawData,
                        IsValid = false,
                        ErrorMessage = $"Invalid PID length: {pid.Length}. Expected 11 or 22 characters."
                    };
                }

                // ✅ TRY to parse data part as quantity, but don't fail if it's not numeric
                int quantity = 0;
                if (!string.IsNullOrEmpty(dataPart))
                {
                    // Try to extract numeric part from data (e.g., "14" from "14", "I14", "abc14def")
                    var numericPart = System.Text.RegularExpressions.Regex.Match(dataPart, @"\d+").Value;
                    if (!string.IsNullOrEmpty(numericPart))
                    {
                        int.TryParse(numericPart, out quantity);
                    }
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