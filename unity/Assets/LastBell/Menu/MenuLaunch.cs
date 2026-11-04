using System;
using LastBell.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // Bound to one destination and consumed before gameplay begins. Retry never reuses a pending request.
    public static class MenuLaunch
    {
        sealed class Request { public string Hero,Scene;public Difficulty Difficulty;public bool Test;public int Encounter,Phase=1;public int[] Order; }
        static Request pending;
        public static bool Pending=>pending!=null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear(){pending=null;}
        public static bool Normal(string hero)
        {
            if(pending!=null || Array.IndexOf(RunDirector.Classes,hero)<0 || RunDirector.RunOrder.Length==0)return false;
            var order=RunDirector.CreateRunOrder();int encounter=order[0];
            int map=RunMaps.IndexOf(RunDirector.Bosses[encounter].Map??"test");
            if(!RunMaps.InBuild(map))return false;
            pending=new Request{Hero=hero,Scene=RunMaps.All[map].RunScene,Difficulty=RunDifficulty.Selected,Order=order};
            MenuReturnBridge.Ensure();MenuReturnBridge.TestSession=false;SceneManager.LoadSceneAsync(pending.Scene);return true;
        }
        public static bool Test(string hero,int map,int encounter,int phase=1)
        {
            if(pending!=null||Array.IndexOf(RunDirector.Classes,hero)<0||TestRushSetup.Incompatibility(map,encounter)!=null)return false;
            pending=new Request{Hero=hero,Scene=RunMaps.All[map].RunScene,Difficulty=RunDifficulty.Selected,Test=true,Encounter=encounter,Phase=phase};
            MenuReturnBridge.Ensure();MenuReturnBridge.TestSession=true;SceneManager.LoadSceneAsync(pending.Scene);return true;
        }
        public static bool Consume(RunDirector director)
        {
            if(pending==null)return false;var request=pending;pending=null;
            if(SceneManager.GetActiveScene().path!=request.Scene)return false;
            RunDifficulty.Selected=request.Difficulty;
            return request.Test?director.StartPracticeOnSelectedMap(request.Hero,request.Encounter,request.Phase):director.StartRun(request.Hero,request.Order);
        }
    }
}
