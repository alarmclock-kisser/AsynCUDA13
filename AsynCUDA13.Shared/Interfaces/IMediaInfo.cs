using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Interface for media info (AudioInfo and ImageInfo).
    /// Audiometadaten. Includes JsonConverter attributes for proper serialization in WebAPI/nswag/Blazor architectures.
    /// The System.Text.Json and Newtonsoft.Json converters enable proper serialization
    /// when these DTOs are returned or received by API controllers in WebAPI/nswag/Blazor scenarios.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(AsynCUDA13.Shared.Api.Payloads.MediaInfoSystemTextConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(AsynCUDA13.Shared.Api.Payloads.MediaInfoNewtonsoftConverter))]
    public interface IMediaInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for the media info.
        /// </summary>
        Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the creation timestamp of the media info.
        /// </summary>
        DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the name of the media file.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// Gets or sets the file path of the media info.
        /// </summary>
        string FilePath { get; set; }

        /// <summary>
        /// Determines if the media ID matches the provided ID, with optional GPU requirement.
        /// </summary>
        /// <param name="id">The ID to match against.</param>
        /// <param name="requireOnGpu">Whether to require the media to be on GPU.</param>
        /// <returns>True if the IDs match and GPU requirement is satisfied.</returns>
        bool IdMatch(string id, bool requireOnGpu = false);

        /// <summary>
        /// Gets or sets the pointer associated with the media info, used for GPU memory management.
        /// </summary>
        string? Pointer { get; set; }
    }
}