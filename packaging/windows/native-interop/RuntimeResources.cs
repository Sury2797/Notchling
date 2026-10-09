// Project-owned Setup interop. Built before packaging; never compiled on a user PC.
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
namespace Notchling.Setup {
    public static class RuntimeResources {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr module, string name, string type);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);
        public static string[] Extract(string installer, string directory, string[] names) {
            if (names == null || names.Length < 4 || names.Length > 5) throw new InvalidDataException("Four or five Microsoft runtime resource names are required.");
            string[] paths = new string[names.Length];
            // LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE:
            // no import resolution, DllMain, or application code execution.
            IntPtr module = LoadLibraryEx(installer, IntPtr.Zero, 0x2 | 0x20);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the verified Microsoft installer as resource data.");
            try {
                long total = 0;
                for (int index = 0; index < names.Length; index++) {
                    IntPtr resource = FindResource(module, names[index], "PACKAGE");
                    if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The verified installer did not contain " + names[index] + ".");
                    uint size = SizeofResource(module, resource);
                    total += size;
                    if (size < 4 || size > 167772160 || total > 268435456) throw new InvalidDataException("A Microsoft runtime resource exceeded its size limit.");
                    IntPtr loaded = LoadResource(module, resource);
                    if (loaded == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load the Microsoft runtime resource.");
                    IntPtr data = LockResource(loaded);
                    if (data == IntPtr.Zero || Marshal.ReadInt32(data) != 0x04034B50) throw new InvalidDataException("A Microsoft runtime resource was not an MSIX archive.");
                    paths[index] = Path.Combine(directory, names[index] + ".msix");
                    using (FileStream output = new FileStream(paths[index], FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        byte[] buffer = new byte[65536];
                        for (long offset = 0; offset < size; offset += buffer.Length) {
                            int count = (int)Math.Min(buffer.Length, size - offset);
                            Marshal.Copy(new IntPtr(data.ToInt64() + offset), buffer, 0, count);
                            output.Write(buffer, 0, count);
                        }
                    }
                }
                return paths;
            }
            finally { FreeLibrary(module); }
        }
    }
}
