using System.Runtime.InteropServices;

namespace Suite.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left, Top, Right, Bottom;
}
