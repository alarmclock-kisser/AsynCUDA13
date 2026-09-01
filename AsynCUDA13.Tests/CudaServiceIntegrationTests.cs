using AsynCUDA13.Runtime;
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
            this.service = HardwareTestGuard.CreateCudaService(this.Logger);
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
