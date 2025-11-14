using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class scr_prefab_actortab : MonoBehaviour, IPointerEnterHandler
{
    public List<Image> transparent = new List<Image>();

    protected void Start()
    {
        var color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent;
        foreach(var i in transparent)
        {
            i.color = color.Color;
        }
    }

    public scr_CharIconBox imageBox;

    public scr_HoverableText  status, action, location;
    public scr_SelectableText nameBox,  btn_plus, btn_minus;

    public RectTransform actionList;
    public RectTransform SelfRect;

    public Character_Trainable c;
    public CombatStatManager Stats;
    public scr_SelectableText extra_EOTAction;

    public Dictionary<int, scr_Menu_Combat.Button_OpenActionSelect> actions = new Dictionary<int, scr_Menu_Combat.Button_OpenActionSelect>();
    public int overrideCount = 2;
    int maxIndex, prevIndex = -1;

    public bool isHostile;

    public void Load(scr_Menu_Combat Parent, I_StatsManager Stats, Character_Trainable c, bool isHostile)
    {
        _actionCountCache = LocalizeDictionary.QueryThenParse("ui_actor_actionsCount");

        this.Parent = Parent;
        this.Stats = Stats as CombatStatManager;
        this.c = c;

        imageBox.InitializeWithArgument(this.c.RefID, null, this.Stats);

        Utility.DestroyAllChildrenFrom( actionList);

        this.isHostile = isHostile;


        if (isHostile)
        {
            btn_minus.gameObject.SetActive(false);
            btn_plus.gameObject.SetActive(false);
        }
        else
        {
            this.Parent.MakeModCountButton(this, this.btn_minus, true);
            this.Parent.MakeModCountButton(this, this.btn_plus, false);
        }

        if (!isHostile)
        {
            this.nameBox.forbidNotify = true;
            this.nameBox.showBrackets = false;
        }
        else
        {
            this.Parent.MakeTargetSelectBTN(this, this.nameBox);
        }

        this.Parent.MakeEOTActionButton(c, this.extra_EOTAction);
    }

    public bool CanModCount()
    {
        return scr_System_CampaignManager.current.DebugMode || !isHostile;
    }

    scr_Menu_Combat Parent;

    string _actionCountCache;
    bool firstInit = true;
    public void AddActionCount(CombatInstance Handler)
    {
        if (!actions.ContainsKey(this.overrideCount))
        {
            var rect = Parent.MakeActionButton(c, this.overrideCount);
            rect.selfRect.SetParent(this.actionList, false);
            actions.Add(this.overrideCount, rect);
        }
        this.overrideCount++;
        UpdateContent(Handler, true);
    }
    public void ReduceActionCount(CombatInstance Handler)
    {
        this.overrideCount = Math.Max(2, this.overrideCount - 1);
        Debug.Log($"ReduceActionCount new override [{this.overrideCount}] max [{maxIndex}]");
        if (maxIndex > this.overrideCount)
        {
            UpdateContent(Handler, true);
            Handler.RemoveActionsOngoing(c, this.overrideCount - 1);
        }
        else UpdateContent(Handler, true);
        //UpdateContent(Handler, true);
        //return maxIndex > this.overrideCount;
    }

    public void UpdateContent(CombatInstance Handler, bool useOverride = false)
    {
        this.nameBox.SetText(Handler.GetName(c));
        if (scr_System_CampaignManager.current.DebugMode) this.nameBox.GetComponent<scr_HoverableText>().SetExternalTooltip($"refid: {c.RefID}");

        this.imageBox.OnUpdateNotice();
        this.imageBox.CombatRefresh(Stats, true);

        this.status.SetText("");

        //if (Stats.HP != null) Stats.HP.Draw(this.hp);
        //else this.hp.SetText("-");

        location.SetText(Handler.GetLocationName(c));

        bool canPush = Handler.roundMaxAction > 2 && Stats.CanPush;

        this.action.SetText(_actionCountCache.Replace("$count$", $"{Handler.MaxActionsByCharaRef(c.RefID)}{(canPush ? "+":"")}"));

        var preset_strs = new List<string>();
        foreach(var preset in Stats.ValidPresets)
        {
            preset_strs.Add(preset.Value.ID);
        }

        this.action.SetExternalTooltip($"Valid Presets:\n{String.Join("\n", preset_strs)}");

        var actorActions = Handler.ActionsByCharaRef(c.RefID);
        maxIndex = Handler.MaxActionsByCharaRef(c.RefID);// actorActions.Count < 2 ? 2 : actorActions.Last().ActionSlotIndex;


        if (prevIndex < maxIndex) this.overrideCount = Math.Max(this.overrideCount, maxIndex);
        prevIndex = maxIndex;

        string s = $"Actor [{c.FirstName}] UpdateContent: actionsCount[{actions.Count}] actorActionsCount[{actorActions.Count}] MaxIndex: {maxIndex} Override {this.overrideCount}";

        //Debug.Log(s);

        List<int> tempList = new List<int>();

        for (int i = 0; i < this.overrideCount || i < actions.Count; i++)
        {
            tempList.Add(i);
            if (actions.ContainsKey(i)) continue;
            var rect = Parent.MakeActionButton(c, i);
            rect.selfRect.SetParent(this.actionList, false);
            actions.Add(i,rect);
        }

        foreach(var act in actorActions) 
        {
            //if (act.isEOTAction) continue;
            if (act.ActionSlotIndex >= this.overrideCount || act.isEOTAction) continue;
            actions[act.ActionSlotIndex].ResetAction(act, this.overrideCount);
            tempList.Remove(act.ActionSlotIndex);
        }
        foreach(var key in tempList) actions[key].ResetAction(null, this.overrideCount);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        //Debug.Log($"pointer enter {c.CallName}");
        Parent.LoadChara(this.c, !isHostile);
    }
}
