using System;
using System.Collections.Generic;
using System.Text;

namespace LastBell.Run.Networking
{
    // LAN player names: one rule set for the profile card, the host's join check and the .NET harness (Unity-free).
    // 2-16 characters of ASCII letters, digits, spaces, '_' and '-'; spaces are trimmed and runs of spaces collapsed.
    // ASCII only so every name renders in the menu font and look-alike letters can't impersonate another player.
    public static class LanPlayerName
    {
        public const int MinLength = 2, MaxLength = 16;
        // A roster name may carry the host's " (2)".." (4)" duplicate suffix.
        public const int MaxRosterLength = MaxLength + 4;

        public static string Normalize(string raw)
        {
            if (raw == null) return "";
            var sb = new StringBuilder(raw.Length); bool space = false;
            foreach (char c in raw.Trim())
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false; sb.Append(c);
            }
            return sb.ToString();
        }

        static bool Allowed(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-';

        // Null when the (normalized) name is acceptable, else a short message for the player.
        public static string Problem(string raw)
        {
            var name = Normalize(raw);
            if (name.Length < MinLength) return "Use at least " + MinLength + " characters.";
            if (name.Length > MaxLength) return "Use at most " + MaxLength + " characters.";
            foreach (char c in name) if (!Allowed(c)) return "Use letters, numbers, spaces, _ or - only.";
            return null;
        }
        public static bool Valid(string raw) => Problem(raw) == null;

        // A name as the host reports it in the roster: a valid, already-normalized name, optionally with " (n)".
        public static bool ValidRoster(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxRosterLength) return false;
            string root = name;
            if (name.EndsWith(")", StringComparison.Ordinal) && name.Length > 4 && name[name.Length - 4] == ' ' && name[name.Length - 3] == '(')
            {
                char n = name[name.Length - 2];
                if (n < '2' || n > '9') return false;
                root = name.Substring(0, name.Length - 4);
            }
            return Valid(root) && Normalize(root) == root;
        }

        // The host keeps names unique (ignoring case) by adding " (2)", " (3)", ...
        public static string MakeUnique(string name, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(taken ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(name)) return name;
            for (int i = 2; i <= 9; i++) { var candidate = name + " (" + i + ")"; if (!used.Contains(candidate)) return candidate; }
            return name;   // unreachable with at most four players
        }
    }
}
