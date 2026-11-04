using System;
using System.IO;
using LastBell.Run.Networking;
using UnityEngine;

namespace LastBell.Menu
{
    // The LAN player's name, saved on this computer in persistentDataPath/lan-profile.json. LastBell.exe
    // -lastbellProfile <name> uses that name for one run without saving it (several test players on one PC).
    // Names follow LanPlayerName (the host checks the same rules).
    public static class LanProfile
    {
        [Serializable] sealed class Data { public int version = 1; public string name; }
        public const string FileName = "lan-profile.json";
        // Tests point this at their own folder so they never touch the player's real profile.
        public static string PathOverride;
        public static string FilePath => PathOverride ?? Path.Combine(Application.persistentDataPath, FileName);

        static string Override()
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-lastbellProfile");
            if (i < 0 || i + 1 >= args.Length) return null;
            if (LanPlayerName.Valid(args[i + 1])) return LanPlayerName.Normalize(args[i + 1]);
            Debug.LogWarning("[LAN] -lastbellProfile ignored: " + LanPlayerName.Problem(args[i + 1]));
            return null;
        }
        public static bool Overridden => Override() != null;

        // The name to play as, or null when the player hasn't chosen one yet (or the file is unreadable).
        public static string Current => Override() ?? Saved;
        public static string Saved
        {
            get
            {
                try
                {
                    if (!File.Exists(FilePath)) return null;
                    var data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                    if (data != null && LanPlayerName.Valid(data.name)) return LanPlayerName.Normalize(data.name);
                    Debug.LogWarning("[LAN] profile has no valid name; asking again: " + FilePath);
                }
                catch (Exception ex) { Debug.LogWarning("[LAN] profile unreadable; asking again: " + ex.Message); }
                return null;
            }
        }

        // Saves a valid name. Returns null on success, else the reason. Writes a temporary file first, then swaps it in,
        // so a crash mid-write can't leave half a profile.
        public static string Save(string raw)
        {
            var problem = LanPlayerName.Problem(raw); if (problem != null) return problem;
            try
            {
                var path = FilePath; Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(new Data { name = LanPlayerName.Normalize(raw) }, true));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                return null;
            }
            catch (Exception ex) { Debug.LogWarning("[LAN] could not save profile: " + ex.Message); return "Could not save the name: " + ex.Message; }
        }
    }
}
