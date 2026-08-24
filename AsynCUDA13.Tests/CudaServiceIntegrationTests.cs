using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using Shouldly;

namespace AsynCUDA13.Tests
{
    [TestClass]
    public sealed class CudaServiceIntegrationTests : TestBase
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
        public void DevicesExposePropertiesAndEntries()
        {
            var devices = CudaService.GetAvailableDevicesProperties();
            devices.Count.ShouldBeGreaterThan(0);
            devices[0].DeviceName.ShouldNotBeNullOrWhiteSpace();
            this.service!.DeviceEntries.Count.ShouldBe(devices.Count);
            this.service[0].ShouldNotBeNull();
            this.service.SelectedDeviceId.ShouldBe(0);
            this.service.Online.ShouldBeTrue();
        }

        [TestMethod]
        public void InitializeDeviceZeroMakesServiceOnline()
        {
            this.service!.Initialize(0).ShouldBeTrue();
            this.service.Online.ShouldBeTrue();
            this.service.SelectedDeviceProperties.ShouldNotBeNull();
        }

        [TestMethod]
        public void DisposeMakesServiceOffline()
        {
            this.service!.Dispose();
            this.service.Online.ShouldBeFalse();
            this.service.SelectedDeviceId.ShouldBe(-1);
            this.service.TotalAllocations.ShouldBe(0);
        }
    }
}
