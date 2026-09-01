using System;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Interface for media objects (AudioObj and ImageObj).
    /// Provides common properties for media objects such as ID, creation time, name, and file path.
    /// </summary>
    public interface IMediaObj
    {
        /// <summary>
        /// Gets the unique identifier assigned when the media object is created.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Gets the timestamp assigned when the media object is created.
        /// </summary>
        DateTime CreatedAt { get; }

        /// <summary>
        /// Gets or sets the name of the media file.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// Gets or sets the file path of the media.
        /// </summary>
        string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the Pointer, if set, the media object's data is most probably on the GPU / e.g. an Accelerator-Runtime-Device
        /// </summary>
        long Pointer { get; set; }
    }
}