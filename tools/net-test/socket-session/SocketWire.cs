using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace LastBell.Network.SocketPrototype
{
    internal enum Op : byte { Hello = 1, Welcome = 2, Ready = 3, Start = 4, Input = 5,
        Tick = 6, Events = 7, EventAck = 8, Reject = 9, Shutdown = 10, Quit = 11, StopInput = 12 }
    internal sealed class Message
    {
        public readonly Op Kind; public readonly byte[] Data;
        public Message(Op kind, byte[] data) { Kind = kind; Data = data; }
    }
    // TCP is a byte stream: partial headers/bodies and several frames per read are normal.
    internal sealed class Channel : IDisposable
    {
        public const int MaxFrame = 65536, MaxQueued = 32, PartialTimeoutMs = 1000;
        public const int AssemblyTimeoutMs = 2000, MaxPumpBytes = 8192;
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly byte[] header = new byte[4], scratch = new byte[4096];
        private readonly Queue<Message> ready = new Queue<Message>();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private byte[] body;
        private int headerUsed, bodyUsed;
        private long progress, frameStarted;
        public bool Closed { get; private set; }
        public long BytesRead, BytesWritten, FramesRead, FramesWritten;
        public Channel(TcpClient socket)
        {
            client = socket; client.NoDelay = true; client.SendTimeout = 2000;
            stream = client.GetStream(); stream.WriteTimeout = 2000;
        }
        public static byte[] Encode(Op kind, byte[] data)
        {
            int length = checked(data.Length + 1);
            if (length > MaxFrame) throw new InvalidDataException("frame_length");
            var bytes = new byte[length + 4];
            bytes[0] = (byte)(length >> 24); bytes[1] = (byte)(length >> 16);
            bytes[2] = (byte)(length >> 8); bytes[3] = (byte)length; bytes[4] = (byte)kind;
            Array.Copy(data, 0, bytes, 5, data.Length); return bytes;
        }
        public void Send(Op kind, byte[] data, bool fragment = false, bool duplicate = false)
        {
            byte[] bytes = Encode(kind, data);
            if (duplicate)
            {
                var both = new byte[bytes.Length * 2]; Array.Copy(bytes, both, bytes.Length);
                Array.Copy(bytes, 0, both, bytes.Length, bytes.Length); bytes = both;
            }
            if (fragment)
            {
                stream.Write(bytes, 0, 1); System.Threading.Thread.Sleep(1);
                stream.Write(bytes, 1, 2); System.Threading.Thread.Sleep(1);
                stream.Write(bytes, 3, bytes.Length - 3);
            }
            else stream.Write(bytes, 0, bytes.Length);
            BytesWritten += bytes.Length; FramesWritten += duplicate ? 2 : 1;
        }
        public void Raw(byte[] bytes) { stream.Write(bytes, 0, bytes.Length); BytesWritten += bytes.Length; }
        public void Pump()
        {
            // Check before reading: a late byte must not revive an expired frame.
            CheckDeadline();
            int remaining = MaxPumpBytes;
            while (!Closed && client.Available > 0 && remaining > 0)
            {
                int count = stream.Read(scratch, 0, Math.Min(remaining, Math.Min(scratch.Length, client.Available)));
                if (count == 0) { Closed = true; break; }
                remaining -= count;
                BytesRead += count; progress = watch.ElapsedMilliseconds;
                for (int i = 0; i < count; i++)
                {
                    if (body == null)
                    {
                        if (headerUsed == 0) frameStarted = watch.ElapsedMilliseconds;
                        header[headerUsed++] = scratch[i];
                        if (headerUsed != 4) continue;
                        uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
                        if (length < 1 || length > MaxFrame) throw new InvalidDataException("frame_length");
                        body = new byte[(int)length]; bodyUsed = 0; headerUsed = 0;
                    }
                    else
                    {
                        body[bodyUsed++] = scratch[i]; if (bodyUsed != body.Length) continue;
                        if (ready.Count >= MaxQueued) throw new InvalidDataException("frame_queue_limit");
                        var payload = new byte[body.Length - 1]; Array.Copy(body, 1, payload, 0, payload.Length);
                        ready.Enqueue(new Message((Op)body[0], payload)); FramesRead++; body = null; bodyUsed = 0;
                    }
                }
            }
            if (!Closed && client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0) Closed = true;
            if (Closed && (body != null || headerUsed != 0)) throw new InvalidDataException("truncated_frame");
            CheckDeadline();
        }
        private void CheckDeadline()
        {
            if (Closed || (body == null && headerUsed == 0)) return;
            if (watch.ElapsedMilliseconds - frameStarted > AssemblyTimeoutMs)
                throw new InvalidDataException("frame_deadline");
            if (watch.ElapsedMilliseconds - progress > PartialTimeoutMs)
                throw new InvalidDataException("frame_timeout");
        }
        public bool TryRead(out Message message)
        { if (ready.Count == 0) { message = null; return false; } message = ready.Dequeue(); return true; }
        public void Dispose() { stream.Dispose(); client.Dispose(); ready.Clear(); body = null; Closed = true; }
    }
    internal static class Payload
    {
        public static byte[] Write(Action<BinaryWriter> write)
        { using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) { write(writer); return stream.ToArray(); } }
        public static void Read(byte[] bytes, Action<BinaryReader> read)
        {
            using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream))
            { read(reader); if (stream.Position != stream.Length) throw new InvalidDataException("payload_length"); }
        }
        public static byte[] Text(string value) { return Encoding.UTF8.GetBytes(value); }
        public static string Text(byte[] bytes)
        { if (bytes.Length > 64) throw new InvalidDataException("reason_length"); return new UTF8Encoding(false, true).GetString(bytes); }
    }
}
