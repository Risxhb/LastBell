using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Shared UI settings page. Switching loads one scene and keeps the persistent music owner.
    public sealed class MenuAppearancePanel : MonoBehaviour
    {
        public Button ShowcaseButton {get;private set;}
        public Button AlternativeButton {get;private set;}
        public Button BackButton {get;private set;}
        public bool IsOpen=>gameObject.activeSelf;
        Action back;
        Text current;
        GameObject showcaseActive,alternativeActive;
        public static MenuAppearancePanel Create(Transform parent,Action onBack)
        {
            var overlay=MenuUI.Panel("UI settings",parent,Vector2.zero,Vector2.one,new Color(.012f,.016f,.032f,.94f));overlay.raycastTarget=true;
            var page=overlay.gameObject.AddComponent<MenuAppearancePanel>();page.back=onBack;
            // Framed modal: Ink fill, GoldDim edge, gold corner brackets and a 3 px gold header rule. Fixed 760x600 at 1080p.
            var frame=MenuUI.FramedPanel("UI settings frame",overlay.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),UiTheme.InkDeep);
            frame.rectTransform.sizeDelta=new Vector2(760,600);
            var rule=MenuUI.Panel("Header rule",frame.transform,new Vector2(0,1),new Vector2(1,1),UiTheme.Gold);rule.rectTransform.pivot=new Vector2(.5f,1);rule.rectTransform.sizeDelta=new Vector2(0,3);
            var panel=MenuUI.Rect("UI settings controls",frame.transform,Vector2.zero,Vector2.one,new Vector2(UiTheme.G3,UiTheme.G3));
            // One Display title and one eyebrow (the old extra "UI" eyebrow duplicated the title).
            MenuUI.Text(UiTheme.Spaced("Interface"),panel,new Vector2(0,.80f),new Vector2(1,.94f),UiTheme.SizeDisplay,UiTheme.Ivory);
            var divider=MenuUI.Panel("Title rule",panel,new Vector2(.5f,.79f),new Vector2(.5f,.79f),UiTheme.GoldDim);divider.rectTransform.sizeDelta=new Vector2(120,1);
            MenuUI.Text(UiTheme.Spaced("Main menu"),panel,new Vector2(0,.68f),new Vector2(1,.74f),UiTheme.SizeLabel,UiTheme.GoldLabel);
            page.ShowcaseButton=Card("Original Showcase",panel,new Vector2(.04f,.49f),new Vector2(.96f,.66f),()=>MenuAppearance.Switch(MenuStyle.Showcase),out page.showcaseActive);
            page.AlternativeButton=Card("Alternative Confrontation",panel,new Vector2(.04f,.31f),new Vector2(.96f,.48f),()=>MenuAppearance.Switch(MenuStyle.Alternative),out page.alternativeActive);
            page.current=MenuUI.Text("",panel,new Vector2(0,.22f),new Vector2(1,.30f),UiTheme.SizeBody,MenuUI.Gold);
            MenuUI.Text("Your choice is saved and used whenever you return to the menu.",panel,new Vector2(.02f,.15f),new Vector2(.98f,.22f),15,UiTheme.IvoryMuted);
            page.BackButton=MenuUI.Button("Back",panel,new Vector2(.3f,0),new Vector2(.7f,.13f),page.Close);
            Selectable[] order={page.ShowcaseButton,page.AlternativeButton,page.BackButton};
            for(int i=0;i<order.Length;i++)order[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=order[(i+2)%3],selectOnDown=order[(i+1)%3]};
            overlay.gameObject.SetActive(false);return page;
        }
        // Option card: a house button (label stays its first Text child) with gold corner brackets. The active choice
        // shows a drawn diamond on the right plus a 1 px GoldBright edge (edge-only stroke, never Outline), so it never
        // relies on colour alone; focus keeps the house left diamond + underline, so the two states stay distinct.
        static Button Card(string label,RectTransform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out GameObject active)
        {
            var b=MenuUI.Button(label,parent,min,max,action);
            MenuUI.Ornaments(b.transform,8,2,2);
            active=MenuUI.Rect("Active choice",b.transform,Vector2.zero,Vector2.one).gameObject;
            MenuUI.Stroke(active.transform,UiTheme.GoldBright,1,0);
            var mark=MenuUI.Rect("Active diamond",active.transform,new Vector2(1,.5f),new Vector2(1,.5f)).gameObject.AddComponent<FocusDiamond>();
            mark.rectTransform.sizeDelta=new Vector2(12,18);mark.rectTransform.anchoredPosition=new Vector2(-24,0);mark.color=UiTheme.Gold;mark.raycastTarget=false;
            active.SetActive(false);return b;
        }
        public void Open()
        {
            gameObject.SetActive(true);
            ShowcaseButton.interactable=MenuAppearance.Available(MenuStyle.Showcase);AlternativeButton.interactable=MenuAppearance.Available(MenuStyle.Alternative);
            bool alternative=MenuAppearance.Selected==MenuStyle.Alternative;
            showcaseActive.SetActive(!alternative);alternativeActive.SetActive(alternative);
            current.text="Active: "+(alternative?"Alternative Confrontation":"Original Showcase");
            EventSystem.current.SetSelectedGameObject((alternative?AlternativeButton:ShowcaseButton).gameObject);
        }
        public void Hide()=>gameObject.SetActive(false);
        public void Close(){Hide();back?.Invoke();}
    }
}
