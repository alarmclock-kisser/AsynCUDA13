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
            if (!CudaAvailabilityTester.IsCudaAvailable()) Assert.Inconclusive("CUDA runtime was not found in a CUDA PATH entry.");
            try
            {
                this.service = new CudaService();
                if (CudaService.DeviceCount <= 0 || !this.service.Initialize(0)) Assert.Inconclusive("No usable CUDA device 0 is available.");
            }
            catch (Exception ex) { Assert.Inconclusive($"CUDA initialization unavailable: {ex.Message}"); }
        }

        [TestCleanup]
        public void Cleanup() => this.service?.Dispose();

        [TestMethod]
        public void PushPullSingleAndBatchPreserveValues()
        {
            var values = Enumerable.Range(0, 256).Select(i => MathF.Sin(i * 0.1f)).ToArray();
            var chunks = new[] { values[..64], values[64..128], values[128..] };
            var single = this.service!.PushData(values);
            var batch = this.service.PushChunks(chunks);
            single.ShouldNotBeNull();
            batch.ShouldNotBeNull();
            this.service.PullData<float>(single!, true)!.ShouldBe(values);
            this.service.PullChunks<float>(batch!, true)!.SelectMany(x => x).ToArray().ShouldBe(chunks.SelectMany(x => x).ToArray());
        }

        [TestMethod]
        public async Task PushPullAsyncSingleAndBatchPreserveValues()
        {
            var chunks = new[] { new[] { 1f, 2f, 3f }, new[] { 4f, 5f } };
            var single = await this.service!.PushDataAsync(chunks.SelectMany(x => x));
            var batch = await this.service.PushChunksAsync(chunks);
            single.ShouldNotBeNull();
            batch.ShouldNotBeNull();
            (await this.service.PullDataAsync<float>(single!, true))!.ShouldBe(chunks.SelectMany(x => x).ToArray());
            (await this.service.PullChunksAsync<float>(batch!, true))!.SelectMany(x => x).ToArray().ShouldBe(chunks.SelectMany(x => x).ToArray());
        }

        [TestMethod]
        public async Task AllocateAndFreeMemoryUpdatesRegistrySizes()
        {
            var single = this.service!.AllocateSingle<float>(128);
            var group = this.service.AllocateGroup<float>(new nint[] { 32, 64 });
            var asyncSingle = await this.service.AllocateSingleAsync<float>(16);
            var asyncGroup = await this.service.AllocateGroupAsync<float>(new nint[] { 8, 8 });
            single.ShouldNotBeNull(); group.ShouldNotBeNull(); asyncSingle.ShouldNotBeNull(); asyncGroup.ShouldNotBeNull();
            this.service.RegisteredMemoryObjects.ShouldBe(4);
            this.service.TotalAllocated.ShouldBe(this.service.MemorySizesList.Sum());
            this.service.FreeMemory(single!).ShouldBe(single.TotalSize);
            this.service.FreeMemory(group!.Id).ShouldBe(group.TotalSize);
            (await this.service.FreeMemoryAsync(asyncSingle!.Id)).ShouldBe(asyncSingle.TotalSize);
            (await this.service.FreeMemoryAsync(asyncGroup!.IndexPointer)).ShouldBe(asyncGroup.TotalSize);
            this.service.RegisteredMemoryObjects.ShouldBe(0);
            this.service.TotalAllocated.ShouldBe(0);
        }
    }
}
