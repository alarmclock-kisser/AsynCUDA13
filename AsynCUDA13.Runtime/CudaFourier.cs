using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.CudaFFT;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Provides GPU-accelerated Fast Fourier Transform (FFT) operations for registered device memory.
    /// Supports Real-to-Complex (R2C) forward transforms and Complex-to-Real (C2R) inverse transforms,
    /// in synchronous, asynchronous (stream-based) and batched ("many") variants, plus helpers to
    /// normalize inverse-transform results. Allocations are obtained from and released through the
    /// associated <see cref="CudaRegister"/>.
    /// </summary>
    internal class CudaFourier : IRuntimeFourier, IDisposable
    {
        // Fields
        /// <summary>The CUDA primary context used for transform execution and synchronization.</summary>
        private readonly PrimaryContext CTX;

        /// <summary>The registry used to allocate and free the transform input/output buffers.</summary>
        private readonly CudaRegister Register;


        /// <summary>
        /// Initializes a new instance of the <see cref="CudaFourier"/> class.
        /// </summary>
        /// <param name="ctx">The CUDA primary context.</param>
        /// <param name="register">The <see cref="CudaRegister"/> to use for memory management.</param>
        internal CudaFourier(PrimaryContext ctx, CudaRegister register)
        {
            this.CTX = ctx;
            this.Register = register;
        }

        /// <summary>
        /// Gets the IRuntimeFourier interface for this instance.
        /// </summary>
        public IRuntimeFourier Fourier => this;


        /// <summary>
        /// Performs a Real-to-Complex (R2C) Fast Fourier Transform.
        /// </summary>
        /// <param name="indexPointer">The pointer to the input memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>The pointer to the output complex memory, or <see cref="IntPtr.Zero"/> if failed.</returns>
        public IntPtr PerformFft(IntPtr indexPointer, bool keep = false)
        {
            // Check initialized & input pointer
            if (this.Register == null || this.CTX == null || indexPointer == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Get memory from register
            var mem = this.Register[indexPointer];
            if (mem == null || mem.IndexPointer == IntPtr.Zero || mem.IndexLength == IntPtr.Zero)
            {
                StaticLogger.Log("Memory not found or invalid pointer.");
                return IntPtr.Zero;
            }

            // Check input memory type
            if (mem.ElementType != typeof(float))
            {
                StaticLogger.Log("Input memory type is not float.");
                return indexPointer;
            }

            // Allocate output memory
            var outputMem = this.Register.AllocateGroup<float2>(mem.PointerLengths.Select(l => (nint) l).ToArray());
            if (outputMem == null || outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Could not allocate output memory.");
                return indexPointer;
            }

            // Use Context instead of stream(s)	
            // this.CTX.Synchronize();

            // Check if all lengths are the same (else create plan for each length in loop)
            try
            {
                if (mem.PointerLengths.Distinct().Count() == 1)
                {
                    int nx = (int) mem.IndexLength;
                    cufftType fftType = cufftType.R2C;
                    int batch = 1;

                    CudaFFTPlan1D plan = new(nx, fftType, batch);
                    for (int i = 0; i < mem.Count; i++)
                    {
                        CUdeviceptr inPtr = new(mem.PointerIds[i]);
                        CUdeviceptr outPtr = new(outputMem.PointerIds[i]);
                        plan.Exec(inPtr, outPtr);
                        outputMem.PointerIds[i] = outPtr.Pointer;
                    }
                    plan.Dispose();
                }
                else
                {
                    long[] nx = mem.PointerLengths.Select(l => (long) l).ToArray();
                    cufftType fftType = cufftType.R2C;
                    int batch = 1;

                    for (int i = 0; i < mem.Count; i++)
                    {
                        CudaFFTPlan1D plan = new((int) nx[i], fftType, batch);
                        CUdeviceptr inPtr = new(mem.PointerIds[i]);
                        CUdeviceptr outPtr = new(outputMem.PointerIds[i]);
                        plan.Exec(inPtr, outPtr);
                        outputMem.PointerIds[i] = outPtr.Pointer;
                        plan.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error during FFT execution.");
                this.Register.FreeMemory(outputMem);
                return indexPointer;
            }

            // Check output memory
            if (outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("FFT execution failed, result pointer is null.");
                this.Register.FreeMemory(outputMem);
                return indexPointer;
            }

            // Synchronize context
            // this.CTX.Synchronize();

            // Optionally free input memory
            if (!keep)
            {
                this.Register.FreeMemory(indexPointer);
            }

            return outputMem.IndexPointer;
        }

        /// <summary>
        /// Performs a Complex-to-Real (C2R) Inverse Fast Fourier Transform.
        /// </summary>
        /// <param name="indexPointer">The pointer to the input complex memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>The pointer to the output real memory, or <see cref="IntPtr.Zero"/> if failed.</returns>
        public IntPtr PerformIfft(IntPtr indexPointer, bool keep = false)
        {
            // Check initialized & input pointer
            if (this.Register == null || this.CTX == null || indexPointer == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Get memory from register
            var mem = this.Register[indexPointer];
            if (mem == null || mem.IndexPointer == IntPtr.Zero || mem.IndexLength == IntPtr.Zero)
            {
                StaticLogger.Log("Memory not found or invalid pointer.");
                return IntPtr.Zero;
            }

            // Check input memory type
            if (mem.ElementType != typeof(float2))
            {
                StaticLogger.Log("Input memory type is not float2.");
                return indexPointer;
            }

            // Allocate output memory
            var outputMem = this.Register.AllocateGroup<float>(mem.PointerLengths.Select(l => (nint) l).ToArray());
            if (outputMem == null || outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Could not allocate output memory.");
                return indexPointer;
            }

            // Use Context instead of stream(s)	
            // this.CTX.Synchronize();

            // Check if all lengths are the same (else create plan for each length in loop)
            try
            {
                if (mem.PointerLengths.Distinct().Count() == 1)
                {
                    int nx = (int) mem.IndexLength;
                    cufftType fftType = cufftType.C2R;
                    int batch = 1;

                    CudaFFTPlan1D plan = new(nx, fftType, batch);
                    for (int i = 0; i < mem.Count; i++)
                    {
                        CUdeviceptr inPtr = new(mem.PointerIds[i]);
                        CUdeviceptr outPtr = new(outputMem.PointerIds[i]);
                        plan.Exec(inPtr, outPtr);
                        outputMem.PointerIds[i] = outPtr.Pointer;
                    }
                    plan.Dispose();
                }
                else
                {
                    long[] nx = mem.PointerLengths.Select(l => (long) l).ToArray();
                    cufftType fftType = cufftType.C2R;
                    int batch = 1;

                    for (int i = 0; i < mem.Count; i++)
                    {
                        CudaFFTPlan1D plan = new((int) nx[i], fftType, batch);
                        CUdeviceptr inPtr = new(mem.PointerIds[i]);
                        CUdeviceptr outPtr = new(outputMem.PointerIds[i]);
                        plan.Exec(inPtr, outPtr);
                        outputMem.PointerIds[i] = outPtr.Pointer;
                        plan.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error during IFFT execution.");
                this.Register.FreeMemory(outputMem);
                return indexPointer;
            }

            // Check output memory
            if (outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("FFT execution failed, result pointer is null.");
                this.Register.FreeMemory(outputMem);
                return indexPointer;
            }

            // Synchronize context
            // this.CTX.Synchronize();

            // Optionally free input memory
            if (!keep)
            {
                this.Register.FreeMemory(indexPointer);
            }

            return outputMem.IndexPointer;
        }

        /// <summary>
        /// Normalizes the results of an IFFT by scaling by the number of elements.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of the data.</typeparam>
        /// <param name="data">The collection of data to normalize.</param>
        /// <returns>A new array containing the normalized data.</returns>
        public T[] NormalizeIfftResult<T>(IEnumerable<T> data) where T : unmanaged
        {
            if (data == null)
            {
                return [];
            }

            T[] result = data.ToArray();
            long count = result.LongLength;
            if (count == 0)
            {
                return result;
            }

            double scale = 1.0 / count;
            if (typeof(T) == typeof(float))
            {
                float fscale = (float) scale;
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = (T) (object) ((float) (object) result[i] * fscale);
                }
            }
            else if (typeof(T) == typeof(double))
            {
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = (T) (object) ((double) (object) result[i] * scale);
                }
            }

            return result;
        }



        /// <summary>
        /// Asynchronously performs a Real-to-Complex (R2C) Fast Fourier Transform.
        /// </summary>
        /// <param name="pointer">The pointer to the input memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>A task representing the asynchronous operation, containing the output pointer.</returns>
        public async Task<IntPtr> PerformFftAsync(IntPtr pointer, bool keep = false)
        {
            if (this.Register == null || this.CTX == null || pointer == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var mem = this.Register[pointer];
            if (mem == null || mem.IndexPointer == IntPtr.Zero || mem.IndexLength == IntPtr.Zero)
            {
                StaticLogger.Log("Memory not found or invalid pointer.");
                return IntPtr.Zero;
            }

            var outputMem = await this.Register.AllocateGroupAsync<float2>(mem.PointerLengths.Select(l => (nint) l).ToArray());
            if (outputMem == null || outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Could not allocate output memory.");
                return pointer;
            }

            try
            {
                var stream = this.Register.GetStream();
                if (stream == null)
                {
                    this.Register.FreeMemory(outputMem);
                    return pointer;
                }

                int nx = (int) mem.IndexLength;
                cufftType fftType = cufftType.R2C;
                int batch = 1;
                CudaFFTPlan1D plan = new(nx, fftType, batch, stream.Stream);

                for (int i = 0; i < mem.Count; i++)
                {
                    CUdeviceptr inPtr = new(mem.PointerIds[i]);
                    CUdeviceptr outPtr = new(outputMem.PointerIds[i]);
                    plan.Exec(inPtr, outPtr);
                    outputMem.PointerIds[i] = outPtr.Pointer;
                }

                await Task.Run(stream.Synchronize);
                plan.Dispose();

            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error during FFT execution.");
                this.Register.FreeMemory(outputMem);
                return pointer;
            }

            if (outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("FFT execution failed, result pointer is null.");
                this.Register.FreeMemory(outputMem);
                return pointer;
            }

            if (!keep)
            {
                this.Register.FreeMemory(pointer);
            }

            return outputMem.IndexPointer;
        }

        /// <summary>
        /// Asynchronously performs a Complex-to-Real (C2R) Inverse Fast Fourier Transform.
        /// </summary>
        /// <param name="pointer">The pointer to the input complex memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>A task representing the asynchronous operation, containing the output pointer.</returns>
        public async Task<IntPtr> PerformIfftAsync(IntPtr pointer, bool keep = false)
        {
            if (this.Register == null || this.CTX == null || pointer == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var mem = this.Register[pointer];
            if (mem == null || mem.IndexPointer == IntPtr.Zero || mem.IndexLength == IntPtr.Zero)
            {
                StaticLogger.Log("Memory not found or invalid pointer.");
                return IntPtr.Zero;
            }

            var outputMem = await this.Register.AllocateGroupAsync<float>(mem.PointerLengths.Select(l => (nint) l).ToArray());
            if (outputMem == null || outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Could not allocate output memory.");
                return pointer;
            }

            try
            {
                var stream = this.Register.GetStream();
                if (stream == null)
                {
                    this.Register.FreeMemory(outputMem);
                    return pointer;
                }

                int nx = (int) mem.IndexLength;
                cufftType fftType = cufftType.C2R;
                int batch = 1;
                CudaFFTPlan1D plan = new(nx, fftType, batch, stream.Stream);

                for (int i = 0; i < mem.Count; i++)
                {
                    CUdeviceptr inPtr = new(mem.PointerIds[i]);
                    CUdeviceptr outPtr = new(outputMem.PointerIds[i]);
                    plan.Exec(inPtr, outPtr);
                    outputMem.PointerIds[i] = outPtr.Pointer;
                }

                await Task.Run(stream.Synchronize);
                plan.Dispose();
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error during IFFT execution.");
                this.Register.FreeMemory(outputMem);
                return pointer;
            }

            if (outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("IFFT execution failed, result pointer is null.");
                this.Register.FreeMemory(outputMem);
                return pointer;
            }

            if (!keep)
            {
                this.Register.FreeMemory(pointer);
            }

            return outputMem.IndexPointer;
        }

        /// <summary>
        /// Asynchronously normalizes the results of an IFFT.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of the data.</typeparam>
        /// <param name="data">The collection of data to normalize.</param>
        /// <returns>A task representing the asynchronous operation, containing the normalized array.</returns>
        public async Task<T[]> NormalizeIfftResultAsync<T>(IEnumerable<T> data) where T : unmanaged
        {
            return await Task.Run(() => this.NormalizeIfftResult(data));
        }




        /// <summary>
        /// Asynchronously performs a batch of Real-to-Complex (R2C) Fast Fourier Transforms.
        /// </summary>
        /// <param name="indexPointer">The pointer to the input memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>A task representing the asynchronous operation, containing the output pointer.</returns>
        public async Task<IntPtr> PerformFftManyAsync(IntPtr indexPointer, bool keep = false)
        {
            if (this.Register == null || this.CTX == null || indexPointer == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var mem = this.Register[indexPointer];
            if (mem == null || mem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Memory not found or invalid pointer.");
                return IntPtr.Zero;
            }

            IntPtr[] lengths = mem.PointerLengths.Select(l => (IntPtr) l).ToArray();

            var outputMem = await this.Register.AllocateGroupAsync<float2>(lengths);
            if (outputMem == null || outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Could not allocate output memory.");
                return indexPointer;
            }

            try
            {
                var stream = this.Register.GetStream();
                if (stream == null)
                {
                    this.Register.FreeMemory(outputMem);
                    return indexPointer;
                }

                int rank = 1;
                cufftType fftType = cufftType.R2C;
                CudaFFTPlanMany plan = new(rank, lengths.Select(l => (int) l.ToInt64()).ToArray(), mem.Count, fftType, stream.Stream);

                for (int i = 0; i < mem.Count; i++)
                {
                    CUdeviceptr inPtr = new(mem.PointerIds[i]);
                    CUdeviceptr outPtr = new(outputMem.PointerIds[i]);

                    plan.Exec(inPtr, outPtr);

                    outputMem.PointerIds[i] = outPtr.Pointer;
                }

                if (outputMem.IndexPointer == IntPtr.Zero)
                {
                    StaticLogger.Log("FFT-many execution failed, result pointer is null.");
                    this.Register.FreeMemory(outputMem);
                    return indexPointer;
                }

                await Task.Run(stream.Synchronize);

                if (!keep)
                {
                    this.Register.FreeMemory(indexPointer);
                }

                plan.Dispose();
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error during FFT-many execution.");
                return indexPointer;
            }

            return outputMem.IndexPointer;
        }

        /// <summary>
        /// Asynchronously performs a batch of Complex-to-Real (C2R) Inverse Fast Fourier Transforms.
        /// </summary>
        /// <param name="indexPointer">The pointer to the input complex memory in the register.</param>
        /// <param name="keep">If true, the input memory is not freed.</param>
        /// <returns>A task representing the asynchronous operation, containing the output pointer.</returns>
        public async Task<IntPtr> PerformIfftManyAsync(IntPtr indexPointer, bool keep = false)
        {
            if (this.Register == null || this.CTX == null || indexPointer == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var mem = this.Register[indexPointer];
            if (mem == null || mem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Memory not found or invalid pointer.");
                return IntPtr.Zero;
            }

            IntPtr[] lengths = mem.PointerLengths.Select(l => (IntPtr) l).ToArray();

            var outputMem = await this.Register.AllocateGroupAsync<float>(lengths);
            if (outputMem == null || outputMem.IndexPointer == IntPtr.Zero)
            {
                StaticLogger.Log("Could not allocate output memory.");
                return indexPointer;
            }

            try
            {
                var stream = this.Register.GetStream();
                if (stream == null)
                {
                    this.Register.FreeMemory(outputMem);
                    return indexPointer;
                }

                int rank = 1;
                cufftType fftType = cufftType.C2R;
                CudaFFTPlanMany plan = new(rank, lengths.Select(l => (int) l.ToInt64()).ToArray(), mem.Count, fftType, stream.Stream);

                for (int i = 0; i < mem.Count; i++)
                {
                    CUdeviceptr inPtr = new(mem.PointerIds[i]);
                    CUdeviceptr outPtr = new(outputMem.PointerIds[i]);

                    plan.Exec(inPtr, outPtr);

                    outputMem.PointerIds[i] = outPtr.Pointer;
                }

                if (outputMem.IndexPointer == IntPtr.Zero)
                {
                    StaticLogger.Log("IFFT-many execution failed, result pointer is null.");
                    this.Register.FreeMemory(outputMem);
                    return indexPointer;
                }

                await Task.Run(stream.Synchronize);

                if (!keep)
                {
                    this.Register.FreeMemory(indexPointer);
                }

                plan.Dispose();
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error during IFFT-many execution.");
                return indexPointer;
            }

            return outputMem.IndexPointer;
        }

        /// <summary>
        /// Normalizes a collection of IFFT result arrays.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of the data.</typeparam>
        /// <param name="data">The collection of arrays to normalize.</param>
        /// <returns>An array of normalized arrays.</returns>
        internal T[][] NormalizeIfftManyResult<T>(IEnumerable<T[]> data) where T : unmanaged
        {
            if (data == null)
            {
                return [];
            }

            T[][] arrays = data.ToArray();
            for (int i = 0; i < arrays.Length; i++)
            {
                T[] src = arrays[i];
                if (src == null || src.Length == 0)
                {
                    arrays[i] = src ?? [];
                    continue;
                }

                double scale = 1.0 / src.Length;
                T[] dst = new T[src.Length];
                if (typeof(T) == typeof(float))
                {
                    float fscale = (float) scale;
                    for (int j = 0; j < src.Length; j++)
                    {
                        dst[j] = (T) (object) ((float) (object) src[j] * fscale);
                    }
                }
                else if (typeof(T) == typeof(double))
                {
                    for (int j = 0; j < src.Length; j++)
                    {
                        dst[j] = (T) (object) ((double) (object) src[j] * scale);
                    }
                }
                else
                {
                    Array.Copy(src, dst, src.Length);
                }

                arrays[i] = dst;
            }

            return arrays;
        }

        /// <summary>
        /// Asynchronously normalizes a collection of IFFT result arrays.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of the data.</typeparam>
        /// <param name="data">The collection of arrays to normalize.</param>
        /// <returns>A task representing the asynchronous operation, containing the normalized arrays.</returns>
        public async Task<T[][]> NormalizeIfftManyResultAsync<T>(IEnumerable<T[]> data) where T : unmanaged
        {
            return await Task.Run(() => this.NormalizeIfftManyResult(data));
        }





        /// <summary>
        /// Releases the resources used by the <see cref="CudaFourier"/>.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

    }
}
