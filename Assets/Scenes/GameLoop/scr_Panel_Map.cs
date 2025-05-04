using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;


public class scr_Panel_Map : scr_Menu, IPointerClickHandler
{

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

    public RectTransform Debug_MapFloorList;
    public RectTransform Map_FloorLayout;
    public RectTransform prefab_roomButton;

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        //if (this.gameObject.activeInHierarchy && vm == ViewMode.View_Map)
        //{
        //    if (this.FloorDisplay == null) makeFloorDisplay();
        //}
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                default:
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        ValidateAll();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        //draw all floors
        foreach (Floor_Instance floor in scr_System_CampaignManager.current.Map.Floors)
        {

            offset_mapwide = new Vector2(0, 0);

            MakeButton(floor, Map_FloorLayout, new Vector2(0,0));

            foreach(scr_SelectableText txt in buttonsByID.Values)
            {
                RectTransform r = txt.GetComponent<RectTransform>();
                r.SetParent(Map_FloorLayout, true);
                r.anchoredPosition += offset_mapwide;
            }
        }




        //draw current floor

        //Debug.Log("Current player floor is [" + scr_System_CampaignManager.current.PlayerFloor.refID + " " + scr_System_CampaignManager.current.PlayerFloor.FloorBase.ID + "]");
        ValidateAll();
        makeFloorDisplay();

    }

    Vector2 offset_mapwide;

    private void MakeButton(Floor_Instance floor, RectTransform parent, Vector2 offset)
    {
        if (!buttonsByID.ContainsKey(floor.refID))
        {
            RectTransform r2 = Instantiate(prefab_roomButton);
            r2.SetParent(parent, false);
            scr_SelectableText btn = r2.GetComponent<scr_SelectableText>();

            btn.Initialize(this, new ButtonValidator_ChangeFloor(this, floor, btn));

            btn.SetText(floor.displayName);

            r2.anchoredPosition = offset;
            r2.transform.rotation = Quaternion.identity;

            btn.optionID = floor.refID;
            buttonsByID.Add(btn.optionID, btn);
            validatorsByID.Add(btn.optionID, btn.Validator);

            //btn.showOptionID = true;
            //btn.SetText("");

            btn.Validate();

            offset_mapwide += -(offset / 4);


            Map_FloorLayout.sizeDelta = new Vector2(Mathf.Max(Map_FloorLayout.sizeDelta.x, Mathf.Abs(offset.x)+Mathf.Max(prefab_roomButton.sizeDelta.x, LayoutUtility.GetPreferredWidth(r2))), 
                Mathf.Max(Map_FloorLayout.sizeDelta.y, Mathf.Abs(offset.y)+ Mathf.Max(prefab_roomButton.sizeDelta.y, r2.rect.height)));
            



            foreach (int i in scr_System_CampaignManager.current.Map.GetConnectedFloorRefs(floor.refID))
            {
                Vector2 childOffset = scr_System_CampaignManager.current.Map.FloorLayout[new System.Tuple<int, int>(floor.refID, i)];
                MakeButton(scr_System_CampaignManager.current.Map.FindFloorByRefID(i), r2, childOffset/2.5f);
            }

        }
    }



    private canvas_RoomDisplay FloorDisplay = null;
    public RectTransform canvas_FloorDisplay;

    private void makeFloorDisplay(int floorRefID = -1)
    {
        FloorDisplay = scr_System_SceneManager.current.LoadCanvasIntoScene(canvas_FloorDisplay, this.m_Canvas.GetComponent<RectTransform>()).GetComponent<canvas_RoomDisplay>();
        
        //if (floorRefID == -1) FloorDisplay.InitializeWithArgument(this, scr_System_CampaignManager.current.PlayerFloor.refID);
        //else FloorDisplay.InitializeWithArgument(this, floorRefID);
    }


    public void destroyFloorDisplay()
    {
        if (FloorDisplay != null)
        {
            FloorDisplay.gameObject.SetActive(false);
            scr_System_SceneManager.current.UnloadLastCanvasFromScene(typeof(canvas_RoomDisplay));
            Destroy(FloorDisplay);
            FloorDisplay = null;
        }

    }

    public void changeFloorDisplay(int floorRefID)
    {
        destroyFloorDisplay();
        makeFloorDisplay(floorRefID);
    }

    private void OnDisable()
    {
        destroyFloorDisplay();
    }

    protected override void Awake()
    {
        base.Awake();
        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        this.gameObject.SetActive(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
    }

    public class ButtonValidator_ChangeFloor : ButtonValidator, I_ButtonClickable
    {
        Floor_Instance floor;
        scr_SelectableText text;
        new scr_Panel_Map parent;
        public ButtonValidator_ChangeFloor(scr_Panel_Map parent, Floor_Instance floor, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.floor = floor;
            this.text = text;
        }


        public override bool IsButtonValid()
        {
            if (scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(scr_System_CampaignManager.current.CurrentRoom.RefID).refID == floor.refID)
            {
                this.text.Toggle(true, true);
            }
            else
            {
                this.text.Toggle(true, false);
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.changeFloorDisplay(floor.refID);
        }
    }
}
