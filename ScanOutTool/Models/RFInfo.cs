using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ScanOutTool.Models
{
    public class RFInfo
    {
        public int Id { get; set; }
        public string Pid { get; set; }
        public string Model { get; set; }
        public string Supplier { get; set; }

        [JsonPropertyName("valuE_NG")]
        public string ValueNgRaw { get; set; }
        public double ValueNg => double.Parse(ValueNgRaw);

        public string Freq { get; set; }
        public string Band { get; set; }
        public string Subband { get; set; }

        [JsonPropertyName("gaiN_STATE")]
        public string GainState { get; set; }

        public string Ucl { get; set; }
        public string Lcl { get; set; }

        [JsonPropertyName("clienT_CHECKED")]
        [JsonConverter(typeof(IntToBoolConverter))]
        public bool ClientChecked { get; set; }

        [JsonPropertyName("weB_CHECKED")]
        [JsonConverter(typeof(IntToBoolConverter))]
        public bool WebChecked { get; set; }

        [JsonPropertyName("anT_NUMBER")]
        public string AntNumber { get; set; }

        public string Market { get; set; }
        public string Station { get; set; }

        public string Signpath { get; set; }

        [JsonPropertyName("creatE_USER")]
        public string CreateUser { get; set; }
        public string MachineIP => CreateUser.Split("-")[0];

        [JsonPropertyName("creatE_TIME")]
        [JsonConverter(typeof(CustomDateTimeConverter))]
        public DateTime CreateTime { get; set; }

        [JsonPropertyName("cleared")]
        [JsonConverter(typeof(IntToBoolConverter))]
        public bool Cleared { get; set; }
    }


    public class CustomDateTimeConverter : JsonConverter<DateTime>
    {
        private const string DateFormat = "dd-MM-yyyy HH:mm:ss";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? dateStr = reader.GetString();

            if (DateTime.TryParseExact(dateStr, DateFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                return dt;
            }

            throw new JsonException($"Unable to parse DateTime: {dateStr}");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(DateFormat));
        }
    }


    public class IntToBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();
                return value == "1";
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                return intValue == 1;
            }
            else
            {
                throw new JsonException($"Unexpected token type {reader.TokenType} for bool conversion.");
            }
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value ? "1" : "0");
        }
    }

}
