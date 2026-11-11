using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace LastBell.Menu
{
    // Shared code-built uGUI kit. Visual tokens come from LastBell.UiTheme so menus and the combat HUD match.
    // The main-menu language (Row, Chooser, Header, SideGradient, NavFocus) is described in docs/ui/ui-kit.md.
    internal static class MenuUI
    {
        public static readonly Color Gold=UiTheme.Gold, Ivory=UiTheme.Ivory, Ink=UiTheme.Ink;

        // ---------------------------------------------------------------- main-menu language
        static Sprite stripSprite, sideSprite;
        public static Sprite StripSprite=>stripSprite?stripSprite:stripSprite=Sprite.Create(UiTheme.StripTexture,new UnityEngine.Rect(0,0,128,1),Vector2.one*.5f);
        public static Sprite SideGradientSprite=>sideSprite?sideSprite:sideSprite=Sprite.Create(UiTheme.SideGradient,new UnityEngine.Rect(0,0,128,1),Vector2.one*.5f);
        // Soft dark gradient over the live scene, clear at min.x and near-opaque from ~43 % across (the main menu's right side).
        public static Image SideGradient(Transform parent,Vector2 min,Vector2 max,string name="Right contrast gradient")
        {var g=Panel(name,parent,min,max,Color.white);g.sprite=SideGradientSprite;return g;}
        // Highlight strip behind a control (first child, so the label/glyph draws over it): Gold .26 band fading at both ends,
        // 1 px Gold .55 edges, 28 px past each side. Hidden until NavFocus shows it.
        public static Image Strip(Transform parent)
        {
            var s=Panel("Highlight strip",parent,Vector2.zero,Vector2.one,UiTheme.StripFill);
            s.sprite=StripSprite;s.rectTransform.offsetMin=new Vector2(-28,2);s.rectTransform.offsetMax=new Vector2(28,-2);s.transform.SetAsFirstSibling();
            var top=Panel("Strip edge",s.transform,new Vector2(0,1),new Vector2(1,1),UiTheme.StripEdge);top.sprite=StripSprite;top.rectTransform.pivot=new Vector2(.5f,1);top.rectTransform.sizeDelta=new Vector2(0,1);
            var bottom=Panel("Strip edge",s.transform,new Vector2(0,0),new Vector2(1,0),UiTheme.StripEdge);bottom.sprite=StripSprite;bottom.rectTransform.pivot=new Vector2(.5f,0);bottom.rectTransform.sizeDelta=new Vector2(0,1);
            s.enabled=false;top.enabled=bottom.enabled=false;return s;
        }
        public static readonly Vector2 RowInset=new Vector2(9,7);
        // Text row (the main menu's only button language): transparent hit area, SemiBold label in RestLabel (the first Text
        // child; tests read it) and the highlight strip on focus or hover. The main action on a screen uses 28 px, others 24.
        // inset: hit-area inset inside min..max (the main menu uses RowInset; Boss Rush's footer uses zero).
        public static Button Row(string label,Transform parent,Vector2 min,Vector2 max,UnityAction action,int size=24,Vector2? inset=null)
        {
            var r=Rect(label,parent,min,max,inset??RowInset);var im=r.gameObject.AddComponent<Image>();im.color=new Color(0,0,0,0);im.raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;Tint(b);
            var t=Text(label,r,Vector2.zero,Vector2.one,size);t.font=UiTheme.SemiBold;
            t.resizeTextForBestFit=true;t.resizeTextMinSize=13;t.resizeTextMaxSize=size;
            var focus=r.gameObject.AddComponent<NavFocus>();focus.Label=t;focus.Strip=Strip(r);
            b.onClick.AddListener(action);return b;
        }
        // Drawn chevron button (no glyphs, so it shows in WebGL): Ivory at rest, GoldBright on focus, no strip. Callers place it.
        public static Button ChevronButton(Transform parent,bool next,UnityAction action)
        {
            var b=Row(next?"Next class":"Previous class",parent,new Vector2(0,.63f),new Vector2(1,.72f),action);
            var focus=b.GetComponent<NavFocus>();Object.DestroyImmediate(focus.Strip.gameObject);focus.Strip=null;
            b.GetComponentInChildren<Text>().text="";focus.Label=null;
            var icon=Rect("Class arrow",b.transform,Vector2.zero,Vector2.one,new Vector2(8,8)).gameObject.AddComponent<ClassSelectionArrow>();
            icon.Next=next;icon.color=Ivory;icon.raycastTarget=false;focus.Icon=icon;
            return b;
        }
        // Small non-interactive chevron centred on the parent's middle (a cycling row's cue); callers position it.
        public static RectTransform Chevron(Transform parent,bool next,float size,Color color)
        {
            var a=Rect(next?"Cycle next":"Cycle previous",parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f)).gameObject.AddComponent<ClassSelectionArrow>();
            a.Next=next;a.color=color;a.raycastTarget=false;a.rectTransform.sizeDelta=new Vector2(size,size);return a.rectTransform;
        }
        // Chooser: a value label between two drawn chevron buttons that hug the rendered text (call Place every frame).
        public static Chooser Chooser(Transform parent,string value,Vector2 min,Vector2 max,int size,UnityAction previous,UnityAction next,float hitSize=44)
        {
            var c=new Chooser{Label=Text(value,parent,min,max,size,UiTheme.Ivory),Anchor=new Vector2(.5f,(min.y+max.y)*.5f)};
            c.Previous=ChevronButton(parent,false,previous);FitBox(c.Previous,new Vector2(min.x+.09f,c.Anchor.y),hitSize);
            c.Next=ChevronButton(parent,true,next);FitBox(c.Next,new Vector2(max.x-.09f,c.Anchor.y),hitSize);
            return c;
        }
        // Fixed square hit box (canvas units) centred on an anchor point.
        public static void FitBox(Selectable b,Vector2 at,float size){var r=(RectTransform)b.transform;r.anchorMin=r.anchorMax=at;r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(size,size);r.anchoredPosition=Vector2.zero;}
        // Header: spaced Gold eyebrow, Ivory title, 96x2 Gold rule under the title. Exact rects (the main menu's screens differ);
        // the box overload lays out in Settings' proportions for new screens.
        public static Header Header(Transform parent,string eyebrow,string title,Vector2 eyebrowMin,Vector2 eyebrowMax,Vector2 titleMin,Vector2 titleMax,Vector2 ruleAt,int titleSize=52,int titleMinSize=28,int eyebrowSize=14,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            var h=new Header();
            h.Eyebrow=Text(UiTheme.Spaced(eyebrow),parent,eyebrowMin,eyebrowMax,eyebrowSize,Gold,alignment);
            h.Title=Text(title,parent,titleMin,titleMax,titleSize,UiTheme.Ivory,alignment);h.Title.resizeTextForBestFit=true;h.Title.resizeTextMinSize=titleMinSize;h.Title.resizeTextMaxSize=titleSize;
            h.Rule=Panel("Title rule",parent,ruleAt,ruleAt,Gold);
            float px=alignment==TextAnchor.MiddleCenter||alignment==TextAnchor.UpperCenter||alignment==TextAnchor.LowerCenter?.5f:0;
            h.Rule.rectTransform.pivot=new Vector2(px,.5f);h.Rule.rectTransform.sizeDelta=new Vector2(UiTheme.RuleWidth,UiTheme.RuleHeight);
            return h;
        }
        public static Header Header(Transform parent,string eyebrow,string title,Vector2 min,Vector2 max,int titleSize=52,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            float H=max.y-min.y;float Y(float f)=>min.y+H*f;bool centre=alignment==TextAnchor.MiddleCenter;
            return Header(parent,eyebrow,title,new Vector2(min.x,Y(.72f)),new Vector2(max.x,Y(1)),new Vector2(min.x,Y(.07f)),new Vector2(max.x,Y(.72f)),new Vector2(centre?(min.x+max.x)*.5f:min.x,min.y),titleSize,Mathf.Min(28,titleSize),14,alignment);
        }
        // Distance from a label's centre to its rendered left edge (canvas units), allowing for best-fit shrink.
        public static float HalfWidth(Text label)
        {
            // fontSizeUsedForBestFit is in screen pixels, so remove the canvas scale before comparing.
            float scale=label.canvas?label.canvas.scaleFactor:1;
            float fit=label.resizeTextForBestFit?label.cachedTextGenerator.fontSizeUsedForBestFit/Mathf.Max(1f,label.fontSize*scale):1;
            return Mathf.Min(label.preferredWidth*Mathf.Clamp01(fit>0?fit:1),label.rectTransform.rect.width)*.5f;
        }
        // Wrap-around keyboard/controller navigation down a column (and optional left/right links per item).
        public static void LinkColumn(Selectable[] items,System.Func<int,Selectable> left=null,System.Func<int,Selectable> right=null)
        {
            for(int i=0;i<items.Length;i++)items[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnDown=items[(i+1)%items.Length],selectOnUp=items[(i+items.Length-1)%items.Length],selectOnLeft=left?.Invoke(i),selectOnRight=right?.Invoke(i)};
        }
        // Explicit grid navigation (rows of different lengths): up/down picks the proportionally nearest item. No wrap.
        public static void LinkGrid(params Selectable[][] rows)
        {
            for(int r=0;r<rows.Length;r++)for(int i=0;i<rows[r].Length;i++)
            {
                var row=rows[r];float frac=row.Length>1?i/(float)(row.Length-1):.5f;
                Selectable Near(int k)=>k<0||k>=rows.Length?null:rows[k][Mathf.RoundToInt(frac*(rows[k].Length-1))];
                row[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=i>0?row[i-1]:null,selectOnRight=i<row.Length-1?row[i+1]:null,selectOnUp=Near(r-1),selectOnDown=Near(r+1)};
            }
        }

        // ---------------------------------------------------------------- base helpers
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

    // A chooser's parts (MenuUI.Chooser): the value label and the two chevron buttons that hug it.
    public sealed class Chooser
    {
        public Text Label; public Button Previous, Next; public Vector2 Anchor;
        // Chevrons sit half the rendered label width + gap from the centre, kept inside the parent column. Call every frame
        // (the label's text and the canvas size change).
        public void Place(float gap=32,float margin=22)
        {
            var parent=Label?Label.rectTransform.parent as RectTransform:null;if(!parent||!Previous||!Next)return;
            float limit=Mathf.Max(margin,parent.rect.width*.5f-margin);
            float x=Mathf.Clamp(Label.preferredWidth*.5f+gap,margin,limit);
            foreach(var b in new[]{Previous,Next})
            {
                var r=(RectTransform)b.transform;r.anchorMin=r.anchorMax=Anchor;
                r.anchoredPosition=new Vector2(b==Next?x:-x,0);
            }
        }
    }
    // A header's parts (MenuUI.Header).
    public sealed class Header
    {
        public Text Eyebrow, Title; public Image Rule;
        public void SetActive(bool on){if(Eyebrow)Eyebrow.gameObject.SetActive(on);if(Title)Title.gameObject.SetActive(on);if(Rule)Rule.gameObject.SetActive(on);}
    }

    // The one focus cue of the main-menu language (replaces ConfrontationNavFocus and TestRushFocus).
    //  Row:  text row / icon button. Focus or hover shows the highlight strip and turns the label/icon GoldBright; a
    //        disabled row's label drops to 35 % alpha.
    //  Card: picture card. Chosen = GoldBright frame + gold accent + Gold label; focus = GoldDim frame, brighter art,
    //        Ivory label, a slight grow; rest = faint frame, darkened art, muted label.
    //  Tab:  text tab. Same states as Card with an underline as the frame and a diamond as the chosen accent.
    public sealed class NavFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public enum Kind { Row, Card, Tab }
        public Kind Mode = Kind.Row;
        // Row
        public Text Label; public Graphic Icon; public Image Strip;
        public Color Rest = UiTheme.RestLabel;
        public Color IconRest = UiTheme.Ivory;
        // Card / Tab
        public Graphic Frame, Art, Accent, CardLabel;
        public bool Chosen; public float Grow = 1;
        public Color ChosenEdge = UiTheme.Gold, FocusEdge = new Color(UiTheme.GoldDim.r, UiTheme.GoldDim.g, UiTheme.GoldDim.b, .6f);
        Selectable owner;
        bool selected, hovered, live;
        public bool Focused => hovered || selected || EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject;
        void Awake() { owner = GetComponent<Selectable>(); }
        void OnEnable() { if (Mode == Kind.Row) Refresh(); }
        void Start() { if (Mode == Kind.Row) Refresh(); }
        void OnDisable() { hovered = false; }
        public void OnSelect(BaseEventData data) { selected = true; if (Mode == Kind.Row) Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = false; if (Mode == Kind.Row) Refresh(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; if (Mode == Kind.Row) Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; if (Mode == Kind.Row) Refresh(); }
        void LateUpdate()
        {
            if (Mode == Kind.Row) { bool l = !owner || owner.IsInteractable(); if (l != live) Refresh(); return; }
            bool interactable = !owner || owner.interactable; bool on = Focused && interactable;
            if (Frame) Frame.color = Chosen ? ChosenEdge : on ? FocusEdge : UiTheme.RuleFaint;
            if (Art) Art.color = Chosen ? Color.white : on ? new Color(.85f, .85f, .88f) : new Color(.42f, .42f, .48f);
            if (CardLabel) CardLabel.color = Chosen ? UiTheme.Gold : on ? UiTheme.Ivory : UiTheme.RestLabelMuted;
            if (Accent) Accent.enabled = Chosen;
            float target = on ? Grow : 1; var s = transform.localScale.x; s = Mathf.MoveTowards(s, target, Time.unscaledDeltaTime * .6f); transform.localScale = new Vector3(s, s, 1);
        }
        void Refresh()
        {
            live = !owner || owner.IsInteractable(); bool on = (selected || hovered) && live;
            if (Label) Label.color = !live ? new Color(Rest.r, Rest.g, Rest.b, .35f) : on ? UiTheme.GoldBright : Rest;
            if (Strip) { Strip.enabled = on; foreach (var e in Strip.GetComponentsInChildren<Image>(true)) if (e != Strip) e.enabled = on; }
            if (Icon) Icon.color = on ? UiTheme.GoldBright : IconRest;
        }
    }

    // Focus cue for the older boxed house button (MenuUI.Button: now only the LAN lobby; the Showcase menu UI and
    // Hero Selection were removed in the UI clean-up). The main-menu language uses NavFocus; this goes when the lobby
    // moves to text rows.
    // A drawn diamond and underline plus warm fill / bright edge, so focus never
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
                if (Fill) Fill.color = !live ? UiTheme.DisabledFill : on ? UiTheme.GoldBright : UiTheme.Gold;
                if (Edge) Edge.effectColor = on ? UiTheme.Ivory : UiTheme.GoldBright;
                if (Stroke) Stroke.color = on ? UiTheme.Ivory : UiTheme.GoldBright;
                if (Label) Label.color = live ? UiTheme.PrimaryInk : UiTheme.DisabledLabel;
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
