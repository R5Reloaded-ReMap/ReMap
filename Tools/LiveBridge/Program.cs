using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

internal static class Program
{
    private const int SystemExtendedHandleInformation = 64;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const uint ProcessDuplicateHandle = 0x0040;
    private const uint DuplicateSameAccess = 0x00000002;
    private const uint FileTypePipe = 0x0003;
    private const uint FlowstateConsoleWriteAccess = 0x0116;
    private const int ObjectNameInformation = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleEntry
    {
        internal IntPtr Object;
        internal IntPtr ProcessId;
        internal IntPtr Handle;
        internal uint GrantedAccess;
        internal ushort CreatorBackTraceIndex;
        internal ushort ObjectTypeIndex;
        internal uint HandleAttributes;
        internal uint Reserved;
    }

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--file-dialog") return ShowFileDialog();
        bool clientConsole = arguments.Length == 1 && arguments[0] == "--client-console";
        string[] commands = Console.In.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(command => command.Trim()).Where(command => command.Length > 0).ToArray();
        if (commands.Length == 0) return Fail("No console command was provided.");
        Process launcher = Process.GetProcessesByName("R5FlowstateLauncher").FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);
        if (launcher == null) return Fail("Start the Flowstate launcher and a local game first.");
        try
        {
            IntPtr pipe = FindConsolePipe(launcher.Id, clientConsole);
            if (pipe == IntPtr.Zero) return Fail("The Flowstate " + (clientConsole ? "client" : "server") + " console pipe was not found. Start a local game first.");
            try
            {
                foreach (string command in commands)
                {
                    if (command.Length > 512) return Fail("Flowstate console commands cannot exceed 512 characters.");
                    byte[] payload = Encoding.Default.GetBytes(command + "\n");
                    uint written;
                    if (!WriteFile(pipe, payload, (uint)payload.Length, out written, IntPtr.Zero) || written != (uint)payload.Length)
                        return Fail("The Flowstate console did not accept the command. Windows error " + Marshal.GetLastWin32Error() + ".");
                }
            }
            finally { CloseHandle(pipe); }
            Console.Out.WriteLine(commands.Length);
            return 0;
        }
        catch (Exception exception) { return Fail(exception.Message); }
        finally { launcher.Dispose(); }
    }

    private static int ShowFileDialog()
    {
        try
        {
            string[] options = Console.In.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (options.Length < 4) return Fail("The ReMap file dialog request is incomplete.");
            bool save = options[0] == "save";
            string title = Decode(options[1]);
            string suggestedName = Decode(options[2]);
            string extension = Decode(options[3]).TrimStart('.');
            FileDialog dialog = save ? (FileDialog)new SaveFileDialog() : new OpenFileDialog();
            dialog.Title = title;
            dialog.FileName = suggestedName;
            dialog.DefaultExt = extension;
            dialog.AddExtension = true;
            dialog.CheckPathExists = true;
            if (save) ((SaveFileDialog)dialog).OverwritePrompt = true;
            else ((OpenFileDialog)dialog).CheckFileExists = true;
            string description = extension.Equals("ent", StringComparison.OrdinalIgnoreCase) ? "Apex entity lump" : "ReMap project";
            dialog.Filter = description + " (*." + extension + ")|*." + extension + "|All files (*.*)|*.*";
            bool? accepted = dialog.ShowDialog();
            if (accepted != true) return 2;
            Console.Out.WriteLine(dialog.FileName);
            return 0;
        }
        catch (Exception exception) { return Fail(exception.Message); }
    }

    private static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
    }
    private static IntPtr FindConsolePipe(int launcherProcessId, bool clientConsole)
    {
        IntPtr launcher = OpenProcess(ProcessDuplicateHandle, false, launcherProcessId);
        if (launcher == IntPtr.Zero) return IntPtr.Zero;
        IntPtr handles = IntPtr.Zero;
        try
        {
            int length = 1024 * 1024;
            int required;
            int status;
            while (true)
            {
                handles = Marshal.AllocHGlobal(length);
                status = NtQuerySystemInformation(SystemExtendedHandleInformation, handles, length, out required);
                if (status != StatusInfoLengthMismatch) break;
                Marshal.FreeHGlobal(handles);
                handles = IntPtr.Zero;
                length = Math.Max(length * 2, required + 65536);
            }
            if (status < 0) return IntPtr.Zero;

            long count = Marshal.ReadIntPtr(handles).ToInt64();
            int entrySize = Marshal.SizeOf(typeof(SystemHandleEntry));
            IntPtr entryAddress = IntPtr.Add(handles, IntPtr.Size * 2);
            string marker = "\\r5f-in-" + (clientConsole ? "c" : "s") + "-";
            for (long index = 0; index < count; index++)
            {
                var entry = (SystemHandleEntry)Marshal.PtrToStructure(entryAddress, typeof(SystemHandleEntry));
                entryAddress = IntPtr.Add(entryAddress, entrySize);
                if (entry.ProcessId.ToInt64() != launcherProcessId) continue;
                if ((entry.GrantedAccess & 0xFFFF) != FlowstateConsoleWriteAccess) continue;
                IntPtr duplicated;
                if (!DuplicateHandle(launcher, entry.Handle, GetCurrentProcess(), out duplicated, 0, false, DuplicateSameAccess)) continue;
                if (GetFileType(duplicated) == FileTypePipe)
                {
                    string name = ObjectName(duplicated);
                    if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return duplicated;
                }
                CloseHandle(duplicated);
            }
            return IntPtr.Zero;
        }
        finally
        {
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            CloseHandle(launcher);
        }
    }

    private static string ObjectName(IntPtr handle)
    {
        int required;
        NtQueryObject(handle, ObjectNameInformation, IntPtr.Zero, 0, out required);
        if (required <= 0) return "";
        IntPtr information = Marshal.AllocHGlobal(required);
        try
        {
            if (NtQueryObject(handle, ObjectNameInformation, information, required, out required) < 0) return "";
            int length = Marshal.ReadInt16(information);
            IntPtr value = Marshal.ReadIntPtr(information, IntPtr.Size == 8 ? 8 : 4);
            return length <= 0 || value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(value, length / 2);
        }
        finally { Marshal.FreeHGlobal(information); }
    }

    private static int Fail(string message) { Console.Error.WriteLine(message); return 1; }

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int informationLength, out int returnLength);
    [DllImport("ntdll.dll")] private static extern int NtQueryObject(IntPtr handle, int informationClass, IntPtr information, int informationLength, out int returnLength);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle, uint access, bool inheritHandle, uint options);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr handle, byte[] buffer, uint bytesToWrite, out uint bytesWritten, IntPtr overlapped);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
