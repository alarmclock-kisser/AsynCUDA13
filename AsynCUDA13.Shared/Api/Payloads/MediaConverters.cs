using System;
using System.Text.Json.Serialization;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.Shared.Api.Payloads
{
    /// <summary>
    /// Thin System.Text.Json converter for IMediaInfo using the generic polymorphic base.
    /// Audio is detected via SampleRate/Bpm properties or MediaType="audio".
    /// </summary>
    public class MediaInfoSystemTextConverter : SystemTextPolymorphicConverter<IMediaInfo>
    {
        public MediaInfoSystemTextConverter()
            : base(new[] { "SampleRate", "sampleRate", "Bpm", "bpm" }, fallbackProperty: "MediaType", fallbackValue: "audio")
        {
        }
    }

    /// <summary>
    /// Thin Newtonsoft.Json converter for IMediaInfo using the generic polymorphic base.
    /// Audio is detected via SampleRate/Bpm properties or MediaType="audio".
    /// </summary>
    public class MediaInfoNewtonsoftConverter : NewtonsoftPolymorphicConverter<IMediaInfo>
    {
        public MediaInfoNewtonsoftConverter()
            : base(new[] { "SampleRate", "sampleRate", "Bpm", "bpm" }, fallbackProperty: "MediaType", fallbackValue: "audio")
        {
        }
    }

    /// <summary>
    /// Thin System.Text.Json converter for IMediaData using the generic polymorphic base.
    /// Audio is detected via AudioDataFloats property or nested Info with SampleRate/Bpm.
    /// </summary>
    public class MediaDataSystemTextConverter : SystemTextPolymorphicConverter<IMediaData>
    {
        public MediaDataSystemTextConverter()
            : base(new[] { "AudioDataFloats", "audioDataFloats" })
        {
        }
    }

    /// <summary>
    /// Thin Newtonsoft.Json converter for IMediaData using the generic polymorphic base.
    /// Audio is detected via AudioDataFloats property or nested Info with SampleRate/Bpm.
    /// </summary>
    public class MediaDataNewtonsoftConverter : NewtonsoftPolymorphicConverter<IMediaData>
    {
        public MediaDataNewtonsoftConverter()
            : base(new[] { "AudioDataFloats", "audioDataFloats" })
        {
        }
    }
}
