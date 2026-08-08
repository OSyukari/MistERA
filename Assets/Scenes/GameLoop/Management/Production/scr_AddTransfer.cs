using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_AddTransfer : scr_Menu, IPointerClickHandler
{
    public Manageable sourceFaction;
    public Manageable targetFaction;
    public RectTransform recipeList;
    public scr_addTrade prefab_trade;

    public scr_HoverableText title;

    public scr_SelectableText btn_factionLeft, btn_factionRight;

    List<Manageable> targetFactions = new List<Manageable>();

    public void InitializeWithArgument(Manageable sourceFaction, Action onExit)
    {
        this.onSelfExit = onExit;
        if (!initialized) Initialize();

        this.sourceFaction = sourceFaction;

        targetFactions.Clear();
        foreach (var f in scr_System_CampaignManager.current.Player.FactionManager.ManagerFactions)
        {
            if (f != null && f != sourceFaction && !targetFactions.Contains(f)) targetFactions.Add(f);
        }
        targetFaction = targetFactions.Count > 0 ? targetFactions[0] : null;
        if (targetFaction != null)
        {
            title.SetText(LocalizeDictionary.QueryThenParse("ui_management_addTransfer_title")
                .Replace("$name$", targetFaction.FactionDisplayName));
        }

        BuildRecipeList();
        ValidateAll();
    }

    // item list depends only on sourceFaction, so it's built once per open; switching the
    // target faction just updates targetFaction and re-validates (see Button_SelectTrade.IsButtonValid).
    private void BuildRecipeList()
    {
        Utility.DestroyAllChildrenFrom(recipeList);

        var counted = new Dictionary<string, int>();
        foreach (var item in sourceFaction.Inventory.Contents)
        {
            if (item.isToken) continue;
            if (!counted.ContainsKey(item.BaseID)) counted.Add(item.BaseID, 0);
            counted[item.BaseID] += item.Count;
        }

        foreach (var kvp in counted)
        {
            if (kvp.Value <= 0) continue;
            MakeRecipeButton(new ItemEntry(kvp.Key, "", 1, false));
        }
    }

    public void SetTargetFaction(Manageable f)
    {
        this.targetFaction = f;
        title.SetText(LocalizeDictionary.QueryThenParse("ui_management_addTransfer_title")
            .Replace("$name$", f.FactionDisplayName));
        ValidateAll();
    }

    public void NotifyAddTrade(ItemEntry entry, Manageable targetFaction)
    {
        var noCost = new ItemEntry("", "", 0, false);
        sourceFaction.AddTradeOrder(entry, noCost, targetFaction, 0, reversed: true);
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    private void MakeRecipeButton(ItemEntry entry)
    {
        int recipeHash = AssertUniqueHash(entry.itemID.GetHashCode());
        scr_addTrade box = Instantiate(prefab_trade);
        box.itemName.SetText(entry.Print);
        box.itemName.SetExternalTooltip(entry.Tooltip);
        box.pricing.text = "-";
        box.ownedCount.SetText($"{sourceFaction.Inventory.GetItemCount(entry.itemID)}");
        RegisterButton(recipeHash, box.Button, new Button_SelectTrade(this, entry, box));
        box.GetComponent<RectTransform>().SetParent(recipeList, false);
    }

    private void RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
            //button.Validate();
            // return true;
        }
        // else return false;
    }


    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        this.sourceFaction = null;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 10: // target faction left
                    button.Initialize(this, new Button_TargetFactionSwitch(this, true)); break;
                case 11: // target faction right
                    button.Initialize(this, new Button_TargetFactionSwitch(this, false)); break;
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        // build all presetLis

    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }

    public override void Notify(int optionID)
    {
        //Debug.Log("Parent Notified ! [" + optionID + "]");
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            switch (optionID)
            {
                case 9999: scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        /*
        while (list_Jobs.transform.childCount > 0)
        {
            DestroyImmediate(list_Jobs.transform.GetChild(0).gameObject);
        }*/
        //Debug.LogError("CANVAS MANAGEMENT ONDESTROY");
        scr_System_CampaignManager.current.NotifyUpdate();

    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {

            scr_System_SceneManager.current.UnloadLastCanvasFromScene();

        }
    }

    public class Button_SelectTrade : ButtonValidator, I_ButtonClickable
    {
        new scr_AddTransfer parent;
        ItemEntry entry;
        scr_addTrade box;
        public Button_SelectTrade(scr_AddTransfer parent, ItemEntry entry, scr_addTrade box) : base(parent)
        {
            this.parent = parent;
            this.entry = entry;
            this.box = box;
        }

        public override bool IsButtonValid()
        {
            if (parent.targetFaction != null) box.factionName.text = parent.targetFaction.FactionDisplayName;
            return parent.sourceFaction != null && parent.targetFaction != null && this.entry != null;
        }

        public void OnClickButton()
        {
            parent.NotifyAddTrade(entry, parent.targetFaction);
        }
    }

    public class Button_TargetFactionSwitch : ButtonValidator, I_ButtonClickable
    {
        new scr_AddTransfer parent;
        bool left;
        public Button_TargetFactionSwitch(scr_AddTransfer parent, bool left) : base(parent)
        {
            this.parent = parent;
            this.left = left;
        }

        public override bool IsButtonValid()
        {
            return parent.targetFactions.Count > 1;
        }

        public void OnClickButton()
        {
            var index = parent.targetFactions.IndexOf(parent.targetFaction);
            Manageable next;

            if (left) next = index - 1 >= 0 ? parent.targetFactions[index - 1] : parent.targetFactions[parent.targetFactions.Count - 1];
            else next = index + 1 >= parent.targetFactions.Count ? parent.targetFactions[0] : parent.targetFactions[index + 1];

            parent.SetTargetFaction(next);
        }
    }
}
