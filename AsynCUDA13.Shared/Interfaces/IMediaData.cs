using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Interface for media data (AudioData and ImageData).
    /// Mediendaten für Bildübertragung. Includes JsonConverter attributes for proper serialization in WebAPI/nswag/Blazor architectures.
    /// The System.Text.Json and Newtonsoft.Json converters enable proper serialization
    /// when these DTOs are returned or received by API controllers in WebAPI/nswag/Blazor scenarios.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(AsynCUDA13.Shared.Api.Payloads.MediaDataSystemTextConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(AsynCUDA13.Shared.Api.Payloads.MediaDataNewtonsoftConverter))]
    public interface IMediaData
    {
        /// <summary>
        /// Gets or sets the media info associated with the data.
        /// </summary>
        IMediaInfo Info { get; set; }

        /// <summary>
        /// Gets or sets the pointer associated with the media data, used for GPU memory management.
        /// </summary>
        string? Pointer { get; }

        /// <summary>
        /// Gets the MIME type of the media data.
        /// </summary>
        string MimeType { get; }

        /// <summary>
        /// Gets or sets the Base64-encoded media data.
        /// </summary>
        string Base64Data { get; set; }

        /// <summary>
        /// Determines whether the media data is on GPU based on the pointer value.
        /// </summary>
        bool OnGpu { get; }
    }
}