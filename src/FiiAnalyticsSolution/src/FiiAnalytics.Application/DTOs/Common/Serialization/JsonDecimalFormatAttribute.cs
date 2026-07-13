using System.Text.Json.Serialization;

[AttributeUsage(AttributeTargets.Property)]
public class JsonDecimalFormatAttribute : JsonConverterAttribute
{
    public string Format { get; }
    public JsonDecimalFormatAttribute(string format) => Format = format;

    public override JsonConverter? CreateConverter(Type typeToConvert)
    {
        return new DecimalFormatConverter(Format);
    }
}