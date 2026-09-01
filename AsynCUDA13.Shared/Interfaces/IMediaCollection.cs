using System.Collections.Generic;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Interface for media collections (AudioCollection and ImageCollection).
    /// Provides common properties and methods for collections of media objects.
    /// </summary>
    public interface IMediaCollection
    {
        /// <summary>
        /// Gets the read-only collection of media objects.
        /// </summary>
        IReadOnlyCollection<IMediaObj> Objects { get; }

        /// <summary>
        /// Gets or sets the export directory for the media collection.
        /// </summary>
        string ExportDirectory { get; set; }

        /// <summary>
        /// Indexer to access a media object by Guid.
        /// </summary>
        IMediaObj? this[Guid id] { get; }

        /// <summary>
        /// Indexer to access a media object by index.
        /// </summary>
        IMediaObj? this[int index] { get; }
    }
}