using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Provides FFT and IFFT entry points on top of the OpenCL runtime. Two transform modes are offered:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Chunked</b> (<see cref="FftChunked(float[], int, int)"/> / <see cref="IfftChunked(Vector2[], int, int)"/>):
    /// splits the input into fixed-size, power-of-two chunks and runs the per-chunk kernels
    /// (<c>chunked_fft</c> / <c>chunked_ifft</c>). The chunked IFFT normalizes by chunk size on the device.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Full</b> (<see cref="Fft(float[])"/> / <see cref="Ifft(Vector2[], int)"/>):
    /// treats the whole array as a float transform, padding to the next power of two. The pipeline runs
    /// <c>pad_real_to_complex</c> ? <c>fft_full</c> for the forward direction and
    /// <c>ifft_full</c> ? <c>normalize_complex</c> ? <c>extract_real</c> for the inverse direction.
    /// </description>
    /// </item>
    /// </list>
    /// Complex values use <see cref="Vector2"/> (X = real, Y = imaginary), which is layout-compatible with the
    /// <c>Vector2 { float x; float y; }</c> struct used inside the kernels.
    /// </summary>
    public sealed class OpenClFourier : IRuntimeFourier
    {
        /// <summary>The default chunk size used by the chunked transforms.</summary>
        public const int DefaultChunkSize = 65536;

        private readonly OpenClRegister _register;
        private readonly OpenClLauncher _launcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClFourier"/> class.
        /// </summary>
        /// <param name="register">The memory registry used for buffer allocation and transfers.</param>
        /// <param name="launcher">The launcher used to execute the Fourier kernels.</param>
        internal OpenClFourier(OpenClRegister register, OpenClLauncher launcher)
        {
            this._register = register;
            this._launcher = launcher;
        }

        /// <summary>
        /// Gets the IRuntimeFourier interface for this instance.
        /// </summary>
        public IRuntimeFourier Fourier => this;



        // ---------------------------------------------------------------------
        // Chunked transforms (fixed-size, power-of-two chunks)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Runs a chunked forward FFT over <paramref name="data"/> using the <c>chunked_fft</c> kernel.
        /// The input length must be a multiple of <paramref name="chunkSize"/>, and <paramref name="chunkSize"/>
        /// must be a power of two. NOT normalized (forward FFT is unnormalized).
        /// </summary>
        /// <param name="data">The real input samples.</param>
        /// <param name="chunkSize">The chunk size (power of two).</param>
        /// <param name="overlap">The overlap size passed to the kernel (currently informational).</param>
        /// <returns>The complex output, or <c>null</c> on failure.</returns>
        public Vector2[]? FftChunked(float[] data, int chunkSize = DefaultChunkSize, int overlap = 0)
        {
            if (!ValidateChunked(data, chunkSize))
            {
                return null;
            }

            int chunkCount = data.Length / chunkSize;

            Shared.Interfaces.IRuntimeMem? input = this._register.PushData(data);
            Shared.Interfaces.IRuntimeMem? output = this._register.AllocateSingle<Vector2>(data.Length);
            if (input == null || output == null)
            {
                this.SafeFree(input, output);
                return null;
            }

            bool ok = this._launcher.Execute("chunked_fft", chunkCount, input, output, chunkSize, overlap);
            Vector2[]? result = ok ? this._register.PullData<Vector2>((IRuntimeMem) output) : null;

            this.SafeFree(input, output);
            return result;
        }

        /// <summary>
        /// Runs a chunked inverse FFT over <paramref name="data"/> using the <c>chunked_ifft</c> kernel.
        /// The input length must be a multiple of <paramref name="chunkSize"/>, and <paramref name="chunkSize"/>
        /// must be a power of two. The kernel normalizes each chunk by its chunk size.
        /// </summary>
        /// <param name="data">The complex input.</param>
        /// <param name="chunkSize">The chunk size (power of two).</param>
        /// <param name="overlap">The overlap size passed to the kernel (currently informational).</param>
        /// <returns>The real output, or <c>null</c> on failure.</returns>
        public float[]? IfftChunked(Vector2[] data, int chunkSize = DefaultChunkSize, int overlap = 0)
        {
            if (!ValidateChunked(data, chunkSize))
            {
                return null;
            }

            int chunkCount = data.Length / chunkSize;

            Shared.Interfaces.IRuntimeMem? input = this._register.PushData(data);
            Shared.Interfaces.IRuntimeMem? output = this._register.AllocateSingle<float>(data.Length);
            if (input == null || output == null)
            {
                this.SafeFree(input, output);
                return null;
            }

            bool ok = this._launcher.Execute("chunked_ifft", chunkCount, input, output, chunkSize, overlap);
            float[]? result = ok ? this._register.PullData<float>((IRuntimeMem) output) : null;

            this.SafeFree(input, output);
            return result;
        }

        /// <summary>
        /// Asynchronously runs a chunked forward FFT. See <see cref="FftChunked(float[], int, int)"/>.
        /// </summary>
        public Task<Vector2[]?> FftChunkedAsync(float[] data, int chunkSize = DefaultChunkSize, int overlap = 0)
        {
            return Task.Run(() => this.FftChunked(data, chunkSize, overlap));
        }

        /// <summary>
        /// Asynchronously runs a chunked inverse FFT. See <see cref="IfftChunked(Vector2[], int, int)"/>.
        /// </summary>
        public Task<float[]?> IfftChunkedAsync(Vector2[] data, int chunkSize = DefaultChunkSize, int overlap = 0)
        {
            return Task.Run(() => this.IfftChunked(data, chunkSize, overlap));
        }



        // ---------------------------------------------------------------------
        // Full transforms (arbitrary length, padded to a power of two)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Runs a forward FFT over the whole <paramref name="data"/> array regardless of its length.
        /// The input is zero-padded to the next power of two on the device via <c>pad_real_to_complex</c>
        /// and transformed with <c>fft_full</c>. NOT normalized.
        /// </summary>
        /// <param name="data">The real input samples (any length).</param>
        /// <returns>The complex spectrum of length <c>NextPowerOfTwo(data.Length)</c>, or <c>null</c> on failure.</returns>
        public Vector2[]? Fft(float[] data)
        {
            if (data == null || data.Length == 0)
            {
                StaticLogger.LogError("Fft: data is null or empty.");
                return null;
            }

            long n = NextPowerOfTwo(data.Length);

            Shared.Interfaces.IRuntimeMem? input = this._register.PushData(data);
            Shared.Interfaces.IRuntimeMem? complex = this._register.AllocateSingle<Vector2>(n);
            if (input == null || complex == null)
            {
                this.SafeFree(input, complex);
                return null;
            }

            bool ok = this._launcher.Execute("pad_real_to_complex", n, input, complex, data.Length, (int)n)
                      && this._launcher.Execute("fft_full", 1, complex, n);

            Vector2[]? result = ok ? this._register.PullData<Vector2>((IRuntimeMem) complex) : null;

            this.SafeFree(input, complex);
            return result;
        }

        /// <summary>
        /// Runs an inverse FFT over the whole <paramref name="data"/> array regardless of its length.
        /// The input is zero-padded to the next power of two (on the host), transformed with <c>ifft_full</c>,
        /// normalized by <c>1/n</c> via <c>normalize_complex</c> and reduced to its real part via <c>extract_real</c>.
        /// </summary>
        /// <param name="data">The complex input (any length).</param>
        /// <param name="originalLength">
        /// The number of real samples to return. When negative, the full padded length is returned.
        /// </param>
        /// <returns>The reconstructed real samples, or <c>null</c> on failure.</returns>
        public float[]? Ifft(Vector2[] data, int originalLength = -1)
        {
            if (data == null || data.Length == 0)
            {
                StaticLogger.LogError("Ifft: data is null or empty.");
                return null;
            }

            int n = (int)NextPowerOfTwo(data.Length);

            Vector2[] padded;
            if (n == data.Length)
            {
                padded = data;
            }
            else
            {
                padded = new Vector2[n];
                Array.Copy(data, padded, data.Length);
            }

            Shared.Interfaces.IRuntimeMem? complex = this._register.PushData(padded);
            Shared.Interfaces.IRuntimeMem? real = this._register.AllocateSingle<float>(n);
            if (complex == null || real == null)
            {
                this.SafeFree(complex, real);
                return null;
            }

            float scale = 1.0f / n;
            bool ok = this._launcher.Execute("ifft_full", 1, complex, n)
                      && this._launcher.Execute("normalize_complex", n, complex, scale, n)
                      && this._launcher.Execute("extract_real", n, complex, real, n);

            float[]? full = ok ? this._register.PullData<float>((IRuntimeMem) real) : null;

            this.SafeFree(complex, real);

            if (full == null)
            {
                return null;
            }

            if (originalLength < 0 || originalLength >= full.Length)
            {
                return full;
            }

            float[] trimmed = new float[originalLength];
            Array.Copy(full, trimmed, originalLength);
            return trimmed;
        }

        /// <summary>
        /// Asynchronously runs a full forward FFT. See <see cref="Fft(float[])"/>.
        /// </summary>
        public Task<Vector2[]?> FftAsync(float[] data)
        {
            return Task.Run(() => this.Fft(data));
        }

        /// <summary>
        /// Asynchronously runs a full inverse FFT. See <see cref="Ifft(Vector2[], int)"/>.
        /// </summary>
        public Task<float[]?> IfftAsync(Vector2[] data, int originalLength = -1)
        {
            return Task.Run(() => this.Ifft(data, originalLength));
        }



        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Normalizes an unnormalized IFFT result on the host by dividing every sample by <paramref name="n"/>.
        /// Provided for parity with host-side normalization workflows.
        /// </summary>
        /// <param name="data">The samples to normalize in place returned as a new array.</param>
        /// <param name="n">The divisor (typically the transform length).</param>
        /// <returns>A new array containing the normalized samples, or <c>null</c> when input is invalid.</returns>
        public static T[]? NormalizeIfftResult<T>(T[] data, int n) where T : struct, IConvertible
        {
            if (data == null || data.Length == 0 || n <= 0)
            {
                return null;
            }

            float[] result = new float[data.Length];
            float scale = 1.0f / n;
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = Convert.ToSingle(data[i]) * scale;
            }

            return result.Cast<T>().ToArray();
        }

        /// <summary>
        /// Returns the smallest power of two greater than or equal to <paramref name="value"/> (minimum 1).
        /// </summary>
        public static long NextPowerOfTwo(long value)
        {
            if (value <= 1)
            {
                return 1;
            }

            long power = 1;
            while (power < value)
            {
                power <<= 1;
            }

            return power;
        }

        /// <summary>
        /// Validates a chunked transform request: non-empty input, power-of-two chunk size, length divisible by it.
        /// </summary>
        private static bool ValidateChunked<T>(T[] data, int chunkSize)
        {
            if (data == null || data.Length == 0)
            {
                StaticLogger.LogError("Chunked transform: data is null or empty.");
                return false;
            }

            if (chunkSize <= 0 || (chunkSize & (chunkSize - 1)) != 0)
            {
                StaticLogger.LogError($"Chunked transform: chunkSize {chunkSize} must be a positive power of two.");
                return false;
            }

            if (data.Length % chunkSize != 0)
            {
                StaticLogger.LogError($"Chunked transform: data length {data.Length} must be a multiple of chunkSize {chunkSize}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Frees any of the supplied allocations that are non-null.
        /// </summary>
        private void SafeFree(params Shared.Interfaces.IRuntimeMem?[] allocations)
        {
            foreach (Shared.Interfaces.IRuntimeMem? mem in allocations)
            {
                if (mem != null)
                {
                    this._register.Free((IRuntimeMem) mem);
                }
            }
        }



        // Interface APIs - IRuntimeFourier
        // Note: OpenClFourier implements host-array based FFT, not device-memory based FFT.
        // These interface methods throw NotImplementedException as they are not applicable.

        public IntPtr PerformFft(IntPtr indexPointer, bool keep)
        {
            IRuntimeMem? mem = this._register[indexPointer] as IRuntimeMem;
            return this.DoFft(mem, keep)?.IndexPointer ?? IntPtr.Zero;
        }

        public IntPtr PerformIfft(IntPtr indexPointer, bool keep)
        {
            IRuntimeMem? mem = this._register[indexPointer] as IRuntimeMem;
            return this.DoIfft(mem, keep)?.IndexPointer ?? IntPtr.Zero;
        }

        public async Task<IntPtr> PerformFftAsync(IntPtr pointer, bool keep)
        {
            IRuntimeMem? mem = this._register[pointer] as IRuntimeMem;
            return await Task.Run(() => this.DoFft(mem, keep)?.IndexPointer ?? IntPtr.Zero);
        }

        public async Task<IntPtr> PerformIfftAsync(IntPtr pointer, bool keep)
        {
            IRuntimeMem? mem = this._register[pointer] as IRuntimeMem;
            return await Task.Run(() => this.DoIfft(mem, keep)?.IndexPointer ?? IntPtr.Zero);
        }

        public T[] NormalizeIfftResult<T>(IEnumerable<T> data) where T : unmanaged
        {
            return this.NormalizeIfftResult(data);
        }

        public async Task<T[]> NormalizeIfftResultAsync<T>(IEnumerable<T> data) where T : unmanaged
        {
            return await Task.Run(() => this.NormalizeIfftResult(data));
        }



        // Interface API helpers
        private IRuntimeMem? DoFft(IRuntimeMem? input, bool keep = false)
        {
            if (input == null)
            {
                return null;
            }

            int n = (int) NextPowerOfTwo(input.TotalLength);
            Shared.Interfaces.IRuntimeMem? complex = this._register.AllocateSingle<Vector2>(n);
            if (complex == null)
            {
                this.SafeFree(input, complex);
                return null;
            }

            bool ok = this._launcher.Execute("pad_real_to_complex", n, input, complex, (int)input.TotalLength, n)
                      && this._launcher.Execute("fft_full", 1, complex, n);

            if (!ok)
            {
                this.SafeFree(input, complex);
                return null;
            }

            return (IRuntimeMem) complex;
        }

        private IRuntimeMem? DoIfft(IRuntimeMem? input, bool keep = false)
        {
            if (input == null)
            {
                return null;
            }
            int n = (int) NextPowerOfTwo(input.TotalLength);
            Shared.Interfaces.IRuntimeMem? real = this._register.AllocateSingle<float>(n);
            if (real == null)
            {
                this.SafeFree(input, real);
                return null;
            }
            float scale = 1.0f / n;
            bool ok = this._launcher.Execute("ifft_full", 1, input, n)
                      && this._launcher.Execute("normalize_complex", n, input, scale, n)
                      && this._launcher.Execute("extract_real", n, input, real, n);
            if (!ok)
            {
                this.SafeFree(input, real);
                return null;
            }
            return (IRuntimeMem) real;
        }
    }
}
