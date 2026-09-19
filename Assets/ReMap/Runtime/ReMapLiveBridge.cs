using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ReMap.Standalone.Core;

namespace ReMap.Standalone
{
    internal static class ReMapLiveBridge
    {
        internal const string HelperFileName = "ReMapLiveBridge.exe";

        internal static int Send(IEnumerable<string> sourceCommands)
        {
            string[] commands = (sourceCommands ?? Enumerable.Empty<string>())
                .SelectMany(command => (command ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                .Select(command => command.Trim()).Where(command => command.Length > 0).ToArray();
            if (commands.Length == 0) throw new ArgumentException(L.T("#ENTER_COMMAND_SEND"));
            if (commands.Any(command => command.Length > 32767)) throw new ArgumentException(L.T("#SERVER_COMMAND_TOO_LONG"));

            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HelperFileName);
            if (!File.Exists(helper)) throw new FileNotFoundException(L.T("#FLOWSTATE_LAUNCHER_CONNECTOR_MISSING_REBUILD"), helper);
            var output = new StringBuilder();
            object outputLock = new object();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = helper,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data != null) lock (outputLock) output.AppendLine(eventArgs.Data); };
                process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data != null) lock (outputLock) output.AppendLine(eventArgs.Data); };
                if (!process.Start()) throw new InvalidOperationException(L.T("#COULD_START_FLOWSTATE_LAUNCHER_CONNECTOR"));
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                foreach (string command in commands) process.StandardInput.WriteLine(command);
                process.StandardInput.Close();
                int timeout = Math.Min(600000, 10000 + commands.Length * 100);
                if (!process.WaitForExit(timeout))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException(L.T("#FLOWSTATE_LAUNCHER_DID_ACCEPT_COMMANDS"));
                }
                process.WaitForExit();
                string transcript; lock (outputLock) transcript = output.ToString().Trim();
                if (process.ExitCode != 0) throw new InvalidOperationException(transcript);
            }
            return commands.Length;
        }

        internal static string SelectFile(bool save, string title, string suggestedName, string extension)
        {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HelperFileName);
            if (!File.Exists(helper)) throw new FileNotFoundException("ReMap file dialog helper is missing. Rebuild ReMap.", helper);
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = helper,
                    Arguments = "--file-dialog",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (!process.Start()) throw new InvalidOperationException("Could not start the ReMap file dialog helper.");
                process.StandardInput.WriteLine(save ? "save" : "open");
                process.StandardInput.WriteLine(Encode(title));
                process.StandardInput.WriteLine(Encode(suggestedName));
                process.StandardInput.WriteLine(Encode(extension));
                process.StandardInput.Close();
                string selected = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
                string error = process.StandardError.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode == 2) return null;
                if (process.ExitCode != 0) throw new InvalidOperationException(error.Length == 0 ? "The ReMap file dialog failed." : error);
                return selected;
            }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }
    }
}
