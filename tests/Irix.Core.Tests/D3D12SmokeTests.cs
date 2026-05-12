using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi.Common;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Headless D3D12 smoke tests. No window, no swapchain, no GPU execution.
/// Validates device creation, command list recording, and resource allocation.
/// Tests skip gracefully if D3D12 is not available (e.g. CI without GPU).
/// </summary>
[Trait("Category", "D3D12")]
public sealed unsafe class D3D12SmokeTests
{
    private static ID3D12Device* CreateDevice()
    {
        PInvoke.D3D12CreateDevice(null, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, typeof(ID3D12Device).GUID, out void* obj);
        return (ID3D12Device*)obj;
    }

    private static bool? _d3d12Available;

    private static bool CheckD3D12Available()
    {
        if (_d3d12Available.HasValue) return _d3d12Available.Value;
        try
        {
            var device = CreateDevice();
            if ((nint)device == 0)
            {
                _d3d12Available = false;
                return false;
            }
            device->Release();
            _d3d12Available = true;
            return true;
        }
        catch
        {
            _d3d12Available = false;
            return false;
        }
    }

    private static ID3D12Device* CreateDeviceOrSkip()
    {
        if (!CheckD3D12Available())
        {
            Assert.Skip("D3D12 not available in this environment");
            return null; // unreachable
        }
        return CreateDevice();
    }

    [Fact]
    public void D3D12_device_creation_succeeds()
    {
        var device = CreateDeviceOrSkip();
        Assert.True((nint)device != 0, "D3D12CreateDevice returned null device");
        device->Release();
    }

    [Fact]
    public void CommandAllocator_and_CommandList_recording_succeeds()
    {
        var device = CreateDeviceOrSkip();

        device->CreateCommandAllocator(
            D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,
            typeof(ID3D12CommandAllocator).GUID,
            out void* allocObj);
        var allocator = (ID3D12CommandAllocator*)allocObj;

        device->CreateCommandList(
            0,
            D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,
            allocator,
            null,
            typeof(ID3D12GraphicsCommandList).GUID,
            out void* listObj);
        var list = (ID3D12GraphicsCommandList*)listObj;

        // Close immediately — no execution, just verify recording works
        list->Close();

        list->Release();
        allocator->Release();
        device->Release();
    }

    [Fact]
    public void DescriptorHeap_creation_succeeds()
    {
        var device = CreateDeviceOrSkip();

        var desc = new D3D12_DESCRIPTOR_HEAP_DESC
        {
            NumDescriptors = 2,
            Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV
        };

        device->CreateDescriptorHeap(desc, typeof(ID3D12DescriptorHeap).GUID, out void* heapObj);
        var heap = (ID3D12DescriptorHeap*)heapObj;

        var cpuHandle = heap->GetCPUDescriptorHandleForHeapStart();
        Assert.True(cpuHandle.ptr != 0, "Descriptor heap CPU handle should be non-zero");

        var incrementSize = device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        Assert.True(incrementSize > 0, "RTV descriptor increment size should be > 0");

        heap->Release();
        device->Release();
    }

    [Fact]
    public void UploadBuffer_creation_succeeds()
    {
        var device = CreateDeviceOrSkip();

        var heapProps = new D3D12_HEAP_PROPERTIES
        {
            Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD
        };
        var resourceDesc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
            Width = 256,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR
        };

        device->CreateCommittedResource(
            heapProps,
            D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
            resourceDesc,
            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
            null,
            typeof(ID3D12Resource).GUID,
            out void* resObj);

        var resource = (ID3D12Resource*)resObj;
        var gpuAddr = resource->GetGPUVirtualAddress();
        Assert.True(gpuAddr != 0, "Upload buffer GPU virtual address should be non-zero");

        resource->Release();
        device->Release();
    }

    [Fact]
    public void CommandQueue_creation_succeeds()
    {
        var device = CreateDeviceOrSkip();

        var qd = new D3D12_COMMAND_QUEUE_DESC
        {
            Type = D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT
        };

        device->CreateCommandQueue(qd, typeof(ID3D12CommandQueue).GUID, out void* queueObj);
        Assert.True((nint)queueObj != 0, "Command queue creation returned null");

        var queue = (ID3D12CommandQueue*)queueObj;
        queue->Release();
        device->Release();
    }

    [Fact]
    public void Fence_creation_succeeds()
    {
        var device = CreateDeviceOrSkip();

        device->CreateFence(0, D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE, typeof(ID3D12Fence).GUID, out void* fenceObj);
        var fence = (ID3D12Fence*)fenceObj;

        Assert.Equal(0UL, fence->GetCompletedValue());

        fence->Release();
        device->Release();
    }

}
