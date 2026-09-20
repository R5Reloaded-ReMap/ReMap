#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace ReMap.Standalone
{
    internal sealed class DiscordIpcClient : IDisposable
    {
        private const int HandshakeOpcode = 0;
        private const int FrameOpcode = 1;
        private const int CloseOpcode = 2;
        private const int PingOpcode = 3;
        private const int PongOpcode = 4;
        private const int MaximumPayloadBytes = 1024 * 1024;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;

        private readonly string applicationId;
        private readonly int processId;
        private readonly long startedAt;
        private readonly object sync = new object();
        private readonly AutoResetEvent reconnect = new AutoResetEvent(false);
        private readonly Thread worker;
        private FileStream stream;
        private string desiredActivity;
        private string sentActivity;
        private bool ready;
        private bool disposed;

        public DiscordIpcClient(string applicationId)
        {
            this.applicationId = applicationId;
            processId = Process.GetCurrentProcess().Id;
            startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            worker = new Thread(Run) { IsBackground = true, Name = "ReMap Discord Rich Presence" };
            worker.Start();
        }

        public void SetActivity(string details, string state)
        {
            string activity = JsonUtility.ToJson(new Activity
            {
                details = details,
                state = state,
                timestamps = new ActivityTimestamps { start = startedAt }
            });
            lock (sync)
            {
                if (disposed || activity == desiredActivity) return;
                desiredActivity = activity;
                TrySendActivityLocked();
            }
            reconnect.Set();
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                if (ready && stream != null)
                {
                    try { WriteFrame(stream, FrameOpcode, ActivityCommand(null)); }
                    catch { }
                }
                disposed = true;
                CloseLocked();
            }
            reconnect.Set();
            if (Thread.CurrentThread != worker && worker.Join(500)) reconnect.Dispose();
        }

        private void Run()
        {
            while (true)
            {
                lock (sync) { if (disposed) return; }
                FileStream connection = null;
                try
                {
                    connection = Connect();
                    if (connection == null)
                    {
                        reconnect.WaitOne(5000);
                        continue;
                    }
                    lock (sync)
                    {
                        if (disposed) { connection.Dispose(); return; }
                        stream = connection;
                        ready = false;
                    }

                    WriteFrame(connection, HandshakeOpcode,
                        "{\"v\":1,\"client_id\":\"" + applicationId + "\"}");
                    Frame response = ReadFrame(connection);
                    if (response.Opcode == CloseOpcode) throw new IOException("Discord rejected the IPC handshake.");

                    lock (sync)
                    {
                        if (disposed || stream != connection) return;
                        ready = true;
                        sentActivity = null;
                        TrySendActivityLocked();
                    }
                    ReadLoop(connection);
                }
                catch { }
                finally
                {
                    lock (sync)
                    {
                        if (stream == connection) CloseLocked();
                        else try { connection?.Dispose(); } catch { }
                    }
                }
                reconnect.WaitOne(2000);
            }
        }

        private void ReadLoop(FileStream connection)
        {
            while (true)
            {
                Frame frame = ReadFrame(connection);
                if (frame.Opcode == CloseOpcode) return;
                if (frame.Opcode != PingOpcode) continue;
                lock (sync)
                {
                    if (stream != connection) return;
                    WriteFrame(connection, PongOpcode, frame.Payload);
                }
            }
        }

        private void TrySendActivityLocked()
        {
            if (!ready || stream == null || desiredActivity == null || desiredActivity == sentActivity) return;
            try
            {
                WriteFrame(stream, FrameOpcode, ActivityCommand(desiredActivity));
                sentActivity = desiredActivity;
            }
            catch
            {
                CloseLocked();
                reconnect.Set();
            }
        }

        private string ActivityCommand(string activity) =>
            "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + processId +
            ",\"activity\":" + (activity ?? "null") + "},\"nonce\":\"" +
            Guid.NewGuid().ToString("N") + "\"}";

        private static FileStream Connect()
        {
            for (int index = 0; index < 10; index++)
            {
                SafeFileHandle handle = CreateFile(
                    @"\\.\pipe\discord-ipc-" + index,
                    GenericRead | GenericWrite,
                    0,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);
                if (!handle.IsInvalid) return new FileStream(handle, FileAccess.ReadWrite, 4096, false);
                handle.Dispose();
            }
            return null;
        }

        private void CloseLocked()
        {
            FileStream current = stream;
            stream = null;
            ready = false;
            sentActivity = null;
            try { current?.Dispose(); }
            catch { }
        }

        private static Frame ReadFrame(Stream source)
        {
            byte[] header = ReadExactly(source, 8);
            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > MaximumPayloadBytes)
                throw new InvalidDataException("Invalid Discord IPC frame.");
            return new Frame(opcode, ReadExactly(source, length));
        }

        private static byte[] ReadExactly(Stream source, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = source.Read(buffer, offset, length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return buffer;
        }

        private static void WriteFrame(Stream destination, int opcode, string payload) =>
            WriteFrame(destination, opcode, Encoding.UTF8.GetBytes(payload));

        private static void WriteFrame(Stream destination, int opcode, byte[] payload)
        {
            byte[] header = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, header, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 4, 4);
            destination.Write(header, 0, header.Length);
            destination.Write(payload, 0, payload.Length);
            destination.Flush();
        }

        private readonly struct Frame
        {
            public readonly int Opcode;
            public readonly byte[] Payload;
            public Frame(int opcode, byte[] payload) { Opcode = opcode; Payload = payload; }
        }

        [Serializable]
        private sealed class Activity
        {
            public string details;
            public string state;
            public ActivityTimestamps timestamps;
        }

        [Serializable]
        private sealed class ActivityTimestamps
        {
            public long start;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);
    }
}
#endif
