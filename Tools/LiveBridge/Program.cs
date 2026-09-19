using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using System.Threading;
using System.Windows.Automation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--file-dialog") return ShowFileDialog();
        string[] commands = Console.In.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(command => command.Trim()).Where(command => command.Length > 0).ToArray();
        if (commands.Length == 0) return Fail("No server command was provided.");
        Process launcher = Process.GetProcessesByName("R5FlowstateLauncher").FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);
        if (launcher == null) return Fail("Start the Flowstate launcher and a local game first.");
        IntPtr previous = GetForegroundWindow();
        try
        {
            AutomationElement root = AutomationElement.FromHandle(launcher.MainWindowHandle);
            OpenConsoleTab(root);
            AutomationElement input = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "TxtConsoleServerCmd"));
            if (input == null) return Fail("The Flowstate server console input was not found.");
            if (!(bool)input.GetCurrentPropertyValue(AutomationElement.IsEnabledProperty)) return Fail("Start a local Flowstate game before sending commands.");
            if (IsIconic(launcher.MainWindowHandle)) ShowWindow(launcher.MainWindowHandle, 9);
            SetForegroundWindow(launcher.MainWindowHandle);
            var value = (ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern);
            foreach (string command in commands)
            {
                value.SetValue(command);
                input.SetFocus();
                Thread.Sleep(15);
                keybd_event(0x0D, 0, 0, UIntPtr.Zero);
                keybd_event(0x0D, 0, 0x0002, UIntPtr.Zero);
                Thread.Sleep(55);
            }
            Console.Out.WriteLine(commands.Length);
            return 0;
        }
        catch (Exception exception) { return Fail(exception.Message); }
        finally
        {
            if (previous != IntPtr.Zero && previous != launcher.MainWindowHandle) SetForegroundWindow(previous);
            launcher.Dispose();
        }
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
    private static void OpenConsoleTab(AutomationElement root)
    {
        AutomationElement tab = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "BtnTabConsole"));
        object pattern;
        if (tab == null || !tab.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) return;
        ((InvokePattern)pattern).Invoke();
        Thread.Sleep(100);
    }

    private static int Fail(string message) { Console.Error.WriteLine(message); return 1; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
