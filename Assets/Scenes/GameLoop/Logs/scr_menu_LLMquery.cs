using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEditor.Experimental.GraphView.GraphView;

public class scr_menu_LLMQuery : scr_Menu
{

    public TMP_Text messageText;

    public CanvasGroup SelfGroup;
    public Image SelfImage;

    public TMP_Text responseText;

    public RectTransform ResponseList;

    public bool _active = true;

    public bool Active 
    { 
        get
        {
            return _active;
        }
        set
        {
            _active = value;
            if (!_active)
            {
                SelfImage.color = scr_System_CentralControl.current.DisplaySetting.TextColor_transparent;
                SelfGroup.blocksRaycasts = false;
                SelfGroup.interactable = false;
                scr_UpdateHandler.current.Observer_LLMResponse -= OnResponse;
            }
        }
    }
    public LLMRequest request;
    Message_LLMQuery parent = null;
    public void InitializeWithArgs(Canvas mainCanvas, Message_LLMQuery parent, LLMRequest request, scr_panel_logs logs)
    {
        if (!initialized) Initialize();
        this.request = request;
        this.parent = parent;
        SetCanvas(mainCanvas, true);
        this.messageText.text = request.currentString;
        SelfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

       // Debug.Log($"Initializing question menu, grid size {Grid.cellSize.ToString()} rectTransformSizedelta {self.sizeDelta} deltaX {self.sizeDelta.x} rectwidth {self.rect.width} gridflexwidth {Grid.flexibleWidth} rectlocalscale");
        //Grid.cellSize = new Vector2(Grid.cellSize.x, (float)Math.Min(self.rect.width * 0.9, preferredLen));
        ValidateAll();

        scr_UpdateHandler.current.Observer_LLMResponse += OnResponse;
        scr_UpdateHandler.current.Observer_LLMStatus += OnUpdate;
    }

    List<LLMResponse> currentList = new List<LLMResponse>();
    int currentIndex = -1;

    LLMResponse CurrentResponse = null;
    LLMMessage.MessageJSON internalJson = null;
    public bool HasValidResponse
    {
        get
        {
            return CurrentResponse != null;
        }
    }
    public bool HasNext
    {
        get
        {
            return currentIndex > -1 && currentList.Count > currentIndex + 1;
        }
    }
    public bool HasPrev
    {
        get
        {
            return currentIndex > -1;
        }
    }

    /// <summary>
    /// load internal state and animate one step
    /// </summary>
    /// <param name="prev"></param>
    public void LoadResponse(bool prev = false)
    {
        if (currentList.Count < 1) return;

        currentIndex += (prev ? -1 : 1);
        if (currentIndex < 0) currentIndex = currentList.Count - 1;
        else if (currentIndex >= currentList.Count) currentIndex = 0;

        Utility.DestroyAllChildrenFrom(ResponseList);

        CurrentResponse = currentList[currentIndex];
        reload = true;

        if (CurrentResponse != null && CurrentResponse.JSON != null)
        {
            List<string> names = new List<string>();
            foreach(var i in CurrentResponse.JSON.relevantActorRefs)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(i);
                if (c != null && !names.Contains(c.FirstName)) names.Add(c.FirstName);
            }

            List<string> tooltips = new List<string>();
            bool allvalid = true;
            foreach (var ap in CurrentResponse.JSON.actionpackages)
            {
                bool isvalid = ap.Validate();
                allvalid = isvalid && allvalid;
                var result = ap.GetCheckResult(false);
                tooltips.Add(ap.GetTooltips($"{ap.DisplayName} isvalid? [{isvalid}]: doers [$doer$], receivers [$receiver$]"));
            }
            tooltipText.SetText(allvalid ? "all packages parsed successfully" : "error in package parsing");
            tooltipText.SetExternalTooltip($"Relevant actors {CurrentResponse.JSON.relevantActorRefs.Count} [{String.Join(" ",names)}]\ntimecost [{CurrentResponse.JSON.timeCost}]\n{String.Join("\n", tooltips)}");
        }

        Animate();
    }
    public void ExecuteResponse()
    {
        this.Active = false;
        bool update = false;
        if (CurrentResponse != null && CurrentResponse.JSON != null)
        {
            scr_System_CentralControl.current.AutoSave();

            //Debug.Log("Adding package to job [" + job.GetJobDescription(0) + "] with actors [" + String.Join(" ", package.actorRefs)+"], doers["+ String.Join(" ", package.DoerRefs)+"] receivers["+ String.Join(" ", package.ReceiverRefs) + "]");
            //scr_System_CampaignManager.current.Player.ChangeCurrentJob(job);


            var json = CurrentResponse.JSON;
            var allrelevantActors = new List<int>(CurrentResponse.JSON.relevantActorRefs);

            foreach (var ap in CurrentResponse.JSON.actionpackages)
            {
                allrelevantActors.AddRange(ap.DoerRefs);
                allrelevantActors.AddRange(ap.ReceiverRefs);
            }
            allrelevantActors.RemoveAll(x => x < 0);
            allrelevantActors = Utility.Distinct(allrelevantActors);


            var playerjob = scr_System_CampaignManager.current.FindJobInstanceByID(scr_System_CampaignManager.current.jobRef_playerCOM);
            playerjob.m.displayOverride = true;

            foreach (var actor in allrelevantActors)
            {
                scr_System_CampaignManager.current.FindInstanceByID(actor).ChangeCurrentJob(playerjob);
            }

            json.timeCost = Math.Clamp(json.timeCost, 5, 60);

            var apLLM = new ActionPackage_LLM(playerjob, json.timeCost, allrelevantActors, CurrentResponse.JSON);

            playerjob.AddPackage(new List<ActionPackage>() { apLLM }, true);
            //scr_System_CampaignManager.current.Register(apLLM, false);
            update = true;

        }
        scr_UpdateHandler.current.LLMStatus = LLMStatus.inactive;
        if (update) scr_System_CampaignManager.current.FreeUpdate();
    }
    public scr_HoverableText tooltipText;

    public bool canAnimate = true;
    bool reload = true;
    public scr_HoverableText prefab_LogLine;
    public scr_MessageLogBox prefab_LogBox;

    void DrawLine( LLMMessage.MessageParagraph s)
    {
        var box = Instantiate(prefab_LogBox);
        box.SelfRect.SetParent(ResponseList, false);
        //box.SelfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        var c = scr_System_CampaignManager.current.FindInstanceByID(s.portraitRefID);
        Message_Text text = new Message_Text(c, s.portraitTags, s.content_text, false);
        text.animateAllOverride = true;
        text.Draw(false, box, prefab_LogLine);
    }

    void DrawLine(string s)
    {
        var prefab = Instantiate(prefab_LogLine);
        prefab.SelfRect.SetParent(ResponseList, false);
        prefab.SetText(s);
    }

    public void Animate()
    {
        if (CurrentResponse == null) return;

        var message = CurrentResponse.JSON;
        if (message != null)
        {
            responseText.text = "";
            if (message.content_blocks.Count > 0)
            {
                var startIndex = reload ? 0 : message.animatedIndex;
                var endIndex = Math.Min(message.animatedIndex + 1, message.content_blocks.Count);
                for (int i = startIndex; i < endIndex; i++)
                {
                    //RectTransform msgbox = Instantiate(prefab_LogEntry);
                    //if (parent != null) parent.Draw(false, msgbox.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine);
                    DrawLine(message.content_blocks[i]);
                }
                message.animatedIndex = endIndex;
                canAnimate = message.animatedIndex < message.content_blocks.Count;
                if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"reload {reload}, start {startIndex} end {endIndex} animated_final {message.animatedIndex}");
            }
            else if (message.content_string.Length > 0 && message.animatedIndex < message.content_string.Length)
            {
                //RectTransform msgbox = Instantiate(prefab_LogEntry);
                DrawLine(message.content_string);
                message.animatedIndex = message.content_string.Length;
                canAnimate = false;
            }
        }
        else
        {
            responseText.text = "error no message in response";
            canAnimate = false;
        }
        reload = false;
        ValidateAll();
    }



    void OnUpdate(LLMStatus status)
    {
        ValidateAll();
    }

    public void OnResponse(LLMResponse response)
    {
        Debug.Log("OnResponse!");
        this.currentList.Add(response);
        LoadResponse();
    }


    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            switch (button.optionID)
            {
                case -1: break;
                case 1000:
                    button.Initialize(this, new Button_LoadPrev(this, button));
                    break;
                case 1001:
                    button.Initialize(this, new Button_DiscardAll(this, button));
                    break;
                case 1002:
                    button.Initialize(this, new Button_Confirm(this, button));
                    break;
                case 1003:
                    button.Initialize(this, new Button_Regenerate(this, button));
                    break;
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
    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    protected void OnClick(PointerEventData pointer)
    {

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
                default: break;
            }
        }
        ValidateAll();
    }


    protected override void Awake()
    {
        base.Awake();
        //this.sourceFaction = null;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public class Button_Confirm : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_Confirm(scr_menu_LLMQuery parent, scr_SelectableText button):base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) return false;
            if (parent.canAnimate) return false;
            return parent.Active && parent.HasValidResponse;
        }
        public void OnClickButton()
        {
            parent.ExecuteResponse();
        }
    }

    public void Regenerate()
    {
        scr_UpdateHandler.current.SendLLMRequest(this.request);
    }



    public void CancelResponse()
    {
        scr_UpdateHandler.current.InterruptLLMRoutine();
    }

    private void OnEnable()
    {
       // ValidateAll();
    }

    public class Button_Regenerate : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_Regenerate(scr_menu_LLMQuery parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            //Debug.Log("IsButtonValid");
            if (!parent.Active) return false;
            if (parent.canAnimate) return false;
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) 
            {
                button.SetText(LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_cancelRegen"));
            }
            else if (parent.HasNext)
            {
                button.SetText(LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_next"));
            }
            else
            {
                button.SetText(LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_regenerate"));
            }
            return true;
        }
        public void OnClickButton()
        {
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) parent.CancelResponse();
            else if (parent.HasNext) parent.LoadResponse();
            else parent.Regenerate();
        }
    }


    public class Button_LoadPrev : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_LoadPrev(scr_menu_LLMQuery parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (!parent.Active) return false;
            if (parent.canAnimate) return false;
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) return false;
            if (parent.currentList.Count < 2) return false;
            return parent.HasPrev;
        }
        public void OnClickButton()
        {
            parent.LoadResponse(true);
        }
    }
    public class Button_DiscardAll : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_DiscardAll(scr_menu_LLMQuery parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (!parent.Active) return false;
            if (parent.canAnimate) return false;
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) return false;
            return true;
        }
        public void OnClickButton()
        {
            parent.Active = false;
            scr_UpdateHandler.current.LLMStatus = LLMStatus.inactive;
            scr_System_CampaignManager.current.AddLog(-1, LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_abort_message"), true);
            scr_UpdateHandler.current.NotifyLogsSingleUpdate(true);
        }
    }
}

