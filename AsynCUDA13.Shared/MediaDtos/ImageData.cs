using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class ImageData
    {
        /// <summary>
        /// Gets or sets the image information, which includes metadata such as ID, dimensions, and pointer.
        /// </summary>
        public required ImageInfo Info { get; set; }

        /// <summary>
        /// Gets the pointer associated with the image, which can be used for GPU memory management or other purposes.
        /// </summary>
        public string? Pointer => this.Info.Pointer;

        /// <summary>
        /// Gets or sets the MIME type of the image, such as "image/png" or "image/jpeg".
        /// </summary>
        public string MimeType { get; set; } = "image/png";

        /// <summary>
        /// Gets or sets the Base64-encoded image data.
        /// </summary>
        public string Base64Data { get; set; } = string.Empty;

        /// <summary>
        /// Gets the Base64-encoded image data as a data URL, which can be used directly in HTML img elements.
        /// </summary>
        public string Base64Image => $"data:{this.MimeType};base64,{this.Base64Data}";

        /// <summary>
        /// Gets the size of the Base64-encoded image data in megabytes.
        /// </summary>
        public float DataSizeMb => this.Base64Data.LongCount() * 4f / 3f / 1024f / 1024f;

        /// <summary>
        /// Determines whether the image ID matches the specified ID, optionally requiring the image to be on the GPU.
        /// </summary>
        /// <param name="id">The ID to compare with the image's ID.</param>
        /// <param name="requireOnGpu">If true, the image must be on the GPU to match.</param>
        /// <returns>True if the IDs match and the GPU requirement is met; otherwise, false.</returns>
        public bool IdMatch(string id, bool requireOnGpu = false)
        {
            return this.Info.IdMatch(id, requireOnGpu);
        }
    }
}
