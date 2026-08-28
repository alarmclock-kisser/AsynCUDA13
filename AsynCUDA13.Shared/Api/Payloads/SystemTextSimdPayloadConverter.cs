using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class SystemTextSimdPayloadConverter : JsonConverter<ISimdPayload>
    {
        public override ISimdPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string json = root.GetRawText();

            bool is2D = root.TryGetProperty("data2D", out _) || root.TryGetProperty("Data2D", out _) ||
                        root.TryGetProperty("chunks", out _) || root.TryGetProperty("Chunks", out _);

            return is2D
                ? JsonSerializer.Deserialize<SimdPayload2D>(json, options)
                : JsonSerializer.Deserialize<SimdPayload1D>(json, options);
        }

        public override void Write(Utf8JsonWriter writer, ISimdPayload value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}