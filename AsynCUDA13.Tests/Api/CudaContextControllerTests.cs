using AsynCUDA13.Api.Controllers;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;

namespace AsynCUDA13.Tests.Api
{
    [TestClass]
    public class RuntimeContextControllerTests : TestBase
    {
        private RuntimeContextController _controller = null!;
        private Mock<ICudaService> _mockCuda = null!;

        [TestInitialize]
        public void SetUp()
        {
            this._mockCuda = new Mock<ICudaService>();
            this._controller = new RuntimeContextController(this._mockCuda.Object);
        }

        // =====================================================================
        // GET /api/cudacontext/devices
        // =====================================================================


    }
}
