using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public class DecimalFormatConverter : JsonConverter<decimal>
{
    private readonly string _format;

    public DecimalFormatConverter(string format) => _format = format;

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(_format, CultureInfo.InvariantCulture));
    }

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDecimal();
    }
}