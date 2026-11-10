using System.Collections;
using UnityEngine;

namespace LastBell.Run
{
    // Before/after screenshots of the latest graphics pass, now the 10 Nov marker pass (dial plates, beacon posts, the rule
    // line, the scalloped debt ring, the INVERTED ring and tongues):
    //   LastBell.exe -lastbellAutotest -lastbellBellkeeperUltimate -lastbellTestAbility ult_shots -lastbellOut <folder>
    // (windowed, not -batchmode: batch mode renders nothing). Each moment is shot twice from the same seeded fight, first with
    // UltLook.OldMarks (the Prim markers), then as built. (The 8 Nov pass's own before/after used UltLook.Old: its Prim spheres
    // and bars, no clips.) A party of 4 Perfect bots plays from each phase's practice start. "close" moments also shoot a
    // low view over the run's own player, where the markers can be read.
    public sealed partial class RunAutoTest
    {
        static readonly (string seg, float at, string what)[] UltShotMoments =
        {
            ("P1", 4.0f, "p1_plates_spot_counterweight_close"),
            ("P1", 10.4f, "p1_relay_pendulumcharge_crosses_chain"),
            ("P1", 12.3f, "p1_chimedebts_shatteredchime"),
            ("P1", 13.0f, "p1_debt_ring_flash_close"),
            ("P2", 5.3f, "p2_slam_bellsweep_chains"),
            ("P2", 8.0f, "p2_inverted_halfline_spot_close"),
            ("P2", 12.0f, "p2_inverted_meet_close"),
            ("P3", 6.5f, "p3_mainspring_bells_echotell"),
            ("P3", 14.0f, "p3_rewind_hollowtoll"),
            ("P3", 21.2f, "p3_bellcurtain_bellsweep"),
            ("P4", 1.6f, "p4_floorbreaks_shatteredchime"),
            ("P4", 21.0f, "p4_hammer_bellsweep"),
            ("P5", 3.0f, "p5_toll_hollowtoll"),
            ("P5", 30.3f, "p5_echo_echotell"),
        };

        IEnumerator UltShots()
        {
            if (Application.isBatchMode) { Note("ult_shots: skipped in batch mode (nothing renders)"); yield break; }
            foreach (bool old in new[] { true, false })
            {
                UltLook.OldMarks = old;
                string look = old ? "before" : "after";
                foreach (var seg in new[] { "P1", "P2", "P3", "P4", "P5" })
                {
                    yield return UltStart(null, seg);
                    if (bk == null) continue;
                    var bots = UltimateParty.Fill(d.World, 4, UltimateBot.Skill.Perfect, PartyClasses);
                    foreach (var b in bots) b.Speed = PlayerController.WalkSpeed;
                    // a raised three-quarter view: the boss's pose and the floor markers in one frame
                    var game = Camera.main; Camera cam = null;
                    if (game)
                    {
                        cam = new GameObject("ult_shots camera").AddComponent<Camera>();
                        cam.CopyFrom(game); cam.tag = "MainCamera"; cam.fieldOfView = 50;
                        cam.transform.SetPositionAndRotation(new Vector3(0, 15f, -14f), Quaternion.LookRotation(new Vector3(0, -13.5f, 14f)));
                        game.enabled = false;
                    }
                    bk.BeginCombat(); d.SetPaused(false);
                    Time.captureFramerate = 60;   // one frame = one tick of game time, so both looks shoot the same moment
                    int t0 = d.World.Tick;
                    foreach (var m in UltShotMoments)
                    {
                        if (m.seg != seg) continue;
                        while (d.World.Tick - t0 < Sim.Ticks(m.at) && !bk.Dead && d.World.AnyStanding) yield return null;
                        // a close moment about a short-lived mark waits (up to 20 s) for one to be up, and the low view looks at it
                        string mark = m.what.Contains("debt_ring") ? "Chime Debt (stand-in)" : m.what.Contains("inverted") ? "INVERTED mark" : null;
                        GameObject focus = null;
                        if (mark != null) for (int w = 0; w < Sim.Ticks(20f) && !(focus = GameObject.Find(mark)) && !bk.Dead && d.World.AnyStanding; w++) yield return null;
                        yield return Shot($"{m.what}_{look}");   // not paused: the pause menu would cover the boss (a frame is one tick here)
                        if (cam && m.what.EndsWith("_close") && d.Player)
                        {
                            var at = cam.transform.position; var rot = cam.transform.rotation; var me = Sim.Flat(focus ? focus.transform.position : d.Player.transform.position);
                            cam.transform.SetPositionAndRotation(me + new Vector3(0, 6.5f, -5.5f), Quaternion.LookRotation(new Vector3(0, -6.5f, 5.5f)));
                            yield return Shot($"{m.what}_low_{look}");
                            cam.transform.SetPositionAndRotation(at, rot);
                        }
                    }
                    Time.captureFramerate = 0;
                    d.SetPaused(true);
                    if (cam) Destroy(cam.gameObject);
                    if (game) game.enabled = true;
                }
            }
            UltLook.OldMarks = false;
            Note("ult_shots: before/after pairs written");
        }
    }
}
