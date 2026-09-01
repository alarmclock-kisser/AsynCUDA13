using AsynCUDA13.Shared.Api.Payloads;
using Newtonsoft.Json;

namespace AsynCUDA13.Client
{
    public partial class InternalClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        {
            // Register polymorphic converters explicitly so that Newtonsoft resolves
            // concrete types (ImageInfo, AudioInfo, ImageData, AudioData) when deserializing
            // interface-typed collections like ICollection<IMediaInfo>.
            if (!settings.Converters.Any(c => c is MediaInfoNewtonsoftConverter))
            {
                settings.Converters.Add(new MediaInfoNewtonsoftConverter());
            }
            if (!settings.Converters.Any(c => c is MediaDataNewtonsoftConverter))
            {
                settings.Converters.Add(new MediaDataNewtonsoftConverter());
            }
        }
    }
}
