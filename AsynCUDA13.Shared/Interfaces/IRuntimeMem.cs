using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Defines a common interface for memory objects that can be allocated and managed by the runtime. This interface provides properties to access metadata about the memory, such as its unique identifier, element type, size, and buffer handles.
    /// </summary>
    public interface IRuntimeMem
    {
        /// <summary>
        /// Gets the unique identifier that the owning registry uses to track this memory object.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Gets the .NET element <see cref="Type"/> stored in the buffer(s) (for example <see cref="float"/>).
        /// </summary>
        Type ElementType { get; }

        /// <summary>
        /// Gets the size, in bytes, of a float element of <see cref="ElementType"/>.
        /// </summary>
        int ElementSize { get; }

        /// <summary>
        /// Gets the number of individual buffers described by this instance.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets the total number of elements across all buffers.
        /// </summary>
        long TotalLength { get; }

        /// <summary>
        /// Gets the total size, in bytes, of all buffers.
        /// </summary>
        long TotalSize { get; }

        /// <summary>
        /// Gets the native handle of every buffer, in buffer order. This is the backend-agnostic view over
        /// the CUDA device pointers or OpenCL buffer handles so both backends share one representation.
        /// </summary>
        IntPtr[] PointerIds { get; }

        /// <summary>
        /// Gets the native handle of the index buffer, if any. This is the backend-agnostic view over
        /// </summary>
        IntPtr IndexPointer { get; }

        /// <summary>
        /// Gets the element count of each buffer, in the same order as <see cref="PointerIds"/>.
        /// </summary>
        long[] PointerLengths { get; }

        /// <summary>
        /// Gets the element count of the index buffer, if any.
        /// </summary>
        long IndexLength { get; }

        /// <summary>
        /// Gets or sets an optional, free-form status or diagnostic message associated with this allocation.
        /// </summary>
        string Message { get; set; }

        /// <summary>
        /// Gets or sets an optional asset reference Id for reverse assignment MemoryObj -> Asset by its Id
        /// </summary>
        Guid? AssetReferenceId { get; set; }

        /// <summary>
        /// Disposes of the memory object, releasing any associated resources. Returns the total number of bytes that were freed as a result of the disposal.
        /// </summary>
        /// <returns>The total number of bytes that were freed as a result of the disposal.</returns>
        long Dispose();
    }
}
