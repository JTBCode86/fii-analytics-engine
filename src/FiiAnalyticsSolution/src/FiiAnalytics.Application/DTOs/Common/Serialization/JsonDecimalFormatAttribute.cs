using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;

[AttributeUsage(AttributeTargets.Property)]
public class JsonDecimalFormatAttribute : JsonConverterAttribute
{
    public string Format { get; }
    public JsonDecimalFormatAttribute(string format) => Format = format;

    public override JsonConverter? CreateConverter(Type typeToConvert)
    {
        if (typeToConvert == typeof(decimal?)) return new NullableDecimalFormatConverter(Format);
        return new DecimalFormatConverter(Format);
    }

    private sealed class NullableDecimalFormatConverter(string format) : JsonConverter<decimal?>
    {
        private readonly DecimalFormatConverter _converter = new(format);

        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => decimal.Parse(reader.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
                _ => _converter.Read(ref reader, typeof(decimal), options)
            };

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value.HasValue) _converter.Write(writer, value.Value, options);
            else writer.WriteNullValue();
        }
    }
}