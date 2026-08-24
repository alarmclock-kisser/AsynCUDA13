using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Provides GPU-accelerated Fast Fourier Transform (FFT) operations for registered device memory.
    /// Supports Real-to-Complex (R2C) forward transforms and Complex-to-Real (C2R) inverse transforms.
    /// </summary>
    public interface IRuntimeFourier
    {
        /// <summary>
        /// Performs a Real-to-Complex (R2C) Fast Fourier Transform.
        /// </summary>
        /// <param name="indexPointer">The pointer to the input memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>The pointer to the output complex memory, or <see cref="IntPtr.Zero"/> if failed.</returns>
        IntPtr PerformFft(IntPtr indexPointer, bool keep = false);

        /// <summary>
        /// Performs a Complex-to-Real (C2R) Inverse Fast Fourier Transform.
        /// </summary>
        /// <param name="indexPointer">The pointer to the input complex memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>The pointer to the output real memory, or <see cref="IntPtr.Zero"/> if failed.</returns>
        IntPtr PerformIfft(IntPtr indexPointer, bool keep = false);

        /// <summary>
        /// Asynchronously performs a Real-to-Complex (R2C) Fast Fourier Transform.
        /// </summary>
        /// <param name="pointer">The pointer to the input memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>A task representing the asynchronous operation, containing the output pointer.</returns>
        Task<IntPtr> PerformFftAsync(IntPtr pointer, bool keep = false);

        /// <summary>
        /// Asynchronously performs a Complex-to-Real (C2R) Inverse Fast Fourier Transform.
        /// </summary>
        /// <param name="pointer">The pointer to the input complex memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>A task representing the asynchronous operation, containing the output pointer.</returns>
        Task<IntPtr> PerformIfftAsync(IntPtr pointer, bool keep = false);

        /// <summary>
        /// Normalizes the results of an IFFT by scaling by the number of elements.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of the data.</typeparam>
        /// <param name="data">The collection of data to normalize.</param>
        /// <returns>A new array containing the normalized data.</returns>
        T[] NormalizeIfftResult<T>(IEnumerable<T> data) where T : unmanaged;

        /// <summary>
        /// Asynchronously normalizes the results of an IFFT.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of the data.</typeparam>
        /// <param name="data">The collection of data to normalize.</param>
        /// <returns>A task representing the asynchronous operation, containing the normalized array.</returns>
        Task<T[]> NormalizeIfftResultAsync<T>(IEnumerable<T> data) where T : unmanaged;
    }
}
