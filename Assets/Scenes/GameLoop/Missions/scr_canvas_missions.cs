using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_canvas_missions : scr_Menu, IPointerClickHandler
{
    protected override void Awake()
    {
        base.Awake();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    string questTitleString = "";

    public override void Initialize()
    {
        base.Initialize();

        questTitleString = LocalizeDictionary.QueryThenParse("ui_mission_center_title");

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            switch (button.optionID)
            {
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default: break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }
        }

        QuestEvaluationResult firstResult = null;
        foreach(var quest in Masterlist_Event.Instance.Events.quests)
        {
            var result = QuestUtility.Evaluate(quest);
            if (result == null)
            {
                continue;
            }
            MakeButton_QuestToggle(result);
            if (firstResult == null) firstResult = result;
        }

        LoadQuestData(firstResult);

        ValidateAll();
    }

    void MakeButton_QuestToggle(QuestEvaluationResult quest)
    {
        RectTransform r = Instantiate(prefab_text_linkbutton);
        scr_SelectableText button = r.GetComponent<scr_SelectableText>();

        button.optionID = AssertUniqueHash(r.GetHashCode());
        button.Initialize(this, new ButtonValidator_QuestToggle(this, button, quest));
        button.SetText(QuestUtility.ParseQuestEntry(quest.quest.questID, quest.AppendStrings));
        r.SetParent(questList, false);

        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);
    }

    protected class ButtonValidator_QuestToggle : ButtonValidator, I_ButtonClickable
    {
        new scr_canvas_missions parent;
        scr_SelectableText button;
        QuestEvaluationResult quest;

        public ButtonValidator_QuestToggle(scr_canvas_missions parent, scr_SelectableText button, QuestEvaluationResult quest) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.quest = quest;
        }

        public override bool IsButtonValid()
        {
            button.Toggle(true, parent.CurrentQuest == quest);
            return true;
        }

        public void OnClickButton()
        {
            parent.LoadQuestData(quest);
        }
    }

    QuestEvaluationResult CurrentQuest = null;
    public void LoadQuestData(QuestEvaluationResult quest)
    {
        CurrentQuest = quest;

        quest_title.SetText(questTitleString.Replace("$title$", quest == null ?
            LocalizeDictionary.QueryThenParse("none") :
            QuestUtility.ParseQuestEntry(quest.quest.questID, quest.AppendStrings)));

        Utility.DestroyAllChildrenFrom(quest_stages_rect, 1);
        if (quest != null) PopulateStages(quest.stages, quest_stages_rect);
    }

    void PopulateStages(List<QuestStageResult> stages, RectTransform parentRect)
    {
        foreach (var stage in stages)
        {
            if (!stage.isValid) continue;

            PrefabQuestStage prefab = Instantiate(stagePrefab);
            prefab.GetComponent<RectTransform>().SetParent(parentRect, false);
            prefab.stageDescription.SetText(QuestUtility.ParseQuestEntry(stage.ID, stage.AppendStrings), false, "", false);

            PopulateStages(stage.subStages, prefab.substageRect);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_canvas_missions>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            Notify(9999);
        }
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


    public RectTransform questList;
    public scr_HoverableText quest_title;
    public RectTransform quest_stages_rect;
    public PrefabQuestStage stagePrefab;
}
