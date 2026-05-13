using System.Runtime.CompilerServices;
using Windows.Win32.Foundation;

namespace Windows.Win32.Graphics.Direct3D11;

internal unsafe partial struct ID3D11DeviceContext
{
    internal HRESULT GetDataResult(ID3D11Asynchronous* async, void* data, uint dataSize, uint flags)
    {
        return ((delegate* unmanaged[Stdcall]<ID3D11DeviceContext*, ID3D11Asynchronous*, void*, uint, uint, HRESULT>)lpVtbl[29])((ID3D11DeviceContext*)Unsafe.AsPointer(ref this), async, data, dataSize, flags);
    }
}