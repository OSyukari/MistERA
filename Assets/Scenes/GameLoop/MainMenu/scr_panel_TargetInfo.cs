using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using Unity.Jobs;
using UnityEngine.UI;
using System.Linq;

public class scr_panel_TargetInfo : scr_Menu
{
    private scr_System_CampaignManager campaignManager;

    public RectTransform parentBox1, parentBox2, parentBox3;

    public scr_HoverableText hp, mp, st, en;
    public scr_HoverableText fullname;

    public scr_HoverableText prideBox, corruptBox, despairBox;
    public scr_HoverableText attitudeBox, obedienceBox;
    public scr_HoverableText moodBox, stressBox, lustBox;
    public RectTransform StatusBox;

    public TMP_Text currentJobDesc;
    public TMP_Text nextHourJobDesc;

    public CanvasGroup self_canvasGroup;
    //private Image image_bg;

    protected override void Start()
    {
        //update = 0.0f;        
        //image_bg = this.GetComponent<Image>();
        //parentBox1.gameObject.SetActive(false);
        //parentBox2.gameObject.SetActive(false);
        //parentBox3.gameObject.SetActive(false);
        //self_canvasGroup.alpha = 0;


        if (!initialized) Initialize();
    }

    protected override void Awake()
    {
        base.Awake();

        self_canvasGroup.gameObject.SetActive(false);

        campaignManager = scr_System_CampaignManager.current;
        campaignManager.Observer_CurrentTarget += ReadCurrentChar;
        campaignManager.Observer_UpdateNotice += OnNotifyUpdate;
        campaignManager.Observer_CurrentViewMode += OnViewChange;
    }

    protected override void OnDestroy()
    {
        if (!ReferenceEquals(campaignManager, null))
        {
            campaignManager.Observer_CurrentTarget -= ReadCurrentChar;
            campaignManager.Observer_UpdateNotice -= OnNotifyUpdate;
            campaignManager.Observer_CurrentViewMode -= OnViewChange;
            campaignManager = null;
        }
        base.OnDestroy();
    }

    private void OnViewChange(ViewMode v, bool b) { Refresh(); }

    private void ReadCurrentChar(int id, bool foceUpdate)
    {
        Refresh();
    }

    private void OnNotifyUpdate(bool value)
    {
        Refresh();
    }

    Character_Trainable chara = null;

    //private float update;

    public RectTransform Grid_UnequipAll;
    public RectTransform Grid_UnequipSingles;

    List<int> managedEquipRefs = new List<int>();
    string nameTooltip = "";
    List<string> ttips1 = new List<string>(), ttips2 = new List<string>();
    public RectTransform prefab_equippedItem;
    Character_Relationship rel;
    private void Refresh()
    {

        chara = scr_System_CampaignManager.current.CurrentTarget;

        var turnOff = false;
        if (chara != null && chara.RefID > 0)
        {
            rel = chara.Relationships.FindRelationshipWith(0);
            //image_bg.raycastTarget = true;
            //parentBox1.gameObject.SetActive(true);
            //parentBox2.gameObject.SetActive(true);
            //parentBox3.gameObject.SetActive(true);
            //self_canvasGroup.alpha = 1;
            self_canvasGroup.gameObject.SetActive(true);

            fullname.SetText(chara.FullName+(chara.FactionManager.CurrentlyActiveFaction == null ? "" : ", "+chara.CurrentActiveFactionName));

            nameTooltip = chara.FactionManager.CurrentlyActiveFaction == null ? "" : chara.FactionManager.CurrentlyActiveFaction.GetCharaSocialStandingTooltip(chara);
            RelationshipManager.GetRelTooltip(rel, ref nameTooltip, ttips1, ttips2);
            fullname.SetExternalTooltip(nameTooltip);

            if(chara.Stats.HP != null) chara.Stats.HP.Draw(hp);
            else this.hp.SetText(" - ");

            if (chara.Stats.MP != null) chara.Stats.MP.Draw(mp);
            else this.mp.SetText(" - ");

            if (chara.Stats.Stamina != null) chara.Stats.Stamina.Draw(st);
            else this.st.SetText(" - ");

            if (chara.Stats.Energy != null) chara.Stats.Energy.Draw(en);
            else this.en.SetText(" - ");

            //if (chara.FactionManager.CurrentlyActiveFaction != null) socialStatusBox.SetText(chara.FactionManager.CurrentlyActiveFactionStatus);

            RelationshipManager.Draw_Attitude(chara, rel, attitudeBox);

            if (chara.Stats.Mood != null) chara.Stats.Mood.Draw(moodBox);
            else this.moodBox.SetText(" - ");
            if (chara.Stats.Stress != null) chara.Stats.Stress.Draw(stressBox);
            else this.stressBox.SetText(" - ");

            if (chara.Stats.Lust != null) chara.Stats.Lust.Draw(lustBox);
            else this.lustBox.SetText(" - ");

            RefreshStatusBox();

            currentJobDesc.text = chara.GetJobDescription();

            int nextHour = scr_System_Time.current.getCurrentTime().Hour + 1;
            if (nextHour >= 24) nextHour -= 24;
            var nextHourJob = chara.FactionManager.CurrentJobPost(nextHour);
            Manageable faction = chara.FactionManager.CurrentJobScheduleFaction(nextHour);
            nextHourJobDesc.text = (nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (faction != null ? "(" + chara.FactionManager.CurrentJobScheduleFaction(nextHour).FactionDisplayName + ")" : "") ;

            /*
            foreach(var i in managedEquipRefs) DestroyCOMButton(i);
            managedEquipRefs.Clear();

            managedEquipRefs.AddRange(chara.EquippedItemRefs);
            managedEquipRefs.AddRange(chara.inventory_ref);

            managedEquipRefs = managedEquipRefs.Distinct().ToList();
            managedEquipRefs.Sort();

            foreach(var i in managedEquipRefs) MakeCOMButton(Grid_UnequipSingles, prefab_equippedItem, chara.RefID, i);
            */

            if (scr_System_CentralControl.current.isSafeMode)
            {
                lustBox.gameObject.SetActive(false);
            }
        }
        else
        {
            /*
            foreach (var i in managedEquipRefs) DestroyCOMButton(i);
            managedEquipRefs.Clear();
            */
            turnOff = true;
            self_canvasGroup.gameObject.SetActive(false);
        }
        ValidateAll();

        if(turnOff) self_canvasGroup.gameObject.SetActive(false);
    }

   // public TMP_Text prefab_statusDescription;

    private void RefreshStatusBox()
    {
        while (StatusBox.transform.childCount > 0)
        {
            DestroyImmediate(StatusBox.transform.GetChild(0).gameObject);
        }

        foreach (var si in chara.Stats.statusInstancesEx)
        {
            if (!si.Displayable) continue;
            RectTransform box = Instantiate(prefab_text_link);
            box.SetParent(StatusBox, false);

            si.Draw(box.GetComponent<scr_HoverableText>());// UI_Utility.Draw(si, 

            //text.text = si.BaseRef.displayName + ":" + si.SeverityDisplayName;
        }

        foreach ( var si in chara.Stats.StatusInstances)
        {
            if (!si.Displayable) continue;
            RectTransform box = Instantiate(prefab_text_link);
            box.SetParent(StatusBox, false);

            UI_Utility.Draw(si, box.GetComponent<scr_HoverableText>());

            //text.text = si.BaseRef.displayName + ":" + si.SeverityDisplayName;
        }
    }

    public override void Notify(int optionID)
    {
        /*
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
                default: break;
            }
        }
        ValidateAll();*/
    }

    public override void Initialize()
    {
        /*
        base.Initialize();


        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case -1: break;
                //case -2: button.Initialize(this, new ButtonValidator_FixClothes(this, button)); break;
                //case -3: button.Initialize(this, new ButtonValidator_RedressLayers(this, button,1)); break;
                //case -4: button.Initialize(this, new ButtonValidator_UndressLayers(this, button,1)); break;
                //case -5: button.Initialize(this, new ButtonValidator_UndressAll(this, button, 1)); break;

               // case -7: button.Initialize(this, new ButtonValidator_FixClothes(this, button)); break;
                default:
                    button.Initialize(this, button_alwaysValid);
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        // build all presetList
        ValidateAll();*/
    }

}
