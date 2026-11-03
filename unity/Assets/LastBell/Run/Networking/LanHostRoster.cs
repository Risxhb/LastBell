using System;
using System.Collections.Generic;

namespace LastBell.Run.Networking
{
    // Frozen roster and remote input sources for the host's existing CombatWorld. The session owner pumps and owns
    // the lobby, and must clear this context on return/disconnect. No client-side simulation is started here.
    public sealed class LanHostRoster : IDisposable
    {
        public static LanHostRoster Active { get; private set; }
        readonly LanLobby lobby;
        readonly Dictionary<int, LanRemoteInput> inputs = new Dictionary<int, LanRemoteInput>();
        readonly List<LanLobby.Member> remote = new List<LanLobby.Member>();
        readonly Dictionary<int, int> lastInput = new Dictionary<int, int>();   // newest input sequence received per player
        public int LastInput(int id) => lastInput.TryGetValue(id, out int s) ? s : -1;
        public IReadOnlyList<LanLobby.Member> Remote => remote.AsReadOnly();
        public string Error { get; private set; }
        public LanHostRoster(LanLobby lobby)
        {
            if (Active != null || lobby == null || !lobby.IsHost || !lobby.FightStarted || lobby.Error != null) throw new InvalidOperationException("A started host lobby is required.");
            this.lobby = lobby;
            foreach (var member in lobby.Members)
            {
                if (member.Id == 0) continue;
                remote.Add(new LanLobby.Member { Id = member.Id, ClassName = member.ClassName });
                inputs.Add(member.Id, new LanRemoteInput());
            }
            remote.Sort((a, b) => a.Id.CompareTo(b.Id));
            lobby.InputReceived += OnInput;
            Active = this;
        }
        void OnInput(int id, int sequence, byte[] payload)
        {
            if (!inputs.TryGetValue(id, out var input) || !input.Accept(payload)) { Error = "A player sent invalid input."; return; }
            if (!lastInput.TryGetValue(id, out int last) || sequence > last) lastInput[id] = sequence;
        }
        public IInputSource InputFor(int id) => inputs.TryGetValue(id, out var input) ? input : null;
        public void Dispose()
        {
            lobby.InputReceived -= OnInput;
            if (Active == this) Active = null;
        }
    }
}
