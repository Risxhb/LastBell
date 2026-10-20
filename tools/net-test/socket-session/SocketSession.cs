using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LastBell.Network.Rules;
using LastBell.Network.EventRecovery;

namespace LastBell.Network.SocketPrototype
{
    public sealed class PeerResult
    {
        public int ClientPid; public ulong Connection; public long PlayerId, AcceptedSequence, ProcessedSequence;
        public bool Disconnected; public string DisconnectReason;
    }
    public sealed class HostReport
    {
        public int Pid, Port, ExpectedRemotes, AcceptedRemotes, MaxRoster;
        public long CompletedTicks, InputAccepted, InputRejected, Commands, GuardCommands, EventsIssued;
        public long BytesRead, BytesWritten, DuplicateCommandTicks, UnknownActorCommands;
        public string ReadyGateReason, PhaseAfterShutdown;
        public PeerResult[] Peers;
        public Dictionary<string, long> Rejections = new Dictionary<string, long>();
        public bool Passed; public string[] Failures;
    }
    public sealed class ClientReport
    {
        public int Pid, Port; public string Mode, EndReason;
        public ulong Connection; public long PlayerId, LastTick, LastProcessed, SentInputs, Counter, FeedbackEffects, DuplicateEffects;
        public long BytesRead, BytesWritten; public bool Passed; public string[] Failures;
        public Dictionary<string, long> Rejections = new Dictionary<string, long>();
    }
    internal sealed class Remote
    {
        public Channel Channel; public PeerResult Result; public bool Joined, Ready, Dead;
        public long AcceptedAt;
    }
    public static class SocketSession
    {
        public const int ActiveTicks = 150, FinalTick = 180;
        private static void Count(Dictionary<string, long> counts, string key)
        { long value; counts.TryGetValue(key, out value); counts[key] = value + 1; }
        private static void Atomic(string path, params string[] lines)
        { File.WriteAllLines(path + ".tmp", lines); File.Move(path + ".tmp", path, true); }
        public static HostReport Host(string directory, int expectedRemotes)
        {
            var report = new HostReport { Pid = Process.GetCurrentProcess().Id, ExpectedRemotes = expectedRemotes };
            var failures = new List<string>(); var all = new List<Remote>();
            var identity = Guid.NewGuid(); var session = new SessionRules(identity, 0);
            var listener = new TcpListener(IPAddress.Loopback, 0); var clock = Stopwatch.StartNew();
            EventSender events = null; long eventCounter = 0, sequence = 0, nextEventSend = 0, startedAt = -1, drainAt = -1;
            bool stopSent = false, barrierPublished = false; ulong nextConnection = 100;
            var commandKeys = new HashSet<string>();
            Action<string> rejectCount = reason => Count(report.Rejections, reason);
            Action<Remote, string> retire = (peer, reason) =>
            {
                if (peer.Dead) return;
                peer.Dead = true; peer.Result.Disconnected = true; peer.Result.DisconnectReason = reason;
                if (peer.Joined) { session.Disconnect(peer.Result.Connection); if (events != null) events.Remove(peer.Result.Connection); }
                peer.Channel.Dispose();
            };
            try
            {
                report.ReadyGateReason = session.Control(0, new ControlRequest(identity, 0, ControlKind.Start), 0).Reason;
                session.Control(0, new ControlRequest(identity, 0, ControlKind.Ready, ready: true), 0);
                listener.Start(16); report.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Atomic(Path.Combine(directory, "endpoint.txt"), report.Port.ToString(), identity.ToString(), report.Pid.ToString());
                while (clock.ElapsedMilliseconds < 60000)
                {
                    int accepts = 0;
                    while (listener.Pending() && accepts++ < 8)
                    {
                        TcpClient socket = listener.AcceptTcpClient();
                        if (all.Count >= 128 || all.Count(p => !p.Dead) >= 16) { socket.Dispose(); rejectCount("connection_limit"); continue; }
                        all.Add(new Remote { Channel = new Channel(socket), AcceptedAt = clock.ElapsedMilliseconds,
                            Result = new PeerResult { Connection = nextConnection++ } });
                    }
                    foreach (Remote peer in all.Where(p => !p.Dead).ToArray())
                    {
                        try
                        {
                            peer.Channel.Pump(); Message message;
                            while (peer.Channel.TryRead(out message) && !peer.Dead)
                            {
                                if (!peer.Joined)
                                {
                                    if (message.Kind != Op.Hello) throw new InvalidDataException("hello_required");
                                    byte version = 0; Guid room = Guid.Empty; int pid = 0;
                                    Payload.Read(message.Data, r => { version = r.ReadByte(); room = new Guid(r.ReadBytes(16)); pid = r.ReadInt32(); });
                                    string reason = version != 1 ? "unsupported_version" : room != identity ? "wrong_room" : pid <= 0 ? "invalid_pid" : "";
                                    if (reason == "")
                                    {
                                        RuleResult joined = session.Join(peer.Result.Connection);
                                        if (!joined.Ok) reason = joined.Reason;
                                        else
                                        {
                                            peer.Joined = true; peer.Result.PlayerId = joined.PlayerId; peer.Result.ClientPid = pid;
                                            report.AcceptedRemotes++; report.MaxRoster = Math.Max(report.MaxRoster, session.Snapshot().Players.Length);
                                        }
                                    }
                                    if (reason != "") { rejectCount(reason); peer.Channel.Send(Op.Reject, Payload.Text(reason)); retire(peer, reason); continue; }
                                    peer.Channel.Send(Op.Welcome, Payload.Write(w => { w.Write(identity.ToByteArray()); w.Write(peer.Result.Connection); w.Write(peer.Result.PlayerId); }));
                                }
                                else if (message.Kind == Op.Ready && message.Data.Length == 0)
                                {
                                    RuleResult ready = session.Control(peer.Result.Connection, new ControlRequest(identity, session.Generation, ControlKind.Ready, ready: true), clock.ElapsedMilliseconds);
                                    if (!ready.Ok) throw new InvalidDataException(ready.Reason); peer.Ready = true;
                                }
                                else if (message.Kind == Op.Input)
                                {
                                    InputRequest input; string reason;
                                    RuleResult accepted = InputCodec.TryDecode(message.Data, out input, out reason)
                                        ? session.SubmitInput(peer.Result.Connection, input, clock.ElapsedMilliseconds) : RuleResult.Reject("malformed_input");
                                    if (!accepted.Ok) { report.InputRejected++; rejectCount(accepted.Reason); peer.Channel.Send(Op.Reject, Payload.Text(accepted.Reason)); }
                                    else { report.InputAccepted++; peer.Result.AcceptedSequence = input.Sequence; }
                                }
                                else if (message.Kind == Op.EventAck && events != null)
                                {
                                    RuleResult ack = events.ReceiveAck(message.Data, peer.Result.Connection);
                                    if (!ack.Ok && ack.Reason != "invalid_event_ack") throw new InvalidDataException("event_ack_" + ack.Reason);
                                }
                                else if (message.Kind == Op.Quit && message.Data.Length == 0) retire(peer, "client_quit");
                                else throw new InvalidDataException("unexpected_message");
                            }
                            if (!peer.Dead && peer.Channel.Closed) retire(peer, "socket_eof");
                            if (!peer.Dead && !peer.Joined && clock.ElapsedMilliseconds - peer.AcceptedAt > 3000)
                                throw new InvalidDataException("hello_timeout");
                        }
                        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is SocketException || ex is ArgumentException)
                        { rejectCount(ex is InvalidDataException ? ex.Message : "socket_error"); retire(peer, ex.Message); }
                    }
                    if (startedAt < 0)
                    {
                        if (report.AcceptedRemotes == expectedRemotes && all.Count(p => p.Joined && !p.Dead && p.Ready) == expectedRemotes)
                        {
                            if (!barrierPublished) { Atomic(Path.Combine(directory, "lobby.txt"), "ready"); barrierPublished = true; }
                            if (File.Exists(Path.Combine(directory, "start.signal")))
                            {
                                RuleResult start = session.Control(0, new ControlRequest(identity, 0, ControlKind.Start), clock.ElapsedMilliseconds);
                                if (!start.Ok) throw new InvalidOperationException("start: " + start.Reason);
                                events = new EventSender(identity, session.Generation);
                                foreach (Remote peer in all.Where(p => p.Joined && !p.Dead))
                                {
                                    events.Register(peer.Result.Connection);
                                    peer.Channel.Send(Op.Start, Payload.Write(w => { w.Write(session.Generation); w.Write(0L); }));
                                }
                                startedAt = clock.ElapsedMilliseconds; Atomic(Path.Combine(directory, "running.txt"), "0");
                            }
                        }
                    }
                    else
                    {
                        long target = Math.Min(FinalTick, (clock.ElapsedMilliseconds - startedAt) * 60 / 1000);
                        int catchup = 0;
                        while (session.Tick < target && catchup++ < 8)
                        {
                            if (session.Tick < ActiveTicks && session.Tick % 3 == 0)
                            {
                                sequence++;
                                var local = new InputRequest(identity, session.Generation, sequence, session.Tick + 1,
                                    new PlanarMove(0.5, 0), true, false, false, sequence % 5 == 0);
                                if (!session.SubmitInput(0, local, clock.ElapsedMilliseconds).Ok) throw new InvalidOperationException("local input");
                            }
                            foreach (var command in session.Advance())
                            {
                                report.Commands++; if (command.Value.Guard) report.GuardCommands++;
                                if (!commandKeys.Add(session.Tick + "/" + command.Key)) report.DuplicateCommandTicks++;
                                if (command.Key != 1 && !all.Any(p => p.Joined && !p.Dead && p.Result.PlayerId == command.Key)) report.UnknownActorCommands++;
                            }
                            if (session.Tick % 10 == 0)
                            { eventCounter++; events.Append(session.Tick, "socket_counter", BitConverter.GetBytes(eventCounter)); }
                            if (session.Tick == ActiveTicks)
                            { foreach (Remote peer in all.Where(p => p.Joined && !p.Dead)) peer.Channel.Send(Op.StopInput, new byte[0]); stopSent = true; }
                            if (session.Tick % 3 == 0)
                            {
                                SessionSnapshot snapshot = session.Snapshot();
                                foreach (Remote peer in all.Where(p => p.Joined && !p.Dead))
                                {
                                    peer.Result.ProcessedSequence = snapshot.ProcessedSequences[peer.Result.PlayerId];
                                    peer.Channel.Send(Op.Tick, Payload.Write(w => { w.Write(snapshot.Tick); w.Write(peer.Result.ProcessedSequence); }));
                                }
                            }
                            if (session.Tick % 15 == 0) Atomic(Path.Combine(directory, "running.txt"), session.Tick.ToString());
                        }
                        if (clock.ElapsedMilliseconds >= nextEventSend)
                        {
                            foreach (Remote peer in all.Where(p => p.Joined && !p.Dead))
                            {
                                byte[] packet = events.BuildBatch(peer.Result.Connection);
                                if (packet == null) packet = events.BuildCheckpoint(peer.Result.Connection, events.LastIssued, session.Tick, BitConverter.GetBytes(eventCounter));
                                peer.Channel.Send(Op.Events, packet, duplicate: session.Tick >= 20 && session.Tick <= 25);
                            }
                            nextEventSend = clock.ElapsedMilliseconds + 50;
                        }
                        if (session.Tick == FinalTick)
                        {
                            if (drainAt < 0) drainAt = clock.ElapsedMilliseconds;
                            bool drained = true;
                            foreach (Remote peer in all.Where(p => p.Joined && !p.Dead))
                            {
                                byte[] packet = events.BuildBatch(peer.Result.Connection); EventFrame frame;
                                if (packet == null || !EventWire.TryDecode(packet, out frame) || frame.Events.Length != 0) drained = false;
                            }
                            if (drained) break;
                            if (clock.ElapsedMilliseconds - drainAt > 3000) throw new TimeoutException("event drain");
                        }
                    }
                    Thread.Sleep(1);
                }
                if (session.Tick != FinalTick || !stopSent) failures.Add("run did not finish");
                foreach (Remote peer in all.Where(p => p.Joined && !p.Dead))
                {
                    if (peer.Result.AcceptedSequence != peer.Result.ProcessedSequence) failures.Add("accepted input not consumed for " + peer.Result.PlayerId);
                    peer.Channel.Send(Op.Shutdown, Payload.Write(w => { w.Write(session.Tick); w.Write(eventCounter); }));
                }
                session.Disconnect(0); report.PhaseAfterShutdown = session.Phase.ToString();
                report.CompletedTicks = session.Tick; report.EventsIssued = eventCounter;
                if (report.ReadyGateReason != "players_not_ready" || report.AcceptedRemotes != expectedRemotes ||
                    report.MaxRoster != expectedRemotes + 1 || report.DuplicateCommandTicks != 0 || report.UnknownActorCommands != 0)
                    failures.Add("session invariants");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                foreach (Remote peer in all)
                { report.BytesRead += peer.Channel.BytesRead; report.BytesWritten += peer.Channel.BytesWritten; peer.Channel.Dispose(); }
                listener.Stop();
            }
            report.Peers = all.Where(p => p.Joined).Select(p => p.Result).ToArray();
            report.Failures = failures.ToArray(); report.Passed = failures.Count == 0; return report;
        }

        public static ClientReport Client(int port, Guid identity, string mode, bool injectChecks)
        {
            var report = new ClientReport { Pid = Process.GetCurrentProcess().Id, Port = port, Mode = mode };
            var failures = new List<string>(); var timer = Stopwatch.StartNew(); Channel channel = null;
            EventReceiver receiver = null; int generation = 0; long sequence = 0, sentForTick = -1;
            bool started = false, stopped = false, injected = false; var seen = new HashSet<long>();
            try
            {
                var socket = new TcpClient();
                if (!socket.ConnectAsync(IPAddress.Loopback, port).Wait(3000)) throw new TimeoutException("connect");
                channel = new Channel(socket);
                if (mode == "bad-length") channel.Raw(new byte[] { 127, 255, 255, 255 });
                else if (mode == "partial-close")
                { channel.Raw(new byte[] { 0, 0, 0, 10, 1 }); report.EndReason = "partial_frame_sent"; }
                else if (mode == "stall") channel.Raw(new byte[] { 0, 0, 0, 10, 1 });
                else channel.Send(Op.Hello, Payload.Write(w => { w.Write((byte)1); w.Write((mode == "wrong-room" ? Guid.Empty : identity).ToByteArray()); w.Write(report.Pid); }), fragment: true);
                while (report.EndReason == null && timer.ElapsedMilliseconds < 45000)
                {
                    channel.Pump(); Message message;
                    while (channel.TryRead(out message) && report.EndReason == null)
                    {
                        if (message.Kind == Op.Welcome)
                        {
                            Payload.Read(message.Data, r => { if (new Guid(r.ReadBytes(16)) != identity) throw new InvalidDataException("room"); report.Connection = r.ReadUInt64(); report.PlayerId = r.ReadInt64(); });
                            channel.Send(Op.Ready, new byte[0]);
                        }
                        else if (message.Kind == Op.Start)
                        {
                            Payload.Read(message.Data, r => { generation = r.ReadInt32(); report.LastTick = r.ReadInt64(); });
                            receiver = new EventReceiver(identity, report.Connection, generation); started = true;
                        }
                        else if (message.Kind == Op.Tick)
                        {
                            long tick = 0, processed = 0; Payload.Read(message.Data, r => { tick = r.ReadInt64(); processed = r.ReadInt64(); });
                            if (tick < report.LastTick || processed < report.LastProcessed) throw new InvalidDataException("tick/ack rollback");
                            report.LastTick = tick; report.LastProcessed = processed;
                            if (mode == "disconnect" && tick >= 60) { channel.Send(Op.Quit, new byte[0]); report.EndReason = "client_disconnect"; break; }
                            if (!stopped && tick != sentForTick)
                            {
                                sequence++; sentForTick = tick;
                                var input = new InputRequest(identity, generation, sequence, tick + 3, new PlanarMove(0.5, 0.5), true, false, false, sequence % 5 == 0);
                                byte[] packet = InputCodec.Encode(input);
                                if (injectChecks && !injected)
                                {
                                    byte[] malformed = (byte[])packet.Clone(); malformed[0] = 255; channel.Send(Op.Input, malformed);
                                    channel.Send(Op.Input, InputCodec.Encode(new InputRequest(Guid.Empty, generation, sequence, tick + 3, new PlanarMove(0, 0), false, false, false, false)));
                                    channel.Send(Op.Input, InputCodec.Encode(new InputRequest(identity, generation - 1, sequence, tick + 3, new PlanarMove(0, 0), false, false, false, false)));
                                    injected = true;
                                }
                                channel.Send(Op.Input, packet, fragment: sequence % 10 == 0, duplicate: sequence == 1);
                                report.SentInputs++;
                            }
                        }
                        else if (message.Kind == Op.Events && started)
                        {
                            RuleResult result = receiver.Receive(message.Data, 0, record =>
                            {
                                if (record.Kind != "socket_counter" || record.Payload.Length != 8) throw new InvalidDataException("event schema");
                                long value = BitConverter.ToInt64(record.Payload, 0);
                                if (value != report.Counter + 1) throw new InvalidDataException("counter gap");
                                if (!seen.Add(record.EventId)) report.DuplicateEffects++;
                                report.Counter = value; report.FeedbackEffects++;
                            }, (state, watermark, tick) =>
                            {
                                if (state.Length != 8 || BitConverter.ToInt64(state, 0) != watermark) return false;
                                report.Counter = watermark; return true;
                            });
                            if (!result.Ok) throw new InvalidDataException("event receive: " + result.Reason);
                            channel.Send(Op.EventAck, receiver.BuildAck());
                        }
                        else if (message.Kind == Op.StopInput) stopped = true;
                        else if (message.Kind == Op.Reject)
                        {
                            string reason = Payload.Text(message.Data); Count(report.Rejections, reason);
                            if (mode == "capacity" || mode == "wrong-room") report.EndReason = reason;
                            else if (reason != "stale_sequence" && reason != "malformed_input" && reason != "wrong_session" && reason != "wrong_generation")
                                throw new InvalidDataException("unexpected rejection: " + reason);
                        }
                        else if (message.Kind == Op.Shutdown)
                        {
                            Payload.Read(message.Data, r => { long tick = r.ReadInt64(), counter = r.ReadInt64(); if (tick != FinalTick || counter != report.Counter) throw new InvalidDataException("shutdown state"); });
                            if (report.LastProcessed != sequence) throw new InvalidDataException("unprocessed final input");
                            report.EndReason = "graceful_host_shutdown";
                        }
                        else throw new InvalidDataException("unexpected server message");
                    }
                    if (channel.Closed && report.EndReason == null) report.EndReason = "socket_eof";
                    Thread.Sleep(1);
                }
                string expected = mode == "capacity" ? "session_full" : mode == "wrong-room" ? "wrong_room" : mode == "disconnect" ? "client_disconnect" :
                    mode == "bad-length" || mode == "stall" || mode == "expect-eof" ? "socket_eof" : mode == "partial-close" ? "partial_frame_sent" : "graceful_host_shutdown";
                if (report.EndReason != expected || report.DuplicateEffects != 0) failures.Add("unexpected end/result: " + report.EndReason + " expected " + expected);
                if ((mode == "play" || mode == "disconnect") && (!started || report.SentInputs == 0 || report.FeedbackEffects == 0)) failures.Add("play not exercised");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally { if (channel != null) { report.BytesRead = channel.BytesRead; report.BytesWritten = channel.BytesWritten; channel.Dispose(); } }
            report.Failures = failures.ToArray(); report.Passed = failures.Count == 0; return report;
        }
    }
}
