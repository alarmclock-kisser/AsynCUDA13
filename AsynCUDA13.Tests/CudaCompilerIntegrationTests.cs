using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using Shouldly;

namespace AsynCUDA13.Tests
{
    [TestClass]
    public sealed class CudaCompilerIntegrationTests : TestBase
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
        public void CompilestringCreatesPtxAndArgumentDefinitions()
        {
            const string source = "extern \"C\" __global__ void AddConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += value; }";
            var compiler = this.service!._compiler!;
            var ptxPath = compiler.Compilestring(source, true);
            ptxPath.ShouldNotBeNull();
            File.Exists(ptxPath).ShouldBeTrue();
            compiler.PrecompileKernelstring(source, true).ShouldBe("AddConstant");
            var sourcePath = Path.Combine(CudaCompiler.KernelPath, "CU", "AddConstant.cu");
            var arguments = compiler.GetArguments(sourcePath, true);
            arguments.Values.ShouldContain(typeof(IntPtr));
            arguments.Values.ShouldContain(typeof(float));
            arguments.Values.ShouldContain(typeof(int));
            arguments.Values.Count(x => x == typeof(IntPtr)).ShouldBe(1);
        }

        [TestMethod]
        public void CompileCuFileAndEnumerateKernelFiles()
        {
            var compiler = this.service!._compiler!;
            var sourcePath = Path.Combine(CudaCompiler.KernelPath, "CU", $"FileKernel_{Guid.NewGuid():N}.cu");
            File.WriteAllText(sourcePath, "extern \"C\" __global__ void FileKernel(float* data, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += 1.0f; }");
            try
            {
                var ptxPath = compiler.CompileKernel(sourcePath, true);
                ptxPath.ShouldNotBeNull();
                CudaCompiler.GetCuFiles(Path.GetDirectoryName(sourcePath)!).ShouldContain(sourcePath);
                CudaCompiler.GetPtxFiles(Path.Combine(CudaCompiler.KernelPath, "PTX")).ShouldContain(ptxPath);
            }
            finally { File.Delete(sourcePath); }
        }

        [TestMethod]
        public void InvalidKernelReturnsNullAndInvalidSignatureIsRejected()
        {
            var compiler = this.service!._compiler!;
            compiler.Compilestring("extern \"C\" __global__ void Broken(float* data { data[0] = 1.0f;", true).ShouldBeNull();
            compiler.PrecompileKernelstring("__global__ void noExtern(float* data, int length) {}", true).ShouldBeNull();
            compiler.GetArgumentType("float*").ShouldBe(typeof(IntPtr));
            compiler.GetArgumentType("int").ShouldBe(typeof(int));
        }
    }
}
