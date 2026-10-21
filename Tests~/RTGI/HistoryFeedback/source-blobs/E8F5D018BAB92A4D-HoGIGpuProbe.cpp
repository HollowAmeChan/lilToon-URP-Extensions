// Test-only timestamp bridge. Uses Unity's existing D3D12 command list/device;
// no rendering, descriptor interception, new device or product native dependency.
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <atomic>
#include <cstring>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D12.h"
using Microsoft::WRL::ComPtr;
constexpr int Capacity=8192;
struct Slot { std::atomic<int> begun{0},ended{0},resolved{0},error{0};std::atomic<UINT64> fence{0}; };
static Slot slots[Capacity];
static IUnityInterfaces* interfaces=nullptr;
static IUnityGraphics* graphics=nullptr;
static IUnityGraphicsD3D12v7* d3d=nullptr;
static ComPtr<ID3D12QueryHeap> queries;
static ComPtr<ID3D12Resource> readback;
static ID3D12Fence* frameFence=nullptr;
static UINT64* mapped=nullptr;
static std::atomic<UINT64> frequency{0};
static std::atomic<int> ready{0},globalError{0},nextSlot{0};
static int eventBase=-1;

static void Release()
{
    ready.store(0);frequency.store(0);frameFence=nullptr;
    if(readback&&mapped)readback->Unmap(0,nullptr);
    mapped=nullptr;readback.Reset();queries.Reset();
}
static void InitializeOnSubmission()
{
    if(ready.load()||!d3d)return;
    auto queue=d3d->GetCommandQueue();auto device=d3d->GetDevice();UINT64 hz=0;
    if(!queue||!device||FAILED(queue->GetTimestampFrequency(&hz))||hz==0){globalError.store(-2);return;}
    D3D12_QUERY_HEAP_DESC queryDesc={};queryDesc.Count=Capacity*2;queryDesc.Type=D3D12_QUERY_HEAP_TYPE_TIMESTAMP;
    if(FAILED(device->CreateQueryHeap(&queryDesc,IID_PPV_ARGS(&queries)))){globalError.store(-3);return;}
    D3D12_HEAP_PROPERTIES heap={};heap.Type=D3D12_HEAP_TYPE_READBACK;
    D3D12_RESOURCE_DESC buffer={};buffer.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;buffer.Width=Capacity*16;buffer.Height=1;buffer.DepthOrArraySize=1;buffer.MipLevels=1;buffer.SampleDesc.Count=1;buffer.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    if(FAILED(device->CreateCommittedResource(&heap,D3D12_HEAP_FLAG_NONE,&buffer,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&readback)))){globalError.store(-4);return;}
    D3D12_RANGE range={0,Capacity*16};
    if(FAILED(readback->Map(0,&range,reinterpret_cast<void**>(&mapped)))){globalError.store(-5);return;}
    frameFence=d3d->GetFrameFence();if(!frameFence){globalError.store(-6);return;}
    frequency.store(hz);ready.store(1,std::memory_order_release);
}
static void UNITY_INTERFACE_API RenderEvent(int eventId,void* data)
{
    if(eventId==eventBase){InitializeOnSubmission();return;}
    if(!ready.load(std::memory_order_acquire)||!data){globalError.store(-7);return;}
    int index=*reinterpret_cast<int*>(data);if(index<0||index>=Capacity){globalError.store(-8);return;}
    UnityGraphicsD3D12RecordingState recording={};
    if(!d3d->CommandRecordingState(&recording)||!recording.commandList){slots[index].error.store(-9);return;}
    if(eventId==eventBase+1)
    {
        if(slots[index].begun.exchange(1)){slots[index].error.store(-10);return;}
        recording.commandList->EndQuery(queries.Get(),D3D12_QUERY_TYPE_TIMESTAMP,index*2);
    }
    else if(eventId==eventBase+2)
    {
        if(!slots[index].begun.load()||slots[index].ended.load()){slots[index].error.store(-11);return;}
        recording.commandList->EndQuery(queries.Get(),D3D12_QUERY_TYPE_TIMESTAMP,index*2+1);
        slots[index].ended.store(1,std::memory_order_release);
    }
    else if(eventId==eventBase+3)
    {
        // Resolve outside any native render pass. EndQuery can be emitted in a
        // raster pass, but ResolveQueryData must not be recorded inside it.
        if(!slots[index].ended.load(std::memory_order_acquire)||slots[index].resolved.load()){slots[index].error.store(-15);return;}
        recording.commandList->ResolveQueryData(queries.Get(),D3D12_QUERY_TYPE_TIMESTAMP,index*2,2,readback.Get(),index*16);
        UINT64 fence=d3d->GetNextFrameFenceValue();if(!fence){slots[index].error.store(-12);return;}
        slots[index].fence.store(fence);slots[index].resolved.store(1,std::memory_order_release);
    }
}
static void UNITY_INTERFACE_API DeviceEvent(UnityGfxDeviceEventType type)
{
    if(type==kUnityGfxDeviceEventShutdown){Release();d3d=nullptr;return;}
    if(type!=kUnityGfxDeviceEventInitialize||graphics->GetRenderer()!=kUnityGfxRendererD3D12)return;
    d3d=interfaces->Get<IUnityGraphicsD3D12v7>();if(!d3d){globalError.store(-1);return;}
    if(eventBase<0)eventBase=graphics->ReserveEventIDRange(4);
    UnityD3D12PluginEventConfig config={};config.graphicsQueueAccess=kUnityD3D12GraphicsQueueAccess_Allow;config.flags=kUnityD3D12EventConfigFlag_FlushCommandBuffers;
    d3d->ConfigureEvent(eventBase,&config);
    config.graphicsQueueAccess=kUnityD3D12GraphicsQueueAccess_DontCare;config.flags=0;
    d3d->ConfigureEvent(eventBase+1,&config);d3d->ConfigureEvent(eventBase+2,&config);d3d->ConfigureEvent(eventBase+3,&config);
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unity)
{
    interfaces=unity;graphics=unity->Get<IUnityGraphics>();graphics->RegisterDeviceEventCallback(DeviceEvent);DeviceEvent(kUnityGfxDeviceEventInitialize);
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{if(graphics)graphics->UnregisterDeviceEventCallback(DeviceEvent);Release();}
extern "C" __declspec(dllexport) void* HoGpu_Event(){return reinterpret_cast<void*>(RenderEvent);}
extern "C" __declspec(dllexport) int HoGpu_Base(){return eventBase;}
extern "C" __declspec(dllexport) int HoGpu_Status(){return globalError.load()?globalError.load():ready.load();}
extern "C" __declspec(dllexport) int HoGpu_Allocate(){int index=nextSlot.fetch_add(1);return ready.load()&&index<Capacity?index:-1;}
struct Result {UINT64 begin,end,frequency,targetFence,completedFence;int error,ready;};
extern "C" __declspec(dllexport) int HoGpu_Poll(int index,Result* result)
{
    if(!result||index<0||index>=Capacity)return -1;*result={};
    result->frequency=frequency.load();result->error=slots[index].error.load();
    if(result->error)return result->error;
    if(!ready.load()||!slots[index].resolved.load(std::memory_order_acquire))return 0;
    result->targetFence=slots[index].fence.load();result->completedFence=frameFence->GetCompletedValue();
    if(result->completedFence==UINT64_MAX){result->error=-13;return -13;}
    if(result->completedFence<result->targetFence)return 0;
    std::memcpy(&result->begin,mapped+index*2,16);
    // An empty interval may quantize to one timestamp. The managed workload
    // qualifier separately requires positive duration for nonempty dispatches.
    if(!result->frequency||!result->begin||result->end<result->begin){result->error=-14;return -14;}
    result->ready=1;return 1;
}
