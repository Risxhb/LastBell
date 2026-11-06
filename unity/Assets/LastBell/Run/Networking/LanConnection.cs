using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LastBell.Run.Networking
{
    // Message boundaries and connection lifecycle are independent of the underlying transport. A Relay adapter can
    // implement this contract without exposing sockets to the gameplay protocol. Messages are opaque, not game state.
    public interface IConnection : IDisposable
    {
        ConnectionState State { get; }
        string Error { get; }
        bool Send(byte[] message);
        bool TryReceive(out byte[] message);
    }

    public enum ConnectionState { Connecting, Connected, Closed, Failed }

    // First LAN increment: one remote peer over TCP, with bounded length-prefixed messages. Public methods may be
    // called from the Unity thread; the worker does blocking socket operations and never calls Unity APIs.
    public sealed class LanConnection : IConnection
    {
        public const int MaxMessageBytes = 65536;
        const int MaxQueuedMessages = 256;
        // Received messages wait for the main thread. A client sends input at 60 Hz, so the receive side must ride out
        // multi-second stalls (scene loads on a busy machine) while staying bounded in memory.
        const int MaxReceivedMessages = 1024;
        const long MaxReceivedBytes = 8L << 20;
        readonly object gate = new object();
        readonly ConcurrentQueue<byte[]> incoming = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
        readonly AutoResetEvent sendReady = new AutoResetEvent(false);
        TcpListener listener;
        TcpClient client;
        ConnectionState state = ConnectionState.Connecting;
        string error;
        int queued, sendQueued;
        long queuedBytes;
        bool wasConnected;

        LanConnection() { }
        // True once the socket reached the remote end, even if it was closed before the caller saw Connected.
        public bool WasConnected { get { lock (gate) return wasConnected; } }
        public ConnectionState State { get { lock (gate) return state; } }
        public string Error { get { lock (gate) return error; } }

        public static LanConnection Host(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            var connection = new LanConnection();
            // Bind before returning so callers immediately learn about an unavailable port.
            connection.listener = new TcpListener(IPAddress.Any, port);
            connection.listener.Start(1);
            connection.StartWorker(() => connection.Accept());
            return connection;
        }

        public static LanConnection Connect(string address, int port)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Host address is required.", nameof(address));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            var connection = new LanConnection();
            connection.client = new TcpClient();
            connection.StartWorker(() => { connection.client.Connect(address, port); connection.Run(connection.client); });
            return connection;
        }

        internal static LanConnection FromAccepted(TcpClient socket)
        {
            var connection = new LanConnection { client = socket };
            connection.StartWorker(() => connection.Run(socket));
            return connection;
        }

        void StartWorker(ThreadStart action)
        {
            var worker = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { Fail(ex.Message); }
            });
            worker.IsBackground = true;
            worker.Name = "Last Bell LAN";
            worker.Start();
        }

        void Accept()
        {
            var accepted = listener.AcceptTcpClient();
            lock (gate)
            {
                if (state != ConnectionState.Connecting) { accepted.Close(); return; }
                client = accepted;
                listener.Stop(); listener = null;
            }
            Run(accepted);
        }

        void Run(TcpClient socket)
        {
            socket.NoDelay = true;
            lock (gate)
            {
                if (state != ConnectionState.Connecting) return;
                state = ConnectionState.Connected; wasConnected = true;
            }
            var stream = socket.GetStream();
            StartWorker(() => WriteLoop(stream));
            var header = new byte[4];
            while (true)
            {
                ReadExactly(stream, header, 4);
                int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                if (length < 1 || length > MaxMessageBytes) throw new InvalidDataException("Invalid LAN message length.");
                var message = new byte[length];
                ReadExactly(stream, message, length);
                long bytes = Interlocked.Add(ref queuedBytes, length);
                if (Interlocked.Increment(ref queued) > MaxReceivedMessages || bytes > MaxReceivedBytes)
                {
                    Interlocked.Decrement(ref queued); Interlocked.Add(ref queuedBytes, -length);
                    throw new InvalidDataException("LAN receive queue is full.");
                }
                incoming.Enqueue(message);
            }
        }

        static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int n = stream.Read(buffer, offset, count - offset);
                if (n == 0) throw new EndOfStreamException("Peer disconnected.");
                offset += n;
            }
        }

        public bool Send(byte[] message)
        {
            if (message == null || message.Length < 1 || message.Length > MaxMessageBytes) return false;
            lock (gate)
            {
                if (state != ConnectionState.Connected || sendQueued >= MaxQueuedMessages) return false;
                // The caller can reuse its buffer immediately; acceptance is not a delivery acknowledgement.
                var copy = (byte[])message.Clone();
                outgoing.Enqueue(copy);
                Interlocked.Increment(ref sendQueued);
                sendReady.Set();
                return true;
            }
        }

        void WriteLoop(Stream stream)
        {
            while (true)
            {
                sendReady.WaitOne();
                while (outgoing.TryDequeue(out var message))
                {
                    Interlocked.Decrement(ref sendQueued);
                    if (State != ConnectionState.Connected) return;
                    int n = message.Length;
                    var header = new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
                    stream.Write(header, 0, header.Length);
                    stream.Write(message, 0, n);
                }
                if (State != ConnectionState.Connected) return;
            }
        }

        public bool TryReceive(out byte[] message)
        {
            if (!incoming.TryDequeue(out message)) return false;
            Interlocked.Decrement(ref queued); Interlocked.Add(ref queuedBytes, -message.Length);
            return true;
        }

        void Fail(string reason)
        {
            lock (gate)
            {
                if (state == ConnectionState.Closed || state == ConnectionState.Failed) return;
                error = reason; state = ConnectionState.Failed;
                sendReady.Set();
                listener?.Stop(); listener = null;
                client?.Close(); client = null;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (state == ConnectionState.Closed) return;
                state = ConnectionState.Closed;
                sendReady.Set();
                listener?.Stop(); listener = null;
                client?.Close(); client = null;
            }
        }
    }

    // A session owns admitted peers and enforces its roster limit. The pending-accept queue is bounded independently.
    public sealed class LanListener : IDisposable
    {
        const int MaxPending = LanLobby.MaxClients;
        readonly object gate = new object();
        readonly ConcurrentQueue<LanConnection> pending = new ConcurrentQueue<LanConnection>();
        readonly TcpListener listener;
        bool closed;
        int pendingCount;
        string error;
        public string Error { get { lock (gate) return error; } }

        public LanListener(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(MaxPending);
            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Last Bell LAN listener" };
            thread.Start();
        }

        void AcceptLoop()
        {
            try
            {
                while (true)
                {
                    var socket = listener.AcceptTcpClient();
                    lock (gate)
                    {
                        if (closed || pendingCount >= MaxPending) { socket.Close(); if (closed) return; continue; }
                        pending.Enqueue(LanConnection.FromAccepted(socket));
                        pendingCount++;
                    }
                }
            }
            catch (Exception ex) { lock (gate) { if (!closed) error = ex.Message; } }
        }

        public bool TryAccept(out LanConnection connection)
        {
            lock (gate)
            {
                if (closed || !pending.TryDequeue(out connection)) { connection = null; return false; }
                pendingCount--;
                return true;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                listener.Stop();
                while (pending.TryDequeue(out var connection)) connection.Dispose();
                pendingCount = 0;
            }
        }
    }
}
