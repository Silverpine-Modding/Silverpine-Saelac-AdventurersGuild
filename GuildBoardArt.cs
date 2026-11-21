using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AdventurersGuild;

internal sealed class GuildBoardArt
{
    internal static readonly Color Ink = new Color32(52,35,25,255), Faded = new Color32(92,61,39,255),
        Cream = new Color32(249,235,201,255), Brass = new Color32(216,183,118,255),
        Walnut = new Color32(53,37,29,255), Green = new Color32(50,75,61,255);
    internal readonly Sprite Paper, Wood;
    private readonly TMP_FontAsset heading;
    private readonly Dictionary<Material,Material> typeMaterials=new();

    internal GuildBoardArt()
    {
        var letter = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t.name == "letteruianim0010");
        if (letter)
        {
            // The game's unfolded letter occupies only the middle of its animation frame.
            var crop = new Rect(letter.width*.153f,letter.height*.061f,letter.width*.69f,letter.height*.872f);
            Paper = Sprite.Create(letter,crop,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(46,48,46,48));
        }
        var floor = SerializationManager.GetPrefabFromName("prefab_tile_wood");
        if (floor) Wood = floor.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
        heading = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.StartsWith("CinzelDecorative",StringComparison.OrdinalIgnoreCase));
    }
    internal void Dispose()
    {
        if (Paper) Object.Destroy(Paper);
        foreach (var material in typeMaterials.Values) Object.Destroy(material);
    }
    internal static RectTransform Node(Transform parent,string name)
    {
        var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false);
        return rect;
    }
    internal static void Fill(RectTransform rect,float left=0,float top=0,float right=0,float bottom=0)
    {
        rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
        rect.offsetMin=new Vector2(left,bottom); rect.offsetMax=new Vector2(-right,-top);
    }
    internal static void Position(RectTransform rect,float x,float y,float w,float h)
    {
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(w,h);
    }
    internal static Image Paint(RectTransform rect,Color color)
    {
        var image=rect.gameObject.AddComponent<Image>();
        image.color=color; image.raycastTarget=false;
        return image;
    }
    internal static void Size(RectTransform rect,float width=-1,float height=-1,bool grow=false)
    {
        var e = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        e.preferredWidth=width; e.preferredHeight=height; e.flexibleWidth=grow ? 1 : 0;
        if (width >= 0) e.minWidth=width;
        if (height >= 0) e.minHeight=height;
    }
    internal static void Stack(RectTransform rect,bool horizontal=false,int inset=0,float gap=8)
    {
        HorizontalOrVerticalLayoutGroup g=horizontal ? rect.gameObject.AddComponent<HorizontalLayoutGroup>() : rect.gameObject.AddComponent<VerticalLayoutGroup>();
        g.padding=new RectOffset(inset,inset,inset,inset); g.spacing=gap;
        g.childControlWidth=true; g.childControlHeight=true;
        g.childForceExpandWidth=!horizontal; g.childForceExpandHeight=false;
        g.childAlignment=TextAnchor.UpperLeft;
    }
    internal static void Clear(RectTransform rect)
    {
        for(int i=rect.childCount-1;i>=0;i--)
        {
            var child=rect.GetChild(i).gameObject;
            child.SetActive(false); Object.Destroy(child);
        }
    }
    internal TextMeshProUGUI Label(RectTransform parent,string value,float size,Color color,bool title=false)
    {
        var rect=Node(parent,"Text");
        var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (title && heading) label.font=heading;
        else if (title) label.fontStyle=FontStyles.Bold;
        var source=label.fontSharedMaterial;
        if (source)
        {
            if (!typeMaterials.TryGetValue(source,out var ink))
            {
                ink=new Material(source);
                ink.DisableKeyword("UNDERLAY_ON"); ink.DisableKeyword("UNDERLAY_INNER"); ink.DisableKeyword("GLOW_ON");
                if (ink.HasProperty("_OutlineWidth")) ink.SetFloat("_OutlineWidth",0);
                typeMaterials.Add(source,ink);
            }
            label.fontSharedMaterial=ink;
        }
        label.fontSize=size; label.color=color; label.richText=false;
        label.raycastTarget=false; label.text=value; label.alignment=TextAlignmentOptions.MidlineLeft;
        return label;
    }
    internal Button Button(RectTransform parent,string text,Action click,Color color,float height=34)
    {
        var rect=Node(parent,text); Size(rect,height:height);
        var image=Paint(rect,color); image.raycastTarget=true;
        var button=rect.gameObject.AddComponent<Button>(); button.targetGraphic=image;
        var tint=button.colors; tint.highlightedColor=new Color(1.18f,1.13f,1.04f); tint.colorMultiplier=1.02f; button.colors=tint;
        var label=Label(rect,text,17,Cream); Fill(label.rectTransform,8,0,8,0); label.alignment=TextAlignmentOptions.Center;
        button.onClick.AddListener(()=>click());
        return button;
    }
    internal TMP_InputField Input(RectTransform parent,string value,string hint,Action<string> changed)
    {
        var rect=Node(parent,hint); Size(rect,height:38);
        var bg=Paint(rect,new Color32(255,246,220,255)); bg.raycastTarget=true;
        var field=rect.gameObject.AddComponent<TMP_InputField>();
        var viewport=Node(rect,"Text viewport"); Fill(viewport,12,2,12,2);
        viewport.gameObject.AddComponent<RectMask2D>();
        var placeholder=Label(viewport,hint,18,Faded); Fill(placeholder.rectTransform);
        var text=Label(viewport,"",18,Ink); Fill(text.rectTransform); text.enableWordWrapping=false;
        field.targetGraphic=bg; field.textViewport=viewport; field.textComponent=text; field.placeholder=placeholder;
        field.customCaretColor=true; field.caretColor=Ink; field.text=value;
        field.onValueChanged.AddListener(s=>changed(s));
        return field;
    }
    internal RectTransform Sheet(RectTransform parent,string name,int inset=24)
    {
        var paper=Node(parent,name);
        var image=Paint(paper,Paper ? Color.white : new Color32(232,211,168,255));
        if (Paper) { image.sprite=Paper; image.type=Image.Type.Sliced; image.pixelsPerUnitMultiplier=2.5f; }
        Stack(paper,inset:inset,gap:6);
        paper.GetComponent<VerticalLayoutGroup>().padding=new RectOffset(inset+10,inset+10,inset+6,inset);
        return paper;
    }
    internal static (ScrollRect Scroll, RectTransform Content) Scroller(RectTransform parent)
    {
        var scroll=parent.gameObject.AddComponent<ScrollRect>();
        var viewport=Node(parent,"Viewport"); Fill(viewport,right:14);
        Paint(viewport,Color.clear).raycastTarget=true; viewport.gameObject.AddComponent<RectMask2D>();
        var content=Node(viewport,"Entries");
        content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1); content.sizeDelta=Vector2.zero;
        Stack(content,gap:12);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false; scroll.scrollSensitivity=36;
        scroll.movementType=ScrollRect.MovementType.Clamped;
        var rail=Node(parent,"Scroll rail"); rail.anchorMin=new Vector2(1,0); rail.anchorMax=Vector2.one;
        rail.pivot=new Vector2(1,.5f); rail.sizeDelta=new Vector2(8,0);
        var track=Paint(rail,Walnut); track.raycastTarget=true;
        var handle=Node(rail,"Grip"); Fill(handle); var grip=Paint(handle,Brass); grip.raycastTarget=true;
        var bar=rail.gameObject.AddComponent<Scrollbar>(); bar.handleRect=handle; bar.targetGraphic=grip;
        bar.direction=Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar=bar;
        scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        return (scroll,content);
    }
}
