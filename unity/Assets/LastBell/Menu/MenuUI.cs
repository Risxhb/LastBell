using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace LastBell.Menu
{
    // Shared code-built uGUI kit. Visual tokens come from LastBell.UiTheme so menus and the combat HUD match.
    internal static class MenuUI
    {
        public static readonly Color Gold=UiTheme.Gold, Ivory=UiTheme.Ivory, Ink=new Color(.025f,.035f,.065f,.97f);
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
        // Inter SemiBold from 20 px up (headings/display), Regular below (body/labels). A soft drop shadow keeps text legible over scenes.
        public static Text Text(string value,Transform parent,Vector2 min,Vector2 max,int size=24,Color? color=null,TextAnchor alignment=TextAnchor.MiddleCenter)
        {
            var r=Rect(value,parent,min,max,new Vector2(10,3));var t=r.gameObject.AddComponent<Text>();
            Style(t,size);t.text=value;t.fontSize=size;t.color=color??Ivory;t.alignment=alignment;t.raycastTarget=false;return t;
        }
        public static void Style(Text t,int size){t.font=size>=20?UiTheme.SemiBold:UiTheme.Regular;t.fontStyle=FontStyle.Normal;t.lineSpacing=1.08f;Shadow(t,.8f);}
        public static Shadow Shadow(Graphic g,float alpha)
        {var s=g.GetComponent<Shadow>();if(!s)s=g.gameObject.AddComponent<Shadow>();s.effectColor=new Color(0,0,0,alpha);s.effectDistance=new Vector2(1.5f,-1.5f);return s;}
        // Framed panel: fill, 1 px GoldDim border, inset dark line, gold L-bracket corners with diamonds.
        public static Image FramedPanel(string name,Transform parent,Vector2 min,Vector2 max,Color? fill=null,bool ornaments=true)
        {
            var p=Panel(name,parent,min,max,fill??UiTheme.Ink);
            Stroke(p.transform,UiTheme.GoldDim,1,0);Stroke(p.transform,new Color(0,0,0,.5f),1,3);
            if(ornaments)Ornaments(p.transform);
            return p;
        }
        public static void Ornaments(Transform panel,float len=10,float thick=2,float offset=2)
        {
            for(int i=0;i<4;i++)
            {
                var a=new Vector2(i%2,i/2);var sign=new Vector2(a.x>0?-1:1,a.y>0?-1:1);
                var corner=Rect("Corner",panel,a,a);corner.sizeDelta=Vector2.zero;corner.anchoredPosition=-sign*offset;
                var h=Panel("Bracket",corner,Vector2.zero,Vector2.zero,UiTheme.Gold);h.rectTransform.pivot=new Vector2(a.x,a.y);h.rectTransform.sizeDelta=new Vector2(len,thick);
                var v=Panel("Bracket",corner,Vector2.zero,Vector2.zero,UiTheme.Gold);v.rectTransform.pivot=new Vector2(a.x,a.y);v.rectTransform.sizeDelta=new Vector2(thick,len);
                var d=Rect("Corner diamond",corner,Vector2.zero,Vector2.zero).gameObject.AddComponent<FocusDiamond>();d.rectTransform.sizeDelta=new Vector2(6,6);d.color=UiTheme.Gold;d.raycastTarget=false;
            }
        }
        // House button. Rest: Ink fill, GoldDim edge, ivory label. Focus/hover: warm fill, GoldBright edge, diamond and underline.
        // The label is the first Text child (tests read it); marker and underline are drawn graphics, not glyphs.
        public static Button Button(string name,Transform parent,Vector2 min,Vector2 max,UnityAction action)
        {
            var r=Rect(name,parent,min,max,new Vector2(9,7));var im=r.gameObject.AddComponent<Image>();im.color=new Color(UiTheme.Ink.r,UiTheme.Ink.g,UiTheme.Ink.b,.85f);im.raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;Tint(b);
            var o=Stroke(r,UiTheme.GoldDim,1,0);
            var label=Text(name,r,Vector2.zero,Vector2.one,22);
            label.resizeTextForBestFit=true;label.resizeTextMinSize=13;label.resizeTextMaxSize=22;
            var focus=r.gameObject.AddComponent<MenuFocus>();focus.Fill=im;focus.Stroke=o;focus.Label=label;
            var marker=Rect("Focus diamond",r,new Vector2(0,.5f),new Vector2(0,.5f)).gameObject.AddComponent<FocusDiamond>();
            marker.rectTransform.sizeDelta=new Vector2(10,16);marker.rectTransform.anchoredPosition=new Vector2(18,0);marker.color=UiTheme.GoldBright;marker.raycastTarget=false;focus.Marker=marker.gameObject;
            var line=Panel("Focus underline",r,new Vector2(.3f,0),new Vector2(.7f,0),UiTheme.Gold);line.rectTransform.pivot=new Vector2(.5f,0);line.rectTransform.sizeDelta=new Vector2(0,2);line.rectTransform.anchoredPosition=new Vector2(0,5);focus.Underline=line.gameObject;
            b.onClick.AddListener(action);return b;
        }
        // Edge-only border: four quads, so translucent fills never pick up a gold cast (uGUI Outline redraws the whole quad).
        public static StrokeGraphic Stroke(Transform parent,Color color,float thickness=1,float inset=0)
        {
            var g=Rect("Stroke",parent,Vector2.zero,Vector2.one,new Vector2(inset,inset)).gameObject.AddComponent<StrokeGraphic>();
            g.Thickness=thickness;g.color=color;g.raycastTarget=false;return g;
        }
        // Colour tints stay at or below 1: the old >1 multipliers turned the brown base olive.
        public static void Tint(Selectable s)
        {
            var cs=s.colors;cs.normalColor=Color.white;cs.highlightedColor=Color.white;cs.selectedColor=Color.white;
            cs.pressedColor=new Color(.72f,.70f,.66f);cs.disabledColor=new Color(.55f,.55f,.55f,.6f);cs.colorMultiplier=1;cs.fadeDuration=.08f;s.colors=cs;
        }
    }

    // Focus cue shared by code-built menus: a drawn diamond and underline plus warm fill / bright edge, so focus never
    // relies on colour alone. Optional parts may be null (older callers only set Marker).
    public sealed class MenuFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject Marker, Underline;
        public Image Fill; public Outline Edge; public Graphic Stroke; public Text Label;
        public bool Primary;
        Selectable owner;
        bool selected, hovered;
        void Awake() { owner = GetComponent<Selectable>(); }
        void Start() { Refresh(); }
        void OnDisable() { hovered = false; }
        public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = false; Refresh(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
        bool live;
        void LateUpdate() { bool l = !owner || owner.IsInteractable(); if (l != live) Refresh(); }
        void Refresh()
        {
            live = !owner || owner.IsInteractable();
            bool on = (selected || hovered) && live;
            if (Marker) Marker.SetActive(on);
            if (Underline) Underline.SetActive(on);
            if (Primary)
            {
                if (Fill) Fill.color = !live ? new Color(.22f, .2f, .17f, .9f) : on ? UiTheme.GoldBright : UiTheme.Gold;
                if (Edge) Edge.effectColor = on ? UiTheme.Ivory : UiTheme.GoldBright;
                if (Stroke) Stroke.color = on ? UiTheme.Ivory : UiTheme.GoldBright;
                if (Label) Label.color = live ? new Color(.07f, .05f, .02f) : new Color(.6f, .57f, .5f);
                return;
            }
            if (Fill) Fill.color = on ? UiTheme.FocusFill : new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, .85f);
            if (Edge) Edge.effectColor = on ? UiTheme.GoldBright : live ? UiTheme.GoldDim : new Color(0, 0, 0, 0);
            if (Stroke) Stroke.color = on ? UiTheme.GoldBright : live ? UiTheme.GoldDim : new Color(0, 0, 0, 0);
            if (Label) Label.color = !live ? new Color(UiTheme.Ivory.r, UiTheme.Ivory.g, UiTheme.Ivory.b, .35f) : on ? UiTheme.Ivory : new Color(UiTheme.Ivory.r, UiTheme.Ivory.g, UiTheme.Ivory.b, .82f);
        }
    }

    public sealed class StrokeGraphic : MaskableGraphic
    {
        public float Thickness = 1;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = GetPixelAdjustedRect(); float t = Mathf.Min(Thickness, r.width / 2, r.height / 2);
            Quad(vh, r.xMin, r.yMax - t, r.xMax, r.yMax); Quad(vh, r.xMin, r.yMin, r.xMax, r.yMin + t);
            Quad(vh, r.xMin, r.yMin + t, r.xMin + t, r.yMax - t); Quad(vh, r.xMax - t, r.yMin + t, r.xMax, r.yMax - t);
        }
        void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector2(x0, y0), color, Vector2.zero); vh.AddVert(new Vector2(x0, y1), color, Vector2.zero);
            vh.AddVert(new Vector2(x1, y1), color, Vector2.zero); vh.AddVert(new Vector2(x1, y0), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
