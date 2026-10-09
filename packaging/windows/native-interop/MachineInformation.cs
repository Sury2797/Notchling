// Project-owned Setup interop. Built before packaging; never compiled on a user PC.
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Notchling.Setup {
    public static class MachineInformation {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
        public static ushort[] Read() {
            ushort processMachine;
            ushort nativeMachine;
            if (!IsWow64Process2(GetCurrentProcess(), out processMachine, out nativeMachine))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not identify the native Setup architecture.");
            return new ushort[] { processMachine == 0 ? nativeMachine : processMachine, nativeMachine };
        }
    }
}
