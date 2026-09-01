using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Interfaces;
using ManagedCuda.BasicTypes;
using Shouldly;

namespace AsynCUDA13.Tests.Shared;

[TestClass]
public sealed class RuntimeAbstractionTests : TestBase
{
    [TestMethod]
    [DataRow(typeof(byte), 1)]
    [DataRow(typeof(int), 4)]
    [DataRow(typeof(float), 4)]
    [DataRow(typeof(double), 8)]
    public void CudaMem_SingleBuffer_ComputesBackendNeutralMetadata(Type type, int elementSize)
    {
        // Arrange
        var pointer = new CUdeviceptr(123);

        // Act
        var memory = new CudaMem(pointer, 7, type);

        // Assert
        memory.ShouldSatisfyAllConditions(
            value => value.Id.ShouldNotBe(Guid.Empty),
            value => value.Count.ShouldBe(1),
            value => value.IndexPointer.ShouldBe(new IntPtr(123)),
            value => value.IndexLength.ShouldBe(7),
            value => value.ElementType.ShouldBe(type),
            value => value.ElementSize.ShouldBe(elementSize),
            value => value.TotalLength.ShouldBe(7),
            value => value.TotalSize.ShouldBe(7L * elementSize));
    }

    [TestMethod]
    public void CudaMem_GroupBuffer_PreservesOrderAndTotals()
    {
        // Arrange
        CUdeviceptr[] pointers = [new(10), new(20), new(30)];
        IntPtr[] lengths = [2, 3, 5];

        // Act
        IRuntimeMem memory = new CudaMem(pointers, lengths, typeof(float));

        // Assert
        memory.PointerIds.ShouldBe(new IntPtr[] { 10, 20, 30 });
        memory.PointerLengths.ShouldBe(new long[] { 2, 3, 5 });
        memory.Count.ShouldBe(3);
        memory.TotalLength.ShouldBe(10);
        memory.TotalSize.ShouldBe(40);
    }

    [TestMethod]
    public void CudaMem_AssetReferenceId_ProvidesSingleAndArrayViews()
    {
        // Arrange
        var memory = new CudaMem(new CUdeviceptr(1), 1, typeof(byte));
        var id = Guid.NewGuid();

        // Act
        memory.AssetReferenceId = id;

        // Assert
        memory.AssetReferenceId.ShouldBe(id);
        memory.AssetReferenceIds.ShouldBe([id]);

        // Act
        memory.AssetReferenceId = null;

        // Assert
        memory.AssetReferenceIds.ShouldBeEmpty();
    }

    [TestMethod]
    public void CudaMem_Dispose_ResetsMutableDescriptorButPreservesIdentity()
    {
        // Arrange
        var memory = new CudaMem(new CUdeviceptr(9), 4, typeof(int));
        var id = memory.Id;
        var createdAt = memory.CreatedAt;

        // Act
        var releasedElements = memory.Dispose();

        // Assert
        releasedElements.ShouldBe(4);
        memory.Id.ShouldBe(id);
        memory.CreatedAt.ShouldBe(createdAt);
        memory.Pointers.ShouldBeEmpty();
        memory.PointerLengths.ShouldBeEmpty();
        memory.Count.ShouldBe(0);
        memory.TotalLength.ShouldBe(0);
    }

    [TestMethod]
    public void RuntimeInterfaces_ExposeInterchangeableCoreContracts()
    {
        // Arrange
        Type[] serviceContracts = [typeof(IRuntimeService), typeof(IRuntimeRegister), typeof(IRuntimeCompiler), typeof(IRuntimeLauncher), typeof(IRuntimeFourier)];

        // Act
        var methodNames = serviceContracts.ToDictionary(
            type => type,
            type => type.GetMethods().Select(method => method.Name).ToHashSet());

        // Assert
        methodNames[typeof(IRuntimeService)].ShouldContain(nameof(IRuntimeService.Initialize));
        methodNames[typeof(IRuntimeService)].ShouldContain(nameof(IRuntimeService.FreeMemory));
        methodNames[typeof(IRuntimeRegister)].ShouldContain(nameof(IRuntimeRegister.FreeMemory));
        methodNames[typeof(IRuntimeCompiler)].ShouldContain(nameof(IRuntimeCompiler.CompileKernel));
        methodNames[typeof(IRuntimeLauncher)].ShouldContain(nameof(IRuntimeLauncher.Execute));
        methodNames[typeof(IRuntimeFourier)].ShouldContain(nameof(IRuntimeFourier.PerformFft));
        serviceContracts.ShouldAllBe(type => type.IsInterface);
    }
}
