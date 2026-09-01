using AsynCUDA13.Runtime;
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
            this.service = HardwareTestGuard.CreateCudaService(this.Logger);
        }

        [TestCleanup]
        public void Cleanup() => this.service?.Dispose();

        [TestMethod]
        public void CompilestringCreatesPtxAndArgumentDefinitions()
        {
            const string source = "extern \"C\" __global__ void AddConstant(float* data, float value, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += value; }";
            var compiler = this.service!.Compiler!;
            var ptxPath = compiler.CompileKernel(source);
            ptxPath.ShouldNotBeNull();
            File.Exists(ptxPath).ShouldBeTrue();
            compiler.PrecompileKernel(source).ShouldBe("AddConstant");
            var sourcePath = this.service.Compiler.GetKernelSourceFile("AddConstant");
            var arguments = compiler.GetArguments(sourcePath);
            arguments.Values.ShouldContain(typeof(IntPtr));
            arguments.Values.ShouldContain(typeof(float));
            arguments.Values.ShouldContain(typeof(int));
            arguments.Values.Count(x => x == typeof(IntPtr)).ShouldBe(1);
        }

        [TestMethod]
        public void CompileCuFileAndEnumerateKernelFiles()
        {
            var compiler = this.service!.Compiler!;
            var sourcePath = compiler.GetKernelSourceFile("FileKernel") ?? Path.GetTempFileName();
            File.WriteAllText(sourcePath, "extern \"C\" __global__ void FileKernel(float* data, int length) { int i = blockIdx.x * blockDim.x + threadIdx.x; if (i < length) data[i] += 1.0f; }");
            try
            {
                var ptxPath = compiler.CompileKernel(sourcePath);
                ptxPath.ShouldNotBeNull();
                this.service.Compiler.GetSourceFiles().ShouldContain(sourcePath);
                this.service.Compiler.GetCompiledFiles().ShouldContain(ptxPath);
            }
            finally { File.Delete(sourcePath); }
        }

        [TestMethod]
        public void InvalidKernelReturnsNullAndInvalidSignatureIsRejected()
        {
            var compiler = this.service!.Compiler!;
            compiler.CompileKernel("extern \"C\" __global__ void Broken(float* data { data[0] = 1.0f;").ShouldBeNull();
            compiler.PrecompileKernel("__global__ void noExtern(float* data, int length) {}").ShouldBeNull();
            compiler.GetArguments("NonExistentKernel").ShouldBeEmpty();
            compiler.HasKernel("NonExistentKernel").ShouldBeFalse();
        }
    }
}
