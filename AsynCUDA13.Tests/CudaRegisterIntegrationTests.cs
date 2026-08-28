using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using Shouldly;

namespace AsynCUDA13.Tests
{
    [TestClass]
    public sealed class CudaRegisterIntegrationTests : TestBase
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
        public void PushPullfloatAndBatchPreserveValues()
        {
            var values = Enumerable.Range(0, 256).Select(i => MathF.Sin(i * 0.1f)).ToArray();
            var chunks = new[] { values[..64], values[64..128], values[128..] };
            var floatData = this.service!.PushData(values);
            var batch = this.service.PushChunks(chunks);
            floatData.ShouldNotBeNull();
            batch.ShouldNotBeNull();
            this.service.PullData<float>(floatData.IndexPointer, true)!.ShouldBe(values);
            this.service.PullChunks<float>(batch.IndexPointer, true)!.SelectMany(x => x).ToArray().ShouldBe(chunks.SelectMany(x => x).ToArray());
        }

        [TestMethod]
        public async Task PushPullAsyncfloatAndBatchPreserveValues()
        {
            var chunks = new[] { new[] { 1f, 2f, 3f }, [4f, 5f] };
            var floatData = await this.service!.PushDataAsync(chunks.SelectMany(x => x));
            var batch = await this.service.PushChunksAsync(chunks);
            floatData.ShouldNotBeNull();
            batch.ShouldNotBeNull();
            (await this.service.PullDataAsync<float>(floatData.IndexPointer, true))!.ShouldBe(chunks.SelectMany(x => x).ToArray());
            (await this.service.PullChunksAsync<float>(batch.IndexPointer, true))!.SelectMany(x => x).ToArray().ShouldBe(chunks.SelectMany(x => x).ToArray());
        }

        [TestMethod]
        public async Task AllocateAndFreeMemoryUpdatesRegistrySizes()
        {
            var floatData = this.service!.AllocateSingle<float>(128);
            var group = this.service.AllocateGroup<float>([32, 64]);
            var asyncfloat = await this.service.AllocateSingleAsync<float>(16);
            var asyncGroup = await this.service.AllocateGroupAsync<float>([8, 8]);
            floatData.ShouldNotBeNull(); group.ShouldNotBeNull(); asyncfloat.ShouldNotBeNull(); asyncGroup.ShouldNotBeNull();
            this.service.TotalAllocations.ShouldBe(4);
            this.service.TotalAllocatedBytes.ShouldBe(this.service.RegisteredMemory.Sum(m => m.TotalLength));
            this.service.FreeMemory((CudaMem) floatData).ShouldBe(floatData.TotalSize);
            this.service.FreeMemory(group!.Id).ShouldBe(group.TotalSize);
            (await this.service.FreeMemoryAsync(asyncfloat!.Id)).ShouldBe(asyncfloat.TotalSize);
            (await this.service.FreeMemoryAsync(asyncGroup!.IndexPointer)).ShouldBe(asyncGroup.TotalSize);
            this.service.TotalAllocations.ShouldBe(0);
            this.service.TotalAllocatedBytes.ShouldBe(0);
        }
    }
}
