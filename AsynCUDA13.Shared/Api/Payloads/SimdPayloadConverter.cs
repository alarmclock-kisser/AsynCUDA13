using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class SimdPayloadConverter : JsonConverter<ISimdPayload>
    {
        public override ISimdPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // 1. Prüfen, ob ein $type Discriminator existiert
            if (root.TryGetProperty("$type", out var typeProp))
            {
                string typeName = typeProp.GetString() ?? string.Empty;
                if (typeName.Contains("SimdPayload2D", StringComparison.OrdinalIgnoreCase))
                {
                    return root.Deserialize<SimdPayload2D>(options);
                }
                if (typeName.Contains("SimdPayload1D", StringComparison.OrdinalIgnoreCase))
                {
                    return root.Deserialize<SimdPayload1D>(options);
                }
            }

            // 2. Fallback-Erkennung anhand der JSON-Eigenschaften
            return root.TryGetProperty("Data2D", out _) || root.TryGetProperty("data2D", out _) || root.TryGetProperty("Chunks", out _)
                ?  root.Deserialize<SimdPayload2D>(options)
                :  root.Deserialize<SimdPayload1D>(options);
        }

        public override void Write(Utf8JsonWriter writer, ISimdPayload value, JsonSerializerOptions options)
        {
            if (value is SimdPayload1D p1)
            {
                JsonSerializer.Serialize(writer, p1, options);
            }
            else if (value is SimdPayload2D p2)
            {
                JsonSerializer.Serialize(writer, p2, options);
            }
            else
            {
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }
}