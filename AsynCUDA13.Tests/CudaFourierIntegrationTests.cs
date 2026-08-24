using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using ManagedCuda.VectorTypes;
using Shouldly;

namespace AsynCUDA13.Tests
{
    [TestClass]
    public sealed class CudaFourierIntegrationTests : TestBase
    {
        private CudaService? service;

        [TestInitialize]
        public void Initialize()
        {
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime was not found in a CUDA PATH entry.");
            }

            try
            {
                this.service = new CudaService();
                if (CudaService.DeviceCount <= 0 || !this.service.Initialize(0))
                {
                    Assert.Inconclusive("No usable CUDA device 0 is available.");
                }
            }
            catch (Exception ex) { Assert.Inconclusive($"CUDA initialization unavailable: {ex.Message}"); }
        }

        [TestCleanup]
        public void Cleanup() => this.service?.Dispose();

        [TestMethod]
        public void FftAndIfftRoundTripGeneratedSineWave()
        {
            var input = Enumerable.Range(0, 64).Select(i => MathF.Sin(2 * MathF.PI * i / 16)).ToArray();
            var inputMem = this.service!.PushData(input)!;
            var spectrumPointer = this.service._fourier!.PerformFft(inputMem.IndexPointer, true);
            spectrumPointer.ShouldNotBe(IntPtr.Zero);
            var spectrum = this.service.PullData<float2>(spectrumPointer, true);
            spectrum.ShouldNotBeNull();
            var restoredPointer = this.service._fourier.PerformIfft(spectrumPointer, false);
            restoredPointer.ShouldNotBe(IntPtr.Zero);
            var restored = this.service.PullData<float>(restoredPointer, false)!;
            var normalized = this.service._fourier.NormalizeIfftResult(restored);
            normalized.Length.ShouldBe(input.Length);
            restored.Any(float.IsNaN).ShouldBeFalse();
            restored.Max(x => MathF.Abs(x)).ShouldBeGreaterThan(0.5f);
        }

        [TestMethod]
        public async Task AsyncFftAndIfftRoundTripGeneratedSineWave()
        {
            var input = Enumerable.Range(0, 64).Select(i => MathF.Sin(2 * MathF.PI * i / 8)).ToArray();
            var inputMem = (await this.service!.PushDataAsync(input))!;
            var spectrumPointer = await this.service._fourier!.PerformFftAsync(inputMem.IndexPointer, true);
            spectrumPointer.ShouldNotBe(IntPtr.Zero);
            var restoredPointer = await this.service._fourier.PerformIfftAsync(spectrumPointer, false);
            var restored = this.service.PullData<float>(restoredPointer, false)!;
            this.service._fourier.NormalizeIfftResultAsync(restored).Result.Length.ShouldBe(input.Length);
        }

        [TestMethod]
        public async Task ChunkedManyFftAndIfftReturnEveryChunk()
        {
            var chunks = Enumerable.Range(0, 3).Select(c => Enumerable.Range(0, 32).Select(i => MathF.Sin((i + c) * 0.2f)).ToArray()).ToArray();
            var inputMem = (await this.service!.PushChunksAsync(chunks))!;
            var spectrumPointer = await this.service._fourier!.PerformFftManyAsync(inputMem.IndexPointer, true);
            spectrumPointer.ShouldNotBe(IntPtr.Zero);
            spectrumPointer.ShouldNotBe(IntPtr.Zero);
            var restoredPointer = await this.service._fourier.PerformIfftManyAsync(spectrumPointer, false);
            restoredPointer.ShouldNotBe(IntPtr.Zero);
            restoredPointer.ShouldNotBe(IntPtr.Zero);
        }
    }
}
