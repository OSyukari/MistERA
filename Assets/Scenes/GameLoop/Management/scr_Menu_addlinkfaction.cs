using UnityEngine;
using System;
using UnityEngine.EventSystems;
using System.Linq;

public class scr_Menu_addlinkfaction : scr_Menu, IPointerClickHandler
{
    public Manageable sourceFaction;
    public RectTransform factionList;
    public scr_addLinkBTN prefab_link;

    public void InitializeWithArgument(Manageable sourceFaction, Action onExit)
    {
        this.onSelfExit = onExit;
        if (!initialized) Initialize();

        this.sourceFaction = sourceFaction;
        Utility.DestroyAllChildrenFrom( factionList);

        foreach (var faction in scr_System_CampaignManager.current.Factions)
        {
            // TODO INSTANTIATE BUTTON
            if (faction == sourceFaction) continue;
            else if (faction.MainExit == null) continue;
            MakeFactionButton(faction);
        }
        ValidateAll();
    }

    public void NotifyChange()
    {
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    private void MakeFactionButton(Manageable target)
    {
        int buttonHash = AssertUniqueHash(target.GetHashCode());
        scr_addLinkBTN box = Instantiate(prefab_link);
        box.Name.text = target.FactionDisplayName;
        RegisterButton(buttonHash, box.Button, new Button_ToggleLink(this, target, box.Button));
        box.GetComponent<RectTransform>().SetParent(factionList, false);
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

    public class Button_ToggleLink : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_addlinkfaction parent;
        ItemEntry entry;
        Manageable targetFaction;
        scr_SelectableText button;

        string add, add_tooltip, remove, remove_tooltip,  remove_error1;
        bool connect = false;
        public Button_ToggleLink(scr_Menu_addlinkfaction parent, Manageable targetFaction, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.targetFaction = targetFaction;
            this.button = button;

            add = LocalizeDictionary.QueryThenParse("ui_management_linkStatus_add");
            add_tooltip = LocalizeDictionary.QueryThenParse("ui_management_linkStatus_add_tooltip");
            remove = LocalizeDictionary.QueryThenParse("ui_management_linkStatus_remove");
            remove_tooltip = LocalizeDictionary.QueryThenParse("ui_management_linkStatus_remove_tooltip");
            remove_error1 = LocalizeDictionary.QueryThenParse("ui_management_linkStatus_remove_error1");
        }

        public override bool IsButtonValid()
        {
            if (parent.sourceFaction == targetFaction)
            {
                button.SetText(" - ");
                return false;
            }
            else
            {
                if (parent.sourceFaction.ConnectedFactions.Contains(targetFaction))
                {
                    button.SetText(remove);
                    if (targetFaction.ManagedRefs.Count > 0 && Utility.ListContainsLoose(parent.sourceFaction.ManagedRefs, targetFaction.ManagedRefs))
                    {   // if job assigned, return false
                        this.tooltip = remove_error1;
                        return false;
                    }

                    var targetRooms = targetFaction.ManagedRooms.Keys;
                    foreach(var chara in parent.sourceFaction.ManagedRefs)
                    {   // if any chara is present, return false
                        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(chara);
                        if (room != null && targetRooms.Contains( room.RefID))
                        {
                            this.tooltip = remove_error1;
                            return false;
                        }
                    }

                    this.tooltip = remove;
                    connect = false;
                    return true;
                    
                }
                else
                {
                    button.SetText(add);
                    this.tooltip = add_tooltip;
                    connect = true;
                    return true;
                }
            }
        }

        public void OnClickButton()
        {
            if (connect) scr_System_CampaignManager.current.Map.ConnectFactions(parent.sourceFaction, targetFaction);
            else scr_System_CampaignManager.current.Map.DisconnectFactions(parent.sourceFaction, targetFaction);
            parent.NotifyChange();
        }
    }



}
