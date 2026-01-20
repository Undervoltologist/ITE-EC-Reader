using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ITE_EC_Reader.Generic;

public static class PawnIO
{
    private const string RESOURCE_NAME_SYS = "ITE_EC_Reader.Resources.PawnIO.sys";
    private const string RESOURCE_NAME_BIN = "ITE_EC_Reader.Resources.LpcIO.bin";

    private const string DRIVER_FILE_NAME = "PawnIO.sys";

    private static SafeFileHandle _driverHandle;
    private static int _currentSlot = -1;

    private const uint DEVICE_TYPE = 41394u << 16;
    private const uint IOCTL_LOAD_BINARY = DEVICE_TYPE | (0x821 << 2);
    private const uint IOCTL_EXECUTE_FN = DEVICE_TYPE | (0x841 << 2);
    private const int FN_NAME_LENGTH = 32;

    public static bool IsInitialized = false;

    public static bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        try
        {
            EnsureDriverInstalled();

            _driverHandle = CreateFile(@"\\.\Global\PawnIO", 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_driverHandle.IsInvalid)
            {
                _driverHandle = CreateFile(@"\\.\PawnIO", 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (_driverHandle.IsInvalid)
                {
                    return false;
                }
            }

            byte[] binBytes = LoadResourceBytes(RESOURCE_NAME_BIN);

            DeviceIoControl(_driverHandle, IOCTL_LOAD_BINARY, binBytes, (uint)binBytes.Length, null, 0, out _, IntPtr.Zero);
            ExecutePawn("ioctl_find_bars");

            IsInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PawnIO Init Failed: {ex.Message}");
            return false;
        }
    }

    private static void EnsureSlotForPort(uint port)
    {
        int targetSlot = -1;

        if (port == 0x2E || port == 0x2F)
        {
            targetSlot = 0;
        }

        else if (port == 0x4E || port == 0x4F)
        {
            targetSlot = 1;
        }

        if (targetSlot == -1)
        {
            return;
        }

        if (_currentSlot != targetSlot)
        {
            ExecutePawn("ioctl_select_slot", targetSlot);
            _currentSlot = targetSlot;
        }
    }

    public static byte ReadIoPortByte(uint port)
    {
        if (!IsInitialized)
        {
            Initialize();
        }

        EnsureSlotForPort(port);

        var result = ExecutePawn("ioctl_pio_inb", (long)port);
        return result.Length > 0 ? (byte)(result[0] & 0xFF) : (byte)0;
    }

    public static void WriteIoPortByte(uint port, byte value)
    {
        if (!IsInitialized)
        {
            Initialize();
        }

        EnsureSlotForPort(port);

        ExecutePawn("ioctl_pio_outb", (long)port, (long)value);
    }

    public static byte DirectEcRead(byte ecAddrPort, byte ecDataPort, ushort addr)
    {
        WriteIoPortByte(ecAddrPort, 0x2E);
        WriteIoPortByte(ecDataPort, 0x11);
        WriteIoPortByte(ecAddrPort, 0x2F);
        WriteIoPortByte(ecDataPort, (byte)((addr >> 8) & 0xFF));
        WriteIoPortByte(ecAddrPort, 0x2E);
        WriteIoPortByte(ecDataPort, 0x10);
        WriteIoPortByte(ecAddrPort, 0x2F);
        WriteIoPortByte(ecDataPort, (byte)(addr & 0xFF));
        WriteIoPortByte(ecAddrPort, 0x2E);
        WriteIoPortByte(ecDataPort, 0x12);
        WriteIoPortByte(ecAddrPort, 0x2F);
        var result = ReadIoPortByte(ecDataPort);
        return result;
    }

    public static byte[] DirectEcReadArray(byte ecAddrPort, byte ecDataPort, ushort baseAddr, int size)
    {
        if (!IsInitialized)
        {
            Initialize();
        }

        EnsureSlotForPort(ecAddrPort);

        var buffer = new byte[size];
        for (var i = 0; i < size; i++)
        {
            buffer[i] = DirectEcRead(ecAddrPort, ecDataPort, (ushort)(baseAddr + i));
        }
        return buffer;
    }

    public static void DirectEcWrite(byte ecAddrPort, byte ecDataPort, ushort addr, byte data)
    {
        WriteIoPortByte(ecAddrPort, 0x2E);
        WriteIoPortByte(ecDataPort, 0x11);
        WriteIoPortByte(ecAddrPort, 0x2F);
        WriteIoPortByte(ecDataPort, (byte)((addr >> 8) & 0xFF));
        WriteIoPortByte(ecAddrPort, 0x2E);
        WriteIoPortByte(ecDataPort, 0x10);
        WriteIoPortByte(ecAddrPort, 0x2F);
        WriteIoPortByte(ecDataPort, (byte)(addr & 0xFF));
        WriteIoPortByte(ecAddrPort, 0x2E);
        WriteIoPortByte(ecDataPort, 0x12);
        WriteIoPortByte(ecAddrPort, 0x2F);
        WriteIoPortByte(ecDataPort, data);
    }

    public static void DirectEcWriteArray(byte ecAddrPort, byte ecDataPort, ushort baseAddr, byte[] data)
    {
        if (!IsInitialized)
        {
            Initialize();
        }

        EnsureSlotForPort(ecAddrPort);

        for (var i = 0; i < data.Length; i++)
        {
            DirectEcWrite(ecAddrPort, ecDataPort, (ushort)(baseAddr + i), data[i]);
        }
    }

    private static void EnsureDriverInstalled()
    {
        string destPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DRIVER_FILE_NAME);
        if (!File.Exists(destPath))
        {
            byte[] sysBytes = LoadResourceBytes(RESOURCE_NAME_SYS);
            File.WriteAllBytes(destPath, sysBytes);
        }
    }

    private static byte[] LoadResourceBytes(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static long[] ExecutePawn(string funcName, params long[] args)
    {
        if (_driverHandle == null || _driverHandle.IsInvalid) return [];

        int inputSize = FN_NAME_LENGTH + (args.Length * 8);
        byte[] inputBuffer = new byte[inputSize];
        byte[] nameBytes = Encoding.ASCII.GetBytes(funcName);
        Array.Copy(nameBytes, inputBuffer, Math.Min(FN_NAME_LENGTH - 1, nameBytes.Length));

        if (args.Length > 0)
            Buffer.BlockCopy(args, 0, inputBuffer, FN_NAME_LENGTH, args.Length * 8);

        byte[] outputBuffer = new byte[8];
        if (DeviceIoControl(_driverHandle, IOCTL_EXECUTE_FN, inputBuffer, (uint)inputBuffer.Length, outputBuffer, (uint)outputBuffer.Length, out uint bytesReturned, IntPtr.Zero))
        {
            if (bytesReturned >= 8)
            {
                long[] result = new long[1];
                Buffer.BlockCopy(outputBuffer, 0, result, 0, 8);
                return result;
            }
        }
        return [];
    }

    public static void Close()
    {
        if (!IsInitialized)
        {
            return;
        }

        try
        {
            if (_driverHandle is not { IsClosed: false } || _driverHandle.IsInvalid)
            {
                return;
            }

            _driverHandle.Close();
            _driverHandle.Dispose();
            _driverHandle = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error closing PawnIO handle: {ex.Message}");
        }
        finally
        {
            IsInitialized = false;
            _currentSlot = -1;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);
}
