using AsynCUDA13.Runtime;
using Shouldly;

namespace AsynCUDA13.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class CudaLauncherIntegrationTests : TestBase
    {
        private CudaService? service;

        [TestInitialize]
        public void Initialize()
        {
            this.service = HardwareTestGuard.CreateCudaService(this.Logger);
        }

        [TestCleanup]
        public void Cleanup() => this.service?.Dispose();

        private void PrepareKernel(string source)
        {
            var kernelNames = System.Text.RegularExpressions.Regex.Matches(source, @"__global__\s+void\s+(\w+)")
                .Select(match => match.Groups[1].Value);
            foreach (var kernelName in kernelNames)
            {
                var path = Path.Combine(this.service!.Compiler!.KernelDirectory, kernelName + ".cu");
                File.WriteAllText(path, source);
                var service = Require(this.service);
                var compiler = Require(service.Compiler);
                compiler.CompileKernel(path).ShouldNotBeNull();
            }
        }

        [TestMethod]
        public async Task ValidKernelCallReturnsExpectedResult()
        {
            const string source = "extern \"C\" __global__ void AddConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += value; }";
            this.PrepareKernel(source);
            var input = new float[32];
            var service = Require(this.service);
            var memory = Require(service.PushData(input));
            var launcher = Require(service.Launcher);
            var response = await Task.Run(() => launcher.Execute("AddConstant", [memory.IndexPointer, 1f, input.Length]));
            Assert.IsNotNull(response);
            response.ElapsedMs.ShouldBeGreaterThanOrEqualTo(0);
            var result = Require(service.PullData<float>(memory.IndexPointer));
            result.ShouldBe(input.Select(x => x + 1f).ToArray());
        }

        [TestMethod]
        public async Task InvalidKernelCallsReturnNullWithoutCorruptingBuffer()
        {
            const string source = "extern \"C\" __global__ void AddConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += value; }";
            this.PrepareKernel(source);
            var input = new float[32];
            Assert.IsNotNull(this.service);
            var memory = this.service.PushData(input)!;
            var launcher = this.service.Launcher!;

            // Test that invalid kernel calls return null
            (await launcher.ExecuteAsync("AddConstant", [])).ShouldBeNull();
            (await launcher.ExecuteAsync("AddConstant", memory.IndexPointer, "wrong", input.Length)).ShouldBeNull();
            (await launcher.ExecuteAsync("AddConstant", IntPtr.Zero, 1f, input.Length)).ShouldBeNull();
            (await launcher.ExecuteAsync("MissingKernel", memory.IndexPointer, 1f, input.Length)).ShouldBeNull();
        }

        [TestMethod]
        public async Task GenericKernelSupportsOutOfPlaceResultPointer()
        {
            const string source = "extern \"C\" __global__ void AddVectors(float* input, float* output, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) output[i] = input[i] + 2.0f; }";
            this.PrepareKernel(source);
            var input = Enumerable.Range(0, 513).Select(x => (float) x).ToArray();
            var service = Require(this.service);
            var inputMemory = Require(service.PushData(input));
            var outputMemory = Require(service.AllocateSingle<float>(input.Length));

            var elapsedMs = await Require(service.Launcher).ExecuteAsync(
                "AddVectors",
                [inputMemory.IndexPointer, outputMemory.IndexPointer, input.Length]);

            Assert.IsNotNull(elapsedMs);
            var result = Require(service.PullData<float>(outputMemory.IndexPointer));
            result.ShouldBe(input.Select(x => x + 2f).ToArray());
        }

        [TestMethod]
        public async Task GenericKernelSwitchesLoadedKernelAndPreservesArgumentOrder()
        {
            const string source = "extern \"C\" __global__ void AddConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += value; } extern \"C\" __global__ void MultiplyConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] *= value; }";
            this.PrepareKernel(source);
            var input = Enumerable.Repeat(2f, 64).ToArray();
            var service = Require(this.service);
            var memory = Require(service.PushData(input));
            var launcher = Require(service.Launcher);

            (await launcher.ExecuteAsync("AddConstant", [memory.IndexPointer, 3f, input.Length])).ShouldNotBeNull();
            (await launcher.ExecuteAsync("MultiplyConstant", [memory.IndexPointer, 4f, input.Length])).ShouldNotBeNull();

            Require(service.PullData<float>(memory.IndexPointer)).ShouldBe(Enumerable.Repeat(20f, input.Length).ToArray());
        }

        [TestMethod]
        public async Task GenericKernelCanUnloadAfterExecution()
        {
            const string source = "extern \"C\" __global__ void AddConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += value; }";
            this.PrepareKernel(source);
            var input = new float[8];
            var service = Require(this.service);
            var memory = Require(service.PushData(input));

            var elapsedMs = await Require(service.Launcher).ExecuteAsync(
                "AddConstant",
                [memory.IndexPointer, 1f, input.Length]);

            Assert.IsNotNull(elapsedMs);
            Require(service.PullData<float>(memory.IndexPointer)).ShouldBe(Enumerable.Repeat(1f, input.Length).ToArray());
            Require(service.Launcher).KernelName.ShouldBeNull();
        }

    }
}
