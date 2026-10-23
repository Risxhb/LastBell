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
        public static MenuAppearancePanel Create(Transform parent,Action onBack)
        {
            var overlay=MenuUI.Panel("UI settings",parent,Vector2.zero,Vector2.one,new Color(.012f,.016f,.032f,.98f));overlay.raycastTarget=true;
            var page=overlay.gameObject.AddComponent<MenuAppearancePanel>();page.back=onBack;
            var panel=MenuUI.Rect("UI settings controls",overlay.transform,new Vector2(.25f,.18f),new Vector2(.75f,.82f));
            MenuUI.Text("UI",panel,new Vector2(0,.83f),Vector2.one,34,MenuUI.Gold);
            MenuUI.Text("MAIN MENU",panel,new Vector2(0,.70f),new Vector2(1,.80f),20,MenuUI.Gold);
            page.ShowcaseButton=MenuUI.Button("Original Showcase",panel,new Vector2(.08f,.51f),new Vector2(.92f,.65f),()=>MenuAppearance.Switch(MenuStyle.Showcase));
            page.AlternativeButton=MenuUI.Button("Alternative Confrontation",panel,new Vector2(.08f,.32f),new Vector2(.92f,.46f),()=>MenuAppearance.Switch(MenuStyle.Alternative));
            page.current=MenuUI.Text("",panel,new Vector2(0,.20f),new Vector2(1,.28f),17,MenuUI.Gold);
            MenuUI.Text("Your choice is saved and used whenever you return to the menu.",panel,new Vector2(.02f,.10f),new Vector2(.98f,.20f),16);
            page.BackButton=MenuUI.Button("Back",panel,new Vector2(.25f,-.08f),new Vector2(.75f,.06f),page.Close);
            Selectable[] order={page.ShowcaseButton,page.AlternativeButton,page.BackButton};
            for(int i=0;i<order.Length;i++)order[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=order[(i+2)%3],selectOnDown=order[(i+1)%3]};
            overlay.gameObject.SetActive(false);return page;
        }
        public void Open()
        {
            gameObject.SetActive(true);
            ShowcaseButton.interactable=MenuAppearance.Available(MenuStyle.Showcase);AlternativeButton.interactable=MenuAppearance.Available(MenuStyle.Alternative);
            current.text="Active: "+(MenuAppearance.Selected==MenuStyle.Alternative?"Alternative Confrontation":"Original Showcase");
            EventSystem.current.SetSelectedGameObject((MenuAppearance.Selected==MenuStyle.Alternative?AlternativeButton:ShowcaseButton).gameObject);
        }
        public void Hide()=>gameObject.SetActive(false);
        public void Close(){Hide();back?.Invoke();}
    }
}
