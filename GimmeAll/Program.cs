using ITE_EC_Reader.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace ITE_EC_Reader;

public class Program
{
    // disable resize
    [DllImport("user32.dll")]
    private static extern int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    private const int SC_SIZE = 0xF000;
    private const int SC_MAXIMIZE = 0xF030;
    private const int MF_BYCOMMAND = 0x00000000;

    public static GlobalVariables globalVariables = new GlobalVariables();
    private static byte[]? _lastDataBuffer = null;
    private static int _lastPage = -1;
    private static Mutex? _singleInstanceMutex;
    private static readonly object _ecAccessLock = new object();

    public static async Task Main(string[] args)
    {
        if (!EnsureSingleInstance()) return;

        SetupConsole();
        Console.Title = "ITE EC Reader";

        PawnIO.Initialize();

        if (PawnIO.IsInitialized)
        {
            if (!File.Exists("config.json")) globalVariables.SaveToFile("config.json");
            globalVariables = GlobalVariables.LoadFromFile("config.json");
            ushort currentPage = 0;

            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Clear();
            Console.CursorVisible = false;

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.UpArrow)
                    {
                        if (currentPage >= 255) currentPage = 0;
                        else currentPage++;
                    }
                    else if (key == ConsoleKey.DownArrow)
                    {
                        if (currentPage == 0) currentPage = 255;
                        else currentPage--;
                    }

                    int skipCount = 0;
                    while (Console.KeyAvailable && skipCount < 3)
                    {
                        Console.ReadKey(true);
                        skipCount++;
                    }
                }

                ushort addr = (ushort)(globalVariables.Address + currentPage * 0x100);
                byte[] currentData;

                lock (_ecAccessLock)
                {
                    currentData = PawnIO.DirectEcReadArray(globalVariables.ActiveAddr, globalVariables.ActiveData, addr, 0x100);

                    // If 0xFF, try secondary ports
                    if (currentData.All(b => b == 0xFF))
                    {
                        globalVariables.UseSecondaryPorts = !globalVariables.UseSecondaryPorts;
                        currentData = PawnIO.DirectEcReadArray(globalVariables.ActiveAddr, globalVariables.ActiveData, addr, 0x100);

                        // If still 0xFF on both port sets, stop reading
                        if (currentData.All(b => b == 0xFF))
                        {
                            Console.Clear();
                            Console.SetCursorPosition(60, 14);
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Chip not supported");
                            break;
                        }
                    }
                }

                ShowDataFromEc(currentData, globalVariables.Address, currentPage);
                await Task.Delay(10);
            }
        }
        PawnIO.Close();
        _singleInstanceMutex?.ReleaseMutex();
        Console.ReadLine();
    }

    private static void SetupConsole()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.SetWindowSize(141, 30);

            Console.SetBufferSize(141, 30);

            IntPtr handle = GetConsoleWindow();
            IntPtr sysMenu = GetSystemMenu(handle, false);
            if (handle != IntPtr.Zero)
            {
                DeleteMenu(sysMenu, SC_SIZE, MF_BYCOMMAND);
                DeleteMenu(sysMenu, SC_MAXIMIZE, MF_BYCOMMAND);

            }
        }
    }

    public static void ShowDataFromEc(byte[] currentData, ushort startAddress, ushort page)
    {
        ushort currentBaseAddr = (ushort)(startAddress + page * 0x100);
        byte dcr4, dcr5, dcr6, ctr0, chipRev;
        int rpm0, rpm1, rpm2;
        string chipIdHex;

        lock (_ecAccessLock)
        {
            var idData = PawnIO.DirectEcReadArray(globalVariables.ActiveAddr, globalVariables.ActiveData, 0x2000, 3);
            chipIdHex = $"{idData[0]:X2}{idData[1]:X2}";
            chipRev = idData[2];
            ctr0 = PawnIO.DirectEcRead(globalVariables.ActiveAddr, globalVariables.ActiveData, 0x1801);
            dcr4 = PawnIO.DirectEcRead(globalVariables.ActiveAddr, globalVariables.ActiveData, 0x1806);
            dcr5 = PawnIO.DirectEcRead(globalVariables.ActiveAddr, globalVariables.ActiveData, 0x1807);
            dcr6 = PawnIO.DirectEcRead(globalVariables.ActiveAddr, globalVariables.ActiveData, 0x1808);
            rpm0 = GetRpm(0x181E, 0x181F);
            rpm1 = GetRpm(0x1820, 0x1821);
            rpm2 = GetRpm(0x1845, 0x1846);
        }

        double pwmScale = (ctr0 == 0) ? 255.0 : (double)ctr0;
        if (page != _lastPage) { _lastDataBuffer = null; _lastPage = page; }

        Console.SetCursorPosition(0, 0);
        Console.BackgroundColor = ConsoleColor.White;

        // render pages
        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.Write("     ");
        for (int i = 0; i < 16; i++) Console.Write($"{i:X2} ");
        Console.Write("| ");
        for (int i = 0; i < 16; i++) Console.Write($"{i:X2}  ");
        Console.WriteLine("| ASCII");

        string line = new string('-', 53) + "+" + new string('-', 65) + "+" + new string('-', 18);
        Console.WriteLine(line);

        for (int row = 0; row < 16; row++)
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.Write($"{(row * 16):X2} | ");
            for (int col = 0; col < 16; col++)
            {
                int idx = row * 16 + col;
                SetDataColor(currentData[idx], idx);
                Console.Write(currentData[idx].ToString("X2") + " ");
            }
            Console.ForegroundColor = ConsoleColor.DarkBlue; Console.Write("| ");
            for (int col = 0; col < 16; col++)
            {
                int idx = row * 16 + col;
                SetDataColor(currentData[idx], idx);
                Console.Write(currentData[idx].ToString("D3") + " ");
            }
            Console.ForegroundColor = ConsoleColor.DarkBlue; Console.Write("| ");
            for (int col = 0; col < 16; col++)
            {
                int idx = row * 16 + col;
                byte val = currentData[idx];
                SetDataColor(val, idx);
                Console.Write((val >= 32 && val <= 126) ? (char)val : '.');
            }
            Console.WriteLine("   ");
        }

        // ec info output
        Console.ForegroundColor = ConsoleColor.Black;
        string border = new string('=', 140);
        Console.WriteLine("\n" + border);

        Console.Write(" CHIP: ");
        Console.ForegroundColor = ConsoleColor.DarkGreen; Console.Write($"ITE {chipIdHex}");
        Console.ForegroundColor = ConsoleColor.Black; Console.Write(" | CHIP REV: ");
        Console.ForegroundColor = ConsoleColor.DarkGreen; Console.Write($"0x{chipRev:X2}");
        Console.ForegroundColor = ConsoleColor.Black;
        Console.WriteLine($" | ADDR: 0x{currentBaseAddr:X4} | PAGE: {page} | PORTS: {globalVariables.ActiveAddr:X2}/{globalVariables.ActiveData:X2}");

        Console.Write(" PWM STATUS: ");
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.Write($"CTR0 (PWM Max): {ctr0:D3} | ");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write($"DCR4: {dcr4:D3} ({Math.Round((dcr4 / pwmScale) * 100, 1)}%)  ");
        Console.Write($"DCR5: {dcr5:D3} ({Math.Round((dcr5 / pwmScale) * 100, 1)}%)  ");
        Console.Write($"DCR6: {dcr6:D3} ({Math.Round((dcr6 / pwmScale) * 100, 1)}%)  ");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Black;
        Console.Write(" FANS (RPM):  ");
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Write($"TACH0: {rpm0} RPM  |  ");
        Console.Write($"TACH1: {rpm1} RPM  |  ");
        Console.Write($"TACH2: {rpm2} RPM  ");

        Console.ForegroundColor = ConsoleColor.Black;
        Console.WriteLine("\n" + border);
        Console.Write(" Made by: https://github.com/Undervoltologist");

        _lastDataBuffer = (byte[])currentData.Clone();
    }

    private static int GetRpm(ushort lowAddr, ushort hiAddr)
    {
        byte low = PawnIO.DirectEcRead(globalVariables.ActiveAddr, globalVariables.ActiveData, lowAddr);
        byte hi = PawnIO.DirectEcRead(globalVariables.ActiveAddr, globalVariables.ActiveData, hiAddr);
        int combined = low + (hi << 8);
        if (combined <= 0 || combined == 0xFFFF) return 0;
        return 2156250 / combined; // divide by 0x2E6DA to get RPM
    }

    private static void SetDataColor(byte val, int index)
    {
        if (_lastDataBuffer != null && _lastDataBuffer[index] != val) Console.ForegroundColor = ConsoleColor.Red;
        else if (val == 0xFF) Console.ForegroundColor = ConsoleColor.Green;
        else if (val == 0x00) Console.ForegroundColor = ConsoleColor.Gray;
        else Console.ForegroundColor = ConsoleColor.Black;
    }

    private static bool EnsureSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, "Global\\GimmeAll_EC_Access_Mutex", out bool isNewInstance);
        return isNewInstance; 
    }
}