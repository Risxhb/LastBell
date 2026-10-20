using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LastBell.Network.SocketPrototype
{
    public static class FramingChecks
    {
        private static void Available(TcpClient socket, int bytes)
        {
            var timer = Stopwatch.StartNew();
            while (socket.Available < bytes)
            { if (timer.ElapsedMilliseconds > 3000) throw new TimeoutException("test bytes"); Thread.Sleep(1); }
        }
        private static void Reject(Action action, string reason)
        {
            try { action(); }
            catch (InvalidDataException ex) { if (ex.Message == reason) return; throw; }
            throw new Exception("expected " + reason);
        }
        private static void Pair(Action<TcpClient, TcpClient, Channel> check)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                using (var sender = new TcpClient())
                {
                    sender.NoDelay = true;
                    if (!sender.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port).Wait(3000))
                        throw new TimeoutException("test connect");
                    using (var accepted = listener.AcceptTcpClient())
                    using (var receiver = new Channel(accepted)) check(sender, accepted, receiver);
                }
            }
            finally { listener.Stop(); }
        }
        public static string[] Run()
        {
            Pair((sender, accepted, receiver) =>
            {
                byte[] bytes = Channel.Encode(Op.Events, new byte[32768]);
                sender.GetStream().Write(bytes, 0, bytes.Length); Available(accepted, bytes.Length);
                receiver.Pump();
                if (receiver.BytesRead != Channel.MaxPumpBytes) throw new Exception("pump budget");
                Message message;
                if (receiver.TryRead(out message)) throw new Exception("premature frame");
                while (receiver.BytesRead < bytes.Length) receiver.Pump();
                if (!receiver.TryRead(out message) || message.Kind != Op.Events || message.Data.Length != 32768)
                    throw new Exception("budgeted reassembly");
                // A complete frame resets the absolute deadline for the next frame.
                Thread.Sleep(Channel.AssemblyTimeoutMs + 100);
                bytes = Channel.Encode(Op.Ready, new byte[0]);
                sender.GetStream().Write(bytes, 0, bytes.Length); Available(accepted, bytes.Length); receiver.Pump();
                if (!receiver.TryRead(out message) || message.Kind != Op.Ready) throw new Exception("deadline reset");
            });
            Pair((sender, accepted, receiver) =>
            {
                byte[] bytes = Enumerable.Range(0, Channel.MaxQueued + 1).SelectMany(i => Channel.Encode(Op.Ready, new byte[0])).ToArray();
                sender.GetStream().Write(bytes, 0, bytes.Length); Available(accepted, bytes.Length);
                Reject(() => receiver.Pump(), "frame_queue_limit");
            });
            Pair((sender, accepted, receiver) =>
            {
                sender.GetStream().WriteByte(0); Available(accepted, 1); receiver.Pump();
                Thread.Sleep(Channel.PartialTimeoutMs + 100);
                // New data arriving before the next Pump cannot refresh an expired partial header.
                sender.GetStream().WriteByte(0); Available(accepted, 1);
                Reject(() => receiver.Pump(), "frame_timeout");
            });
            Pair((sender, accepted, receiver) =>
            {
                byte[] bytes = { 0, 0, 0, 100, 1 };
                sender.GetStream().Write(bytes, 0, bytes.Length); Available(accepted, bytes.Length); receiver.Pump();
                var timer = Stopwatch.StartNew(); bool rejected = false;
                while (timer.ElapsedMilliseconds < Channel.AssemblyTimeoutMs + 1200)
                {
                    Thread.Sleep(250); sender.GetStream().WriteByte(0); Available(accepted, 1);
                    try { receiver.Pump(); }
                    catch (InvalidDataException ex)
                    { if (ex.Message != "frame_deadline") throw; rejected = true; break; }
                }
                if (!rejected) throw new Exception("slow drip survived absolute deadline");
            });
            return new[] { "pump byte budget and reassembly", "complete frame resets deadline", "coalesced queue cap",
                "late byte cannot revive idle header", "slow drip absolute assembly deadline" };
        }
    }
}
