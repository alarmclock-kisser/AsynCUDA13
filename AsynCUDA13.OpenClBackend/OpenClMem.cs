using System;
using System.Linq;
using AsynCUDA13.Shared.Interfaces;
using OpenTK.Compute.OpenCL;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Represents a managed descriptor for one or more OpenCL device buffers.
    /// An <see cref="OpenClMem"/> instance groups together the buffer handles, their element lengths and
    /// the element type so the buffer(s) can be tracked, transferred and freed as a single unit.
    /// A single instance can describe either one buffer (single allocation) or multiple buffers of the
    /// same element type (a group / chunked allocation).
    /// </summary>
    public sealed class OpenClMem : IRuntimeMem
    {
        private bool _disposed;

        /// <summary>
        /// Gets the unique identifier that the owning registry uses to track this memory object.
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// Gets the OpenCL buffer handles for every buffer described by this instance.
        /// </summary>
        public CLBuffer[] Buffers { get; private set; } = [];

        /// <summary>
        /// Gets the element count of each buffer, in the same order as <see cref="Buffers"/>.
        /// </summary>
        public long[] PointerLengths { get; private set; } = [];

        /// <summary>
        /// Gets the raw native handles (<see cref="nint"/>) of every buffer, in the same order as <see cref="Buffers"/>.
        /// </summary>
        public IntPtr[] PointerIds { get; private set; } = [];

        /// <summary>
        /// Gets the handle of the first buffer, used as the primary index / lookup handle for this allocation.
        /// </summary>
        public CLBuffer IndexBuffer { get; private set; }

        /// <summary>
        /// Gets the native handle of the first buffer, used as the primary index / lookup pointer for this allocation.
        /// This is the OpenCL counterpart to the CUDA <c>IndexPointer</c> and enables by-pointer lookups.
        /// </summary>
        public nint IndexPointer { get; private set; }

        /// <summary>
        /// Gets the element count of the first buffer (the length associated with <see cref="IndexBuffer"/>).
        /// </summary>
        public long IndexLength { get; private set; }

        /// <summary>
        /// Gets the .NET element <see cref="Type"/> stored in the buffer(s) (for example <see cref="float"/>).
        /// </summary>
        public Type ElementType { get; private set; } = typeof(void);

        /// <summary>
        /// Gets the size, in bytes, of a single element of <see cref="ElementType"/>.
        /// </summary>
        public int ElementSize { get; private set; }

        /// <summary>
        /// Gets the number of individual buffers described by this instance.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the total number of elements across all buffers.
        /// </summary>
        public long TotalLength { get; private set; }

        /// <summary>
        /// Gets the total size, in bytes, of all buffers (<see cref="TotalLength"/> multiplied by <see cref="ElementSize"/>).
        /// </summary>
        public long TotalSize { get; private set; }

        /// <summary>
        /// Gets or sets an optional, free-form status or diagnostic message associated with this allocation.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets a value indicating whether this allocation has already been freed.
        /// </summary>
        public bool IsDisposed => this._disposed;



        // Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClMem"/> class describing a single device buffer.
        /// </summary>
        /// <param name="buffer">The OpenCL buffer handle of the allocated buffer.</param>
        /// <param name="length">The number of elements contained in the buffer.</param>
        /// <param name="type">The .NET element type stored in the buffer.</param>
        public OpenClMem(CLBuffer buffer, long length, Type type)
        {
            this.Buffers = [buffer];
            this.PointerLengths = [length];
            this.ElementType = type;

            this.UpdateProperties();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClMem"/> class describing a group of device buffers
        /// that all share the same element type.
        /// </summary>
        /// <param name="buffers">The OpenCL buffer handles.</param>
        /// <param name="lengths">The element counts of each buffer, in the same order as <paramref name="buffers"/>.</param>
        /// <param name="type">The .NET element type stored in the buffers.</param>
        public OpenClMem(CLBuffer[] buffers, long[] lengths, Type type)
        {
            if (buffers.Length != lengths.Length)
            {
                throw new ArgumentException("The number of buffers and lengths must match.", nameof(lengths));
            }

            this.Buffers = buffers;
            this.PointerLengths = lengths;
            this.ElementType = type;

            this.UpdateProperties();
        }



        // Properties
        /// <summary>
        /// Recomputes the derived size and index properties from the current buffers, lengths and element type.
        /// </summary>
        private void UpdateProperties()
        {
            this.Count = this.Buffers.Length;
            this.ElementSize = this.ElementType == typeof(void) ? 0 : System.Runtime.InteropServices.Marshal.SizeOf(this.ElementType);
            this.TotalLength = this.PointerLengths.Sum();
            this.TotalSize = this.TotalLength * this.ElementSize;

            this.PointerIds = new nint[this.Count];
            for (int i = 0; i < this.Count; i++)
            {
                this.PointerIds[i] = this.Buffers[i].Handle;
            }

            if (this.Count > 0)
            {
                this.IndexBuffer = this.Buffers[0];
                this.IndexLength = this.PointerLengths[0];
                this.IndexPointer = this.Buffers[0].Handle;
            }
            else
            {
                this.IndexBuffer = default;
                this.IndexLength = 0;
                this.IndexPointer = 0;
            }
        }



        // Disposal
        /// <summary>
        /// Releases all OpenCL buffer objects described by this instance.
        /// </summary>
        public long Dispose()
        {
            if (this._disposed)
            {
                return 0;
            }

            long disposedBytes = this.TotalSize;
            foreach (var buffer in this.Buffers)
            {
                try
                {
                    CL.ReleaseMemoryObject(buffer);
                }
                catch
                {
                }
            }

            this.Buffers = [];
            this.PointerLengths = [];
            this.PointerIds = [];
            this.Count = 0;
            this.TotalLength = 0;
            this.TotalSize = 0;
            this._disposed = true;

            return disposedBytes;
        }
    }
}
