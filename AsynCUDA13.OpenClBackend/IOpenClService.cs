using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Interface for the OpenCL service — the OpenCL counterpart to <see cref="AsynCUDA13.Runtime.ICudaService"/>.
    /// Exposes device selection, FFT/IFFT, image-kernel dispatch and memory transfer (push/pull/allocate/free)
    /// that operate directly on <see cref="float"/> and <see cref="Vector2"/> arrays, mirroring the CUDA service.
    /// </summary>
    public interface IOpenClService : IRuntimeService
    {
        /// <summary>
        /// Gets all OpenCL devices available on the machine, each identified by a flat <see cref="OpenClDevice.Index"/>.
        /// </summary>
        IReadOnlyList<OpenClDevice> AvailableDevices { get; }


        /// <summary>
        /// Gets the properties of the specified OpenCL device.
        /// </summary>
        /// <param name="deviceIndex">The flat device index from <see cref="AvailableDevices"/>. If <c>null</c>, the currently selected device is used.</param>
        /// <returns>A dictionary of property names and values.</returns>
        Dictionary<string, string> GetDeviceProperties(int? deviceIndex = null);

        /// <summary>
        /// Gets the number of available OpenCL devices.
        /// </summary>
        int DeviceCount { get; }

        /// <summary>
        /// Initializes the first device whose name contains <paramref name="deviceName"/> (case-insensitive).
        /// </summary>
        /// <param name="deviceName">A case-insensitive substring of the desired device name.</param>
        /// <returns><c>true</c> if a matching device was initialized; otherwise <c>false</c>.</returns>
        bool Initialize(string deviceName);

        /// <summary>
        /// Releases the current device context, command queue, compiler and all allocations, leaving the
        /// service offline but reusable.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Runs a full forward FFT over an arbitrary-length array. See <see cref="OpenClFourier.Fft(float[])"/>.
        /// </summary>
        Vector2[]? Fft(float[] data);

        /// <summary>
        /// Runs a full inverse FFT over an arbitrary-length array. See <see cref="OpenClFourier.Ifft(Vector2[], int)"/>.
        /// </summary>
        float[]? Ifft(Vector2[] data, int originalLength = -1);

        /// <summary>
        /// Runs a chunked forward FFT. See <see cref="OpenClFourier.FftChunked(float[], int, int)"/>.
        /// </summary>
        Vector2[]? FftChunked(float[] data, int chunkSize = OpenClFourier.DefaultChunkSize, int overlap = 0);

        /// <summary>
        /// Runs a chunked inverse FFT. See <see cref="OpenClFourier.IfftChunked(Vector2[], int, int)"/>.
        /// </summary>
        float[]? IfftChunked(Vector2[] data, int chunkSize = OpenClFourier.DefaultChunkSize, int overlap = 0);

        /// <summary>
        /// Asynchronously runs a full forward FFT. See <see cref="OpenClFourier.FftAsync(float[])"/>.
        /// </summary>
        Task<Vector2[]?> FftAsync(float[] data);

        /// <summary>
        /// Asynchronously runs a full inverse FFT. See <see cref="OpenClFourier.IfftAsync(Vector2[], int)"/>.
        /// </summary>
        Task<float[]?> IfftAsync(Vector2[] data, int originalLength = -1);

        /// <summary>
        /// Downloads the buffer described by the given native handle back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of the buffer to read.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>The downloaded host array, or <c>null</c> if the service is offline.</returns>
        T[]? PullData<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged;

        /// <summary>
        /// Downloads the buffer described by the given memory object back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object whose primary buffer should be read.</param>
        /// <returns>The downloaded host array, or <c>null</c> if the service is offline.</returns>
        T[]? PullData<T>(IRuntimeMem mem) where T : unmanaged;

        /// <summary>
        /// Allocates a float uninitialized device buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="elementCount">The number of elements to allocate.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline.</returns>
        IRuntimeMem? AllocateSingle<T>(int elementCount) where T : unmanaged;

        /// <summary>
        /// Uploads several host data chunks to the device as a group of buffers.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The collection of host data chunks to upload.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline.</returns>
        IRuntimeMem? PushChunks<T>(IEnumerable<T[]> data) where T : unmanaged;

        /// <summary>
        /// Downloads a grouped device allocation back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>The downloaded chunks, or <c>null</c> if the service is offline.</returns>
        T[][] PullChunks<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged;

        /// <summary>
        /// Downloads the grouped allocation described by the given memory object back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object describing the grouped allocation.</param>
        /// <returns>The downloaded chunks, or <c>null</c> if the service is offline.</returns>
        T[][]? PullChunks<T>(IRuntimeMem mem) where T : unmanaged;

        /// <summary>
        /// Allocates a group of uninitialized device buffers (one per supplied length).
        /// </summary>^^
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline.</returns>
        IRuntimeMem? AllocateGroup<T>(long[] lengths) where T : unmanaged;

        /// <summary>
        /// Asynchronously uploads a host array to the device as a float buffer.
        /// </summary>
        Task<IRuntimeMem?> PushDataAsync<T>(T[] data) where T : unmanaged;

        /// <summary>
        /// Asynchronously downloads a float device buffer back to the host.
        /// </summary>
        Task<T[]?> PullDataAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged;

        /// <summary>
        /// Asynchronously allocates a float uninitialized device buffer.
        /// </summary>
        Task<IRuntimeMem?> AllocateSingleAsync<T>(int elementCount) where T : unmanaged;

        /// <summary>
        /// Asynchronously uploads several host data chunks to the device as a group of buffers.
        /// </summary>
        Task<IRuntimeMem?> PushChunksAsync<T>(IEnumerable<T[]> data) where T : unmanaged;

        /// <summary>
        /// Asynchronously downloads a grouped device allocation back to the host as separate chunks.
        /// </summary>
        Task<T[][]> PullChunksAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged;

        /// <summary>
        /// Asynchronously allocates a group of uninitialized device buffers (one per supplied length).
        /// </summary>
        Task<IRuntimeMem?> AllocateGroupAsync<T>(long[] lengths) where T : unmanaged;

        /// <summary>
        /// Frees the buffer described by the given memory object.
        /// </summary>
        long FreeMemory(IRuntimeMem mem);

    }
}
