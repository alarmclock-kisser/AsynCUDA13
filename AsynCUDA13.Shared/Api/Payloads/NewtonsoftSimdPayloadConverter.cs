using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class NewtonsoftSimdPayloadConverter : JsonConverter<ISimdPayload>
    {
        public override ISimdPayload? ReadJson(JsonReader reader, Type objectType, ISimdPayload? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            JObject jsonObject = JObject.Load(reader);
            bool is2D = jsonObject.GetValue("data2D", StringComparison.OrdinalIgnoreCase) != null ||
                        jsonObject.GetValue("dataChunks", StringComparison.OrdinalIgnoreCase) != null ||
                        jsonObject.GetValue("chunks", StringComparison.OrdinalIgnoreCase) != null;

            ISimdPayload target = is2D ? new SimdPayload2D() : new SimdPayload1D();
            serializer.Populate(jsonObject.CreateReader(), target);
            return target;
        }

        public override void WriteJson(JsonWriter writer, ISimdPayload? value, JsonSerializer serializer)
        {
            switch (value)
            {
                case null:
                    writer.WriteNull();
                    break;
                case SimdPayload1D payload:
                    serializer.Serialize(writer, new { payload.ElementType, payload.AsyncCall, payload.Data });
                    break;
                case SimdPayload2D payload:
                    serializer.Serialize(writer, new { payload.ElementType, payload.AsyncCall, payload.DataChunks });
                    break;
                default:
                    throw new JsonSerializationException($"Unsupported SIMD payload type '{value.GetType().FullName}'.");
            }
        }
    }
}