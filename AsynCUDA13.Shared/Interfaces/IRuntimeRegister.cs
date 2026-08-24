using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// This interface is used to mark classes that can be registered at runtime.
    /// </summary>
    public interface IRuntimeRegister
    {
        /// <summary>
        /// Gets the collection of memory allocations managed by this register.
        /// </summary>
        IReadOnlyCollection<IRuntimeMem> Allocations { get; }

        /// <summary>
        /// Gets the count of memory allocations managed by this register.
        /// </summary>
        int AllocationCount { get; }

        /// <summary>
        /// Gets the total number of bytes currently allocated across all tracked memory objects.
        /// </summary>
        long TotalAllocatedBytes { get; }


        /// <summary>
        /// Gets the memory object associated with the specified index pointer, if it exists; otherwise, returns null.
        /// </summary>
        /// <param name="indexPointer">The index pointer of the memory object to retrieve.</param>
        /// <returns>The memory object associated with the specified index pointer, or null if it does not exist.</returns>
        IRuntimeMem? this[IntPtr indexPointer] { get; }

        /// <summary>
        /// Gets the memory object associated with the specified unique identifier, if it exists; otherwise, returns null.
        /// </summary>
        /// <param name="id">The unique identifier of the memory object to retrieve.</param>
        /// <returns>The memory object associated with the specified unique identifier, or null if it does not exist.</returns>
        IRuntimeMem? this[Guid id] { get; }




        long FreeMemory(Guid id);
        long FreeMemory(IRuntimeMem mem);
        long FreeMemory(IntPtr indexPointer);


        IRuntimeMem? AllocateGroup<T>(IntPtr[] lengths) where T : unmanaged;
        Task<IRuntimeMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged;
        IRuntimeMem? AllocateSingle<T>(IntPtr length) where T : unmanaged;
        Task<IRuntimeMem?> AllocateSingleAsync<T>(IntPtr length) where T : unmanaged;


        List<T[]> PullChunks<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged;
        Task<List<T[]>> PullChunksAsync<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged;
        T[] PullData<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged;
        Task<T[]> PullDataAsync<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged;
        IRuntimeMem? PushChunks<T>(IEnumerable<IEnumerable<T>> chunks) where T : unmanaged;
        Task<IRuntimeMem?> PushChunksAsync<T>(IEnumerable<IEnumerable<T>> chunks) where T : unmanaged;
        IRuntimeMem? PushData<T>(IEnumerable<T> data) where T : unmanaged;
        Task<IRuntimeMem?> PushDataAsync<T>(IEnumerable<T> data) where T : unmanaged;


        /// <summary>
        /// Disposes of the resources used by the runtime register, releasing any allocated memory and cleaning up any associated resources.
        /// </summary>
        void Dispose();
    }
}
