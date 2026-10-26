using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: The Stopped Hour checks (ULTIMATE.md §3.2, GUIDE.md). Owned by the Stopped Hour session.
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs (UltStart, BkStep, UltStepAlive, bkHits, UltimateBot).
    public sealed partial class RunAutoTest
    {
        // hazards seen spawning (any Ultimate move) and the tick each resolved on, for "same tick" checks
        readonly List<(Hazard h, string by)> shWatch = new List<(Hazard, string)>();
        readonly Dictionary<Hazard, int> shResolved = new Dictionary<Hazard, int>();
        BellkeeperEncounter shHooked;
        void ShHook()
        {
            shWatch.Clear(); shResolved.Clear();
            if (bk == null || shHooked == bk) return;
            shHooked = bk;
            bk.HazardSpawned += (a, h) => shWatch.Add((h, a.Key));
        }
        // step, noting resolve ticks; alive = keep standing players topped up (timing checks, not survival)
        void ShStep(int ticks, bool alive = false, Action<int> each = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                each?.Invoke(d.World.Tick);
                d.World.Step();
                foreach (var (h, _) in shWatch) if (!shResolved.ContainsKey(h) && !h.Cancelled && h.Age >= h.ResolveAt) shResolved[h] = d.World.Tick;
                if (alive) foreach (var p in d.World.Players) if (!p.Down && p.Hp < PlayerController.MaxHp) p.DebugSetHp(PlayerController.MaxHp);
            }
        }
        IEnumerable<int> ShResolvesOf(string key, int from) => shWatch.Where(w => w.by == key && shResolved.ContainsKey(w.h) && shResolved[w.h] >= from).Select(w => shResolved[w.h]);
        List<UltimateBot> ShBots()
        {
            var list = new List<UltimateBot>(); int n = d.World.Players.Count;
            for (int i = 0; i < n; i++) { var b = UltimateParty.Drive(d.World.Players[i], i, n); b.Speed = 4.2f; b.Attacks = false; list.Add(b); }
            return list;
        }
        // one cast from now to 3 ticks after the resume; partners launched so they resolve with the resume (as the plan does)
        // Hand launch on the shared manual scheduler (UltManual: no plan, launched moves advance every tick). `pre` runs after
        // the restart has cleared statuses, before the launch.
        void ShGo(StoppedHour sh, UltCue cue, Action pre = null)
        {
            UltManual();
            pre?.Invoke();
            bk.UltLaunch(sh, cue);
        }
        // the next cast once the current one has finished, run to 3 ticks after its resume
        void ShNext(StoppedHour sh, UltCue cue)
        {
            for (int i = 0; i < Sim.Ticks(3) && sh.Running; i++) ShStep(1);
            ShStep(Sim.Ticks(2));
            bk.UltLaunch(sh, cue);
            ShStep(Mathf.Max(1, sh.ResumeTick + 3 - d.World.Tick));
        }
        void ShRun(StoppedHour sh, UltCue cue, bool heart = false, bool mirror = false, Action pre = null)
        {
            ShGo(sh, cue, pre);
            int resume = sh.ResumeTick;
            ShStep(Mathf.Max(1, resume + 3 - d.World.Tick), false, t =>
            {
                if (mirror && t == resume - 240) bk.UltMove(bk.UltMirror, BellkeeperEncounter.MirrorTuning());
                if (heart && t == resume - 120) bk.UltMove(bk.UltTide, BellkeeperEncounter.HeartTuning());
            });
        }
        string ShLashes(StoppedHour sh) => string.Join(", ", sh.Lashes.Select(l => $"{UltimateState.StationNames[l.Station]}→{(l.Boss ? "boss" : l.Struck ? l.Struck.name + " " + l.Damage : "-")}"));
        // per Stilled player: station, the formation's spot and where they stood at the lash
        string ShSpots(StoppedHour sh) => string.Join("; ", sh.Lashes.Select(l =>
        {
            var h = sh.HintFor(l.Target);
            return $"{l.Target.name} {UltimateState.StationNames[l.Station]} spot {(h.HasValue ? $"({h.Value.x:0.0},{h.Value.z:0.0})" : "-")} at ({l.TargetAt.x:0.0},{l.TargetAt.z:0.0})";
        })) + (sh.FormationOk ? "" : " · formation incomplete");
        static UltCue ShCue(int phase, string variant) => new UltCue { Phase = phase, Variant = variant, At = new[] { 5f, 9f }, Seed = 7 + phase };

        IEnumerator UltStoppedHourChecks()
        {
            Note("Ultimate: The Stopped Hour (proposal, built)");

            // ---- 1. a bare launch, solo: glyph at launch, Stilled 5.0-9.0 s, chains dashed then coral, lash on the resume tick ----
            yield return UltStart();
            if (bk == null) yield break;
            ShHook();
            var sh = bk.Mechanic<StoppedHour>(); var me = d.Player; var ult = bk.Ult;
            Check("Stopped Hour: built (not the scaffold stub)", sh.Built && sh.Key == "ult_stopped_hour");
            sh.ForceStation = 0;
            BkPlace(me, UltimateState.StationDir(0) * 2.4f);
            ShGo(sh, sh.DefaultCue());
            int launch = sh.LastCastTick, stop = sh.StopTick, resume = sh.ResumeTick;
            var st = ult.Of(me);
            Check("Stopped Hour: solo, you are Stilled every time", sh.StilledPlayers.SequenceEqual(new[] { me }));
            Check("Stopped Hour: the hourglass glyph shows from launch, the stop starts 5.0 s later and lasts through the resume (9.0 s)",
                  st.HourglassFrom == launch && st.StillFrom == launch + Sim.Ticks(5) && resume == launch + Sim.Ticks(9) && st.StillUntil == resume + 1,
                  $"glass {st.HourglassFrom - launch}, still {st.StillFrom - launch}..{st.StillUntil - launch}");
            Check("Stopped Hour: the glyph line names the lash station (LASH NE) and a brass needle points to it", st.Note == "LASH NE" && sh.NeedlesShown == 1, st.Note);
            bot.MoveWorld = Vector3.right;
            ShStep(stop + 10 - d.World.Tick);
            var p0 = me.transform.position;
            ShStep(Sim.Ticks(2));
            Check("Stopped Hour: a Stilled player can't move", Sim.Flat(me.transform.position - p0).magnitude < 0.01f && ult.IsStilled(me), $"{Sim.Flat(me.transform.position - p0).magnitude:0.00} m");
            bot.Clear(); BkPlace(me, UltimateState.StationDir(0) * 2.4f);
            var lane = sh.Spawned.FirstOrDefault(h => h.Source == sh.Name);
            int laneSpawn = lane != null ? d.World.Tick - lane.Age : -1;
            Check("Stopped Hour: the coral chain lane is live for the second half of the stop (dashed ivory before)", lane != null && Mathf.Abs(laneSpawn - (stop + Sim.Ticks(2))) <= 1
                  && lane.Width == StoppedHour.LaneWidth && lane.Kind == HazardKind.Strike && lane.Origin == UltimateState.Station(0), lane == null ? "none" : $"spawn {(laneSpawn - stop) / 60f:0.00}s into the stop");
            ShStep(resume + 3 - d.World.Tick);
            Check("Stopped Hour: the lash resolves on the resume tick; nothing between = 999 to the Stilled player",
                  sh.Lashes.Count == 1 && sh.Lashes[0].Tick == resume && sh.Lashes[0].Struck == me && sh.Lashes[0].Damage == UltimateState.Lethal && me.Down,
                  ShLashes(sh) + $" at {(sh.Lashes.Count > 0 ? sh.Lashes[0].Tick - resume : -99)}");
            Check("Stopped Hour: no wipe and no Cracked Hour from a lash", ult.Wipes == 0 && ult.CrackedCount == 0);

            // ---- 2. still melee and still ranged are lashed from 3 of 4 stations (the 4th is exactly the hiding strip) ----
            foreach (var (label, at) in new[] { ("melee 2.4 m", 2.4f), ("ranged 10 m", 10f) })
            {
                var hitBy = new List<int>();
                for (int s = 0; s < 4; s++)
                {
                    yield return UltStart();
                    if (bk == null) yield break;
                    ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = s;
                    BkPlace(d.Player, UltimateState.StationDir(0) * at);
                    ShRun(sh, sh.DefaultCue());
                    if (sh.Lashes.Any(l => l.Struck == d.Player && l.Damage == UltimateState.Lethal)) hitBy.Add(s);
                }
                Check($"Stopped Hour: a still {label} player (toward NE) is one-shot from NE, SE and NW; hidden only from SW",
                      hitBy.SequenceEqual(new[] { 0, 1, 3 }), "hit from " + string.Join(",", hitBy.Select(i => UltimateState.StationNames[i])));
            }

            // ---- 3. the intended route at 4.2 m/s with finite stamina, solo: from the station-side wall, and hugging the boss ----
            for (int s = 0; s < 4; s++)
                foreach (var (label, at) in new[] { ("wall", 12.4f), ("melee", 2.1f) })
                {
                    yield return UltStart();
                    if (bk == null) yield break;
                    ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = s;
                    BkPlace(d.Player, UltimateState.StationDir(s) * at); bkHits.Clear();
                    var b = ShBots()[0];
                    bool heart = s % 2 == 1;   // odd stations: with a Heart Toll on the resume (P2 rule: hide beyond 4.3 m)
                    ShRun(sh, heart ? ShCue(2, "lash") : sh.DefaultCue(), heart);
                    float dist = Sim.Flat(d.Player.transform.position - bk.transform.position).magnitude;
                    Check($"Stopped Hour route (solo, {UltimateState.StationNames[s]} station, from the {label}{(heart ? ", + Heart Toll" : "")}): hides behind the boss at 4.2 m/s",
                          !d.Player.Down && sh.Lashes.Count == 1 && sh.Lashes[0].Boss && BkHitsOn(d.Player) == 0,
                          $"{ShLashes(sh)} · {dist:0.0} m from the boss · stamina {d.Player.Stamina:0} · {b.DebugLine}");
                }

            // ---- 4. N = 2: one Stilled per stop, alternating; the free player acts ----
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>();
            var mate = BkAdd(new Vector3(-6, 0, 2), "Chimesinger");
            ShBots();
            ShRun(sh, ShCue(0, "alone"));
            var first = sh.StilledPlayers.ToList();
            bool freeActs = first.Count == 1 && !bk.Ult.All.Any(kv => kv.Key != first[0] && kv.Value.StillFrom >= 0);
            ShNext(sh, ShCue(0, "alone"));
            var second = sh.StilledPlayers.ToList();
            Check("Stopped Hour N = 2: one Stilled per stop, alternating; the other player stays free",
                  first.Count == 1 && second.Count == 1 && first[0] != second[0] && freeActs, $"{first.FirstOrDefault()?.name} then {second.FirstOrDefault()?.name}");
            Check("Stopped Hour N = 2: both stops survived by the route (lashes stopped by the boss)",
                  d.World.Players.All(p => !p.Down) && sh.Lashes.Count == 2 && sh.Lashes.All(l => l.Boss), ShLashes(sh));

            // ---- 5. N = 4 and N = 8 (no clocks, T1): half Stilled, opposite stations crowd the strip; the route works ----
            foreach (int n in new[] { 4, 8 })
            {
                yield return UltStart();
                if (bk == null) yield break;
                ShHook(); sh = bk.Mechanic<StoppedHour>(); bkHits.Clear();
                for (int i = 1; i < n; i++) BkAdd(Sim.Dir(360f * i / n + 10) * (i % 2 == 0 ? 9f : 5f), RunDirector.Classes[i % RunDirector.Classes.Length]);
                ShBots();
                ShRun(sh, ShCue(0, "alone"));
                var who = sh.StilledPlayers.ToList(); var stations = who.Select(p => sh.StationFor(p)).ToList();
                bool opposite = stations.Count >= 2 && (stations[1] - stations[0] + 4) % 4 == 2;
                Check($"Stopped Hour N = {n}: {n / 2} Stilled; without clocks the stations come in opposite pairs{(n == 8 ? " (all four)" : "")}",
                      who.Count == n / 2 && opposite && (n < 8 || stations.Distinct().Count() == 4), string.Join(",", stations.Select(i => UltimateState.StationNames[i])));
                Check($"Stopped Hour N = {n} (alone): every Stilled player hides, nobody is struck by a chain",
                      d.World.Players.All(p => !p.Down) && sh.Lashes.Count == n / 2 && sh.Lashes.All(l => l.Boss) && bkHits.Count == 0 && sh.FormationOk,
                      ShLashes(sh) + " · " + ShSpots(sh));
            }

            // ---- 6. the hardest resume: N = 8, four rules on one tick (lash + Reflection images + Heart Toll; P5) ----
            foreach (int n in new[] { 1, 4, 8 })
            {
                yield return UltStart();
                if (bk == null) yield break;
                ShHook(); sh = bk.Mechanic<StoppedHour>(); bkHits.Clear();
                for (int i = 1; i < n; i++) BkAdd(Sim.Dir(360f * i / n + 10) * 7f, RunDirector.Classes[i % RunDirector.Classes.Length]);
                ShBots();
                ShRun(sh, ShCue(5, "lash"), heart: true, mirror: true);
                var stilledNow = sh.StilledPlayers.ToList();
                int chainHits = bkHits.Count(h => h.Source == sh.Name), onStilled = bkHits.Count(h => stilledNow.Contains(h.Player) && h.Source != "Reflection of the Hour");
                int ownImage = bkHits.Count(h => stilledNow.Contains(h.Player) && h.Source == "Reflection of the Hour");
                Check($"Stopped Hour N = {n}, P5 resume (lash + Reflection + Heart Toll): every Stilled player survives the lash and the Heart Toll",
                      stilledNow.All(p => !p.Down) && sh.Lashes.All(l => l.Boss) && chainHits == 0 && onStilled == 0 && sh.FormationOk,
                      $"{ShLashes(sh)} · {ShSpots(sh)} · hits {string.Join(", ", bkHits.Select(h => (stilledNow.Contains(h.Player) ? "*" : "") + h.Player.name + ":" + h.Source + " " + h.Damage))}");
                if (n == 1) Note($"Stopped Hour solo + Reflection resume: the solo own-image lands on the Stilled player ({ownImage} hit(s) of 40); see the report's open question");
                else Check($"Stopped Hour N = {n}, P5 resume: no reflected image lands on a Stilled player, nobody goes down", ownImage == 0 && d.World.Players.All(p => !p.Down),
                           string.Join(", ", bkHits.Select(h => h.Player.name + ":" + h.Source)) + " · players " + string.Join(" ", d.World.Players.Select(p => $"{(stilledNow.Contains(p) ? "*" : "")}{p.name}({p.transform.position.x:0.0},{p.transform.position.z:0.0})")) + " · images " + string.Join(" ", shWatch.Where(w => w.by == "reflection").Select(w => $"({w.h.Origin.x:0.0},{w.h.Origin.z:0.0})")));
            }

            // the Conductor's Hand phrase 4 (Stopped Hour, 2.0 s stop) at N = 8, party clumped on the +Z far wall: every
            // Stilled player gets a spot (all four stations in the Heart Toll band) and the route works
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); bkHits.Clear();
            BkPlace(d.Player, new Vector3(11.8f * Mathf.Sin(-18 * Mathf.Deg2Rad), 0, 11.8f * Mathf.Cos(-18 * Mathf.Deg2Rad)));
            for (int i = 1; i < 8; i++) { float a = (-18 + 36f * i / 7) * Mathf.Deg2Rad; BkAdd(new Vector3(11.8f * Mathf.Sin(a), 0, 11.8f * Mathf.Cos(a)), RunDirector.Classes[i % RunDirector.Classes.Length]); }
            ShBots();
            ShRun(sh, new UltCue { Variant = "lash", Phase = 2, At = new[] { 5f, 7f }, Seed = 3 }, heart: true);
            {
                var hand = sh.StilledPlayers.ToList();
                Check("Stopped Hour N = 8, Conductor's Hand phrase 4 (2.0 s stop, party on the far wall, + Heart Toll): a full formation, every Stilled player hides",
                      hand.Count == 4 && sh.FormationOk && hand.All(p => !p.Down) && sh.Lashes.Count == 4 && sh.Lashes.All(l => l.Boss) && bkHits.Count(h => h.Source == sh.Name) == 0
                      && !bkHits.Any(h => hand.Contains(h.Player) && h.Source == UltShapes.HeartSource),
                      $"{ShLashes(sh)} · {ShSpots(sh)} · hits {string.Join(", ", bkHits.Select(h => (hand.Contains(h.Player) ? "*" : "") + h.Player.name + ":" + h.Source + " " + h.Damage))}");
            }

            // N = 8 with clock hands only on SE and NW (two Stilled on each of two opposite stations), Heart Toll band
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); bkHits.Clear();
            for (int i = 1; i < 8; i++) BkAdd(Sim.Dir(360f * i / 8 + 10) * 7f, RunDirector.Classes[i % RunDirector.Classes.Length]);
            ShBots();
            ShRun(sh, new UltCue { Variant = "lash", Phase = 2, At = new[] { 5f, 9f }, Seed = 5 }, heart: true,
                  pre: () => { for (int i = 0; i < d.World.Players.Count; i++) bk.Ult.SetClock(d.World.Players[i], 1 + i % 4, i % 2 == 0 ? 1 : 3, d.World.Tick + 9999); });
            {
                var two = sh.StilledPlayers.ToList();
                Check("Stopped Hour N = 8, hands only SE / NW (two Stilled per opposite station, + Heart Toll): a full formation, every Stilled player hides",
                      two.Count == 4 && sh.FormationOk && two.All(p => !p.Down) && sh.Lashes.All(l => l.Boss) && bkHits.Count(h => h.Source == sh.Name) == 0,
                      $"{ShLashes(sh)} · {ShSpots(sh)}");
            }

            // P5 with an NW / SE hand: the hiding line lies on the burning fissure, so the spot keeps 1.35 m off it
            foreach (int s in new[] { 1, 3 })
            {
                yield return UltStart();
                if (bk == null) yield break;
                ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = s; bkHits.Clear();
                BkPlace(d.Player, UltimateState.StationDir((s + 1) % 4) * 9f);   // off the fissure (the NW / SE stations lie on it)
                ShBots();
                ShRun(sh, ShCue(5, "lash"), heart: true, mirror: true, pre: () => d.World.Hazards.Spawn(new Hazard   // stand-in for the Fracture (its session builds the real one)
                {
                    Source = "Fissure probe", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = -UltimateState.FissureDir * 15f,
                    Yaw = Quaternion.LookRotation(UltimateState.FissureDir).eulerAngles.y, Length = 30, Width = 2.4f, BurnPerSecond = 30, LockAt = 0, ResolveAt = 99999,   // the flare's 2.4 m, burning the whole stop (stricter than one toll)
                }));
                var hint = sh.HintFor(d.Player) ?? Vector3.zero;
                float off = Mathf.Abs(Vector3.Dot(Sim.Flat(hint), UltimateState.FissureNormal));
                Check($"Stopped Hour P5, {UltimateState.StationNames[s]} hand: hides {off:0.00} m off the burning fissure and survives (no burn while frozen, no lash, no Heart Toll)",
                      !d.Player.Down && off >= StoppedHour.FissureClear && !bkHits.Any(h => h.Player == d.Player && h.Source == "Fissure probe" && h.Tick >= sh.StopTick) && BkHitsOn(d.Player, sh.Name) == 0 && BkHitsOn(d.Player, UltShapes.HeartSource) == 0 && sh.FormationOk,
                      $"{ShLashes(sh)} · {string.Join(", ", bkHits.Select(h => h.Source + " " + h.Damage))}");
            }

            // ---- 7. first body: an ally in the lane takes 60 and saves the Stilled; one before the boss takes 60; one behind it is safe ----
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = 0; mate = BkAdd(new Vector3(-6, 0, 2));
            var s0 = UltimateState.StationDir(0);
            ShGo(sh, ShCue(0, "alone"));
            var still = sh.StilledPlayers.First(); var free = still == d.Player ? mate : d.Player;
            BkPlace(still, s0 * 4f); BkPlace(free, s0 * 7f); bkHits.Clear();   // Stilled unhidden on the station side; the ally covers
            if (free == d.Player) bot.Clear();
            ShStep(sh.ResumeTick + 3 - d.World.Tick);
            Check("Stopped Hour: a free ally standing in the lane takes 60 and saves the unhidden Stilled player",
                  !still.Down && BkHitsOn(still) == 0 && bkHits.Count(h => h.Player == free && h.Source == sh.Name && h.Damage == StoppedHour.AllyDamage) == 1, ShLashes(sh));
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = 0; mate = BkAdd(new Vector3(-6, 0, 2));
            ShGo(sh, ShCue(0, "alone"));
            still = sh.StilledPlayers.First(); free = still == d.Player ? mate : d.Player;
            BkPlace(still, -s0 * 5f); BkPlace(free, s0 * 5f); bkHits.Clear();   // Stilled hidden; a careless ally between station and boss
            ShStep(sh.ResumeTick + 3 - d.World.Tick);
            Check("Stopped Hour: an ally in the chain before the boss takes 60 even when the Stilled player hid", !still.Down && bkHits.Count(h => h.Player == free && h.Damage == StoppedHour.AllyDamage) == 1 && BkHitsOn(still) == 0, ShLashes(sh));
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = 0; mate = BkAdd(new Vector3(-6, 0, 2));
            ShGo(sh, ShCue(0, "alone"));
            still = sh.StilledPlayers.First(); free = still == d.Player ? mate : d.Player;
            BkPlace(still, -s0 * 7f); BkPlace(free, -s0 * 3.5f); bkHits.Clear();   // the ally between the boss and the Stilled player
            ShStep(sh.ResumeTick + 3 - d.World.Tick);
            Check("Stopped Hour: behind the boss the chain is already stopped (nobody hit)", bkHits.Count == 0 && sh.Lashes.Count == 1 && sh.Lashes[0].Boss, ShLashes(sh));
            // the narrow strip: 1.9 m off the line at 3 m behind is outside it (the shared 2.2 m rule would hide it)
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); sh.ForceStation = 0;
            BkPlace(d.Player, -s0 * 3f + Vector3.Cross(Vector3.up, s0) * 1.9f);
            ShRun(sh, sh.DefaultCue());
            Check("Stopped Hour (hardened): the body stops a chain only through its centre line: 1.9 m off the line is lashed", d.Player.Down && sh.Lashes.Count == 1 && !sh.Lashes[0].Boss, ShLashes(sh));

            // ---- 8. the station comes from the clock hand when there is one ----
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>();
            BkPlace(d.Player, -UltimateState.StationDir(2) * 4f);
            ShRun(sh, ShCue(0, "alone"), pre: () => bk.Ult.SetClock(d.Player, 1, 2, d.World.Tick + 900));
            Check("Stopped Hour: the lash comes from the station the clock hand points to (SW)", sh.StationFor(d.Player) == 2 && sh.Spawned.Any(h => h.Origin == UltimateState.Station(2)) && !d.Player.Down, ShLashes(sh));

            // ---- 9. boss death mid-stop cancels the lanes, the presentation and frees the Stilled player ----
            yield return UltStart();
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>();
            ShGo(sh, sh.DefaultCue());
            ShStep(sh.StopTick + Sim.Ticks(2.5f) - d.World.Tick);
            bool lanesLive = d.World.Hazards.Live.Any(h => h.Source == sh.Name), wasStill = !d.Player.MovementEnabled;
            bk.DebugDefeat(); BkStep(2);
            Check("Stopped Hour: boss death cancels the lanes and the needles, and frees the Stilled player",
                  lanesLive && wasStill && d.World.Hazards.Count == 0 && d.Player.MovementEnabled && sh.NeedlesShown == 0 && !sh.Running && sh.Lashes.Count == 0 && !d.Player.Down);

            // ---- 10. the plan: T1 (practice start), P2 (+ Heart Toll), P3 (+ Reflection), P4 (Ledger III), P5 ----
            BellkeeperEncounter.PushTimersOn = false;
            yield return UltStart(null, "T1");
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); ShBots();
            bk.ManualPlan = false; bk.BeginCombat(); ShStep(2);
            int t1 = UltTick0();
            ShStep(Sim.Ticks(22));
            int l0 = UltLaunchTick("ult_stopped_hour", t1, 0), l1 = UltLaunchTick("ult_stopped_hour", t1, 1);
            Check("plan T1 (practice start): two Stopped Hours at 0 and 12 s, lashes at 9 and 21 s, survived by the route",
                  bk.Segment == BellkeeperEncounter.UltSegment.T1 && l0 == t1 && l1 == t1 + Sim.Ticks(12) && sh.Lashes.Select(l => l.Tick).SequenceEqual(new[] { t1 + Sim.Ticks(9), t1 + Sim.Ticks(21) })
                  && sh.Lashes.All(l => l.Boss) && !d.Player.Down, $"launch {(l0 - t1) / 60f:0.00}, {(l1 - t1) / 60f:0.00} · {ShLashes(sh)}");

            foreach (var (seg, launchAt, partner) in new[] { ("P2", 20f, "toll_tide"), ("P3", 18f, "reflection"), ("P5", 18f, "reflection") })
            {
                yield return UltStart(null, seg);
                if (bk == null) yield break;
                ShHook(); sh = bk.Mechanic<StoppedHour>(); ShBots();
                bk.ManualPlan = false; bk.BeginCombat(); ShStep(2);
                int ts = UltTick0();
                ShStep(ts + Sim.Ticks(launchAt + 9.2f) - d.World.Tick, true);
                int la = UltLaunchTick("ult_stopped_hour", ts), lash = sh.Lashes.Count > 0 ? sh.Lashes[0].Tick : -1;
                var partners = ShResolvesOf(partner, la).ToList();
                bool heartToo = seg == "P5" ? ShResolvesOf("toll_tide", la).Contains(lash) : true;
                Check($"plan {seg}: the Stopped Hour launches at {launchAt} s and lashes at {launchAt + 9} s on the same tick as its partner ({partner}{(seg == "P5" ? " + Heart Toll" : "")})",
                      la == ts + Sim.Ticks(launchAt) && lash == la + Sim.Ticks(9) && partners.Contains(lash) && heartToo,
                      $"launch {(la - ts) / 60f:0.00} · lash {(lash - ts) / 60f:0.00} · {partner} {string.Join("/", partners.Select(t => ((t - ts) / 60f).ToString("0.00")))}");
            }
            // solo P3 through the real plan (part5-attempts-3: the perfect solo bot was lashed at the first P3 resume): frozen
            // 23-27 s through clocks III (24) and IV (27), Reflection on the resume; the hiding spot hugs the boss
            // (4.2 m/s without attacks: the escape proof; 6 m/s with attacks, loops 1 and 2: as the bot clears play it)
            foreach (var (speed, attacks, loops) in new[] { (4.2f, false, 1), (6f, true, 2) })
            {
                yield return UltStart(null, "P3");
                if (bk == null) yield break;
                ShHook(); sh = bk.Mechanic<StoppedHour>(); var clocks = bk.Mechanic<PendulumClocks>();
                var sb = UltimateParty.Drive(d.Player, 0, 1); sb.Speed = speed; sb.Attacks = attacks;
                bk.ManualPlan = false; bk.BeginCombat(); ShStep(2);
                int tp = UltTick0();
                for (int loop = 0; loop < loops; loop++)
                {
                    // P3 loops every 31.5 s (28 s + the 3.5 s punish); the resume is 27 s into each
                    ShStep(tp + Sim.Ticks(31.5f * loop + 27.3f) - d.World.Tick, attacks);
                    var frozen = clocks.Strikes.Where(k => k.Owner == d.Player && k.Tick >= sh.StopTick && k.Tick <= sh.ResumeTick).ToList();
                    Check($"plan P3 solo at {speed} m/s{(attacks ? " attacking" : "")}, loop {loop + 1}: survives the resume (lash stopped by the boss), and both clocks struck while frozen reach the boss",
                          sh.Lashes.Count == loop + 1 && sh.Lashes[loop].Boss && !d.Player.Down && frozen.Count == 2 && frozen.All(k => k.Resolved && k.StruckBoss && !k.Cracked) && sh.FormationOk
                          && bk.Segment == BellkeeperEncounter.UltSegment.P3,
                          $"{ShLashes(sh)} · {ShSpots(sh)} · frozen clocks {string.Join(", ", frozen.Select(k => $"{UltimateState.Roman[k.Hour]} {UltimateState.StationNames[k.Station]} {(k.StruckBoss ? "boss" : k.Cracked ? "cracked" : "-")} from ({(k.Cone != null ? k.Cone.Origin.x : 0):0.00},{(k.Cone != null ? k.Cone.Origin.z : 0):0.00})"))} · hp {d.Player.Hp} · {bk.Segment} · {sb.DebugLine}");
                }
                d.Player.Input = bot;
            }
            yield return UltStart(null, "P4");
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); ShBots();
            bk.ManualPlan = false; bk.BeginCombat(); ShStep(2);
            int t4 = UltTick0();
            ShStep(Sim.Ticks(40), true);
            int la4 = UltLaunchTick("ult_stopped_hour", t4), ledger4 = UltLaunchTick("ult_ledger", la4 >= 0 ? la4 : t4);
            Check("plan P4: block 2's Stopped Hour resumes 9.0 s after its launch, the Ledger replay starting 0.5 s after it",
                  la4 > t4 && sh.Lashes.Count > 0 && sh.Lashes[0].Tick == la4 + Sim.Ticks(9) && ledger4 == la4 + Sim.Ticks(0.5f),
                  $"launch {(la4 - t4) / 60f:0.00} · ledger +{(ledger4 - la4) / 60f:0.00}");
            BellkeeperEncounter.PushTimersOn = true;

            // ---- 11. practice hook: -lastbellAbility ult_stopped_hour loops it, survivable by the route ----
            yield return UltStart("ult_stopped_hour");
            if (bk == null) yield break;
            ShHook(); sh = bk.Mechanic<StoppedHour>(); ShBots();
            bk.ManualPlan = false; bk.BeginCombat();
            ShStep(Sim.Ticks(24));
            Check("Stopped Hour practice hook: loops (two casts in 24 s) and the route survives both", bk.PracticeAbility == sh && sh.Casts >= 2 && sh.Lashes.Count >= 2 && sh.Lashes.All(l => l.Boss) && !d.Player.Down,
                  $"casts {sh.Casts} · {ShLashes(sh)}");
            d.Player.Input = bot;
        }
    }
}
