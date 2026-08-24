using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Represents a managed descriptor for one or more contiguous CUDA device memory allocations.
    /// A <see cref="CudaMem"/> instance groups together the device pointers, their element lengths and
    /// the element type so the buffer(s) can be tracked, transferred and freed as a float unit.
    /// A float instance can describe either one buffer (float allocation) or multiple buffers of the
    /// same element type (a group / chunked allocation).
    /// </summary>
    public class CudaMem : IRuntimeMem, IDisposable
    {
        /// <summary>
        /// Gets the unique identifier that the owning registry uses to track this memory object.
        /// </summary>
        public Guid Id { get; private set; } = Guid.NewGuid();

        /// <summary>
        /// Gets the CUDA device pointers (<see cref="CUdeviceptr"/>) for every buffer described by this instance.
        /// </summary>
        internal CUdeviceptr[] DevicePointers { get; private set; } = [];

        /// <summary>
        /// Gets the native handle of every device buffer, in <see cref="DevicePointers"/> order. This is the
        /// backend-agnostic view over the CUDA device pointers so both backends share one representation.
        /// </summary>
        public IntPtr[] PointerIds => this.DevicePointers.Select(dp => (IntPtr) dp.Pointer).ToArray();

        /// <summary>
        /// Gets the raw native handles (<see cref="IntPtr"/>) of every buffer, in the same order as <see cref="DevicePointers"/>.
        /// </summary>
        public IntPtr[] Pointers { get; private set; } = [];

        /// <summary>
        /// Gets the element count of each buffer, in the same order as <see cref="Pointers"/>.
        /// </summary>
        public long[] PointerLengths { get; private set; } = [];

        /// <summary>
        /// Gets the native handle of the first buffer, used as the primary index / lookup pointer for this allocation.
        /// </summary>
        public IntPtr IndexPointer { get; private set; } = IntPtr.Zero;

        /// <summary>
        /// Gets the element count of the first buffer (the length associated with <see cref="IndexPointer"/>).
        /// </summary>
        public IntPtr IndexLength { get; private set; } = IntPtr.Zero;

        /// <summary>
        /// Gets the .NET element <see cref="Type"/> stored in the buffer(s) (for example <see cref="float"/>).
        /// </summary>
        public Type ElementType { get; private set; } = typeof(void);

        /// <summary>
        /// Gets the size, in bytes, of a float element of <see cref="ElementType"/>.
        /// </summary>
        public int ElementSize { get; private set; } = 0;

        /// <summary>
        /// Gets the number of individual buffers described by this instance.
        /// </summary>
        public int Count { get; private set; } = 0;

        /// <summary>
        /// Gets the total number of elements across all buffers.
        /// </summary>
        public long TotalLength { get; private set; } = 0;

        /// <summary>
        /// Gets the total size, in bytes, of all buffers (<see cref="TotalLength"/> multiplied by <see cref="ElementSize"/>).
        /// </summary>
        public long TotalSize { get; private set; } = 0;

        /// <summary>
        /// Gets or sets an optional, free-form status or diagnostic message associated with this allocation.
        /// </summary>
        public string Message { get; set; } = string.Empty;


        // Enumerable
        /// <summary>
        /// Gets the <see cref="CUdeviceptr"/> that corresponds to the supplied native handle.
        /// </summary>
        /// <param name="pointer">The native handle of one of the buffers in <see cref="Pointers"/>.</param>
        /// <returns>The matching <see cref="CUdeviceptr"/>, or <c>null</c> if the handle is not part of this allocation.</returns>
        public CUdeviceptr? this[IntPtr pointer]
        {
            get
            {
                int index = Array.IndexOf(this.Pointers, pointer);
                if (index >= 0 && index < this.DevicePointers.Length)
                {
                    return this.DevicePointers[index];
                }

                return null;
            }
        }


        // Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="CudaMem"/> class describing a float device buffer.
        /// </summary>
        /// <param name="pointer">The CUDA device pointer of the allocated buffer.</param>
        /// <param name="length">The number of elements contained in the buffer.</param>
        /// <param name="type">The .NET element type stored in the buffer.</param>
        public CudaMem(CUdeviceptr pointer, IntPtr length, Type type)
        {
            this.DevicePointers = [pointer];
            this.Pointers = [pointer.Pointer];
            this.PointerLengths = [length];
            this.ElementType = type;

            this.UpdateProperties();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaMem"/> class describing a group of device buffers
        /// that all share the same element type.
        /// </summary>
        /// <param name="pointers">The CUDA device pointers of the allocated buffers.</param>
        /// <param name="lengths">The element counts for each buffer; must have the same length as <paramref name="pointers"/>.</param>
        /// <param name="type">The .NET element type stored in the buffers.</param>
        public CudaMem(CUdeviceptr[] pointers, IntPtr[] lengths, Type type)
        {
            if (pointers.Length != lengths.Length || pointers.Length <= 0 || lengths.Length <= 0)
            {
                this.Dispose();
            }

            this.DevicePointers = pointers;
            this.Pointers = pointers.Select(ptr => (IntPtr) ptr.Pointer).ToArray();
            this.PointerLengths = lengths.Select(l => (long) l).ToArray();
            this.ElementType = type;

            this.UpdateProperties();
        }




        // Methods
        /// <summary>
        /// Resets the descriptor's metadata to its empty state and suppresses finalization.
        /// Note: this clears the tracked metadata only; releasing the underlying device memory is the
        /// responsibility of the owning registry (see <c>CudaRegister.FreeMemory</c>).
        /// </summary>
        public void Dispose()
        {
            this.Pointers = [];
            this.PointerLengths = [];
            this.ElementType = typeof(void);
            this.ElementSize = 0;
            this.Count = 0;
            this.TotalLength = 0;
            this.Message = string.Empty;

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Recalculates the derived properties (<see cref="ElementSize"/>, <see cref="Count"/>, <see cref="TotalLength"/>,
        /// <see cref="TotalSize"/>, <see cref="IndexPointer"/> and <see cref="IndexLength"/>) from the current
        /// pointers, lengths and element type.
        /// </summary>
        private void UpdateProperties()
        {
            this.ElementSize = System.Runtime.InteropServices.Marshal.SizeOf(this.ElementType);
            this.Count = this.Pointers.Length;
            this.TotalLength = this.PointerLengths.Sum(len => len);
            this.TotalSize = this.TotalLength * this.ElementSize;
            this.IndexPointer = this.Pointers.FirstOrDefault(IntPtr.Zero);
            this.IndexLength = (IntPtr) this.PointerLengths.FirstOrDefault(0);
        }

    }

}
