using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace LastBell.Menu
{
    internal static class MenuUI
    {
        public static readonly Color Gold=new Color(.88f,.72f,.43f), Ivory=new Color(.96f,.92f,.82f), Ink=new Color(.025f,.035f,.065f,.97f);
        public static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 inset=default)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false);
            r.anchorMin=min;r.anchorMax=max;r.offsetMin=inset;r.offsetMax=-inset;return r;
        }
        public static Canvas Canvas(Transform parent)
        {
            var go=new GameObject("Selection UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(parent,false);
            var c=go.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;
            var s=go.GetComponent<CanvasScaler>();s.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;s.referenceResolution=new Vector2(1920,1080);s.matchWidthOrHeight=1;
            if(!EventSystem.current){var e=new GameObject("Selection navigation",typeof(EventSystem),typeof(InputSystemUIInputModule));e.transform.SetParent(parent,false);e.AddComponent<MenuInput>().Bind(e.GetComponent<InputSystemUIInputModule>());}
            return c;
        }
        public static Image Panel(string name,Transform parent,Vector2 min,Vector2 max,Color? color=null)
        {var r=Rect(name,parent,min,max);var im=r.gameObject.AddComponent<Image>();im.color=color??Ink;im.raycastTarget=false;return im;}
        public static Text Text(string value,Transform parent,Vector2 min,Vector2 max,int size=24,Color? color=null,TextAnchor alignment=TextAnchor.MiddleCenter)
        {var r=Rect(value,parent,min,max,new Vector2(10,3));var t=r.gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.text=value;t.fontSize=size;t.color=color??Ivory;t.alignment=alignment;t.raycastTarget=false;return t;}
        public static Button Button(string name,Transform parent,Vector2 min,Vector2 max,UnityAction action)
        {
            var r=Rect(name,parent,min,max,new Vector2(9,7));var im=r.gameObject.AddComponent<Image>();im.color=new Color(.16f,.135f,.09f,.97f);im.raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;
            var cs=b.colors;cs.highlightedColor=new Color(1.5f,1.4f,1.2f);cs.selectedColor=new Color(1.7f,1.5f,1.2f);cs.pressedColor=new Color(.65f,.6f,.5f);b.colors=cs;
            var o=r.gameObject.AddComponent<Outline>();o.effectColor=Gold;o.effectDistance=Vector2.one;
            Text(name,r,Vector2.zero,Vector2.one,23);
            var marker=Text("◆",r,new Vector2(0,0),new Vector2(.08f,1),16,Gold);r.gameObject.AddComponent<MenuFocus>().Marker=marker.gameObject;
            b.onClick.AddListener(action);return b;
        }
    }
}
