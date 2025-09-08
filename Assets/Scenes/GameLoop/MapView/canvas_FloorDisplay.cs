using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using QuikGraph;
using System;
using System.Linq;

public class canvas_RoomDisplay : scr_Menu, IPointerClickHandler
{

    public TMP_Text floorName;
    public RectTransform roomList;

    public IEnumerable<TaggedEdge<int, Door_Instance>> path = null;
    public int pathCost = 0;

    public Image picture;

    Floor_Instance floor = null;

    public RectTransform prefab_roomButton;

    //scr_Panel_Map parent;

    protected List<int> currentFloorIDs = new List<int>();


    private void addExit(RectTransform prefab, RectTransform parent, Floor_Base.FloorPlan_Exit exits, Floor_Instance targetFloor)
    {
        RectTransform r2 = Instantiate(prefab);
        r2.SetParent(parent, false);
        r2.anchoredPosition = new Vector2(exits.offsetX, exits.offsetY);

        r2.transform.rotation = Quaternion.identity;

        scr_SelectableText btn = r2.GetComponent<scr_SelectableText>();

        btn.Initialize(this, new ButtonValidator_ChangeFloor(this, targetFloor, btn));
        btn.SetText(targetFloor.displayName);

        btn.optionID = (floor.GetHashCode() + exits.GetHashCode());
        buttonsByID.Add(btn.optionID, btn);
        validatorsByID.Add(btn.optionID, btn.Validator);
        btn.Validate();

        currentFloorIDs.Add(btn.optionID);
    }

    private void addBtn(RectTransform prefab, RectTransform parent, Room_Instance ri, bool extraOffset = false, bool displayCharaName = false, bool ignorePathToggle = false)
    {
        RectTransform r2 = Instantiate(prefab);
        r2.SetParent(parent, false);
        r2.anchoredPosition = new Vector2(ri.Base.offsetX, ri.Base.offsetY);

        r2.transform.rotation = Quaternion.identity;

        scr_SelectableText btn = r2.GetComponent<scr_SelectableText>();


        Floor_Instance parentFloor = scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(ri.RefID);
        int tempRefID = ri.RefID;
        if (parentFloor != null) tempRefID -= parentFloor.FloorCode;

        if (displayCharaName)
        {
            btn.Initialize(this, new ButtonValidator_MoveRoom(this, ri, btn, false, ignorePathToggle));
            List<int> list = scr_System_CampaignManager.current.CharaInRoom(ri.RefID);
            List<string> names = new List<string>();
            foreach (int i in list)  if (i != 0) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);

            btn.SetText(tempRefID + " - " + ri.DisplayName);

            var namesRect = Instantiate(prefab_text_standard);
            namesRect.SetParent(parent, false);
            namesRect.GetComponent<TMP_Text>().text = String.Join(" ", names);
        }
        else
        {
            btn.Initialize(this, new ButtonValidator_MoveRoom(this, ri, btn, true, ignorePathToggle));
            btn.SetText(tempRefID.ToString());
        }

        if (extraOffset) btn.optionID = (floor.GetHashCode() + ri.GetHashCode()) * 2 + 1;
        else btn.optionID = (floor.GetHashCode() + ri.GetHashCode()) * 2;


        buttonsByID.Add(btn.optionID, btn);
        validatorsByID.Add(btn.optionID, btn.Validator);
        btn.Validate();


        currentFloorIDs.Add(btn.optionID);
    }


    public override void Initialize()
    {
        base.Initialize();
        this.m_Canvas.overrideSorting = true;

        selfRect = GetComponent<RectTransform>();
        this.buttonsByID = new Dictionary<int, scr_SelectableText>();
        validatorsByID = new Dictionary<int, ButtonValidator>();

        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);


        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case -9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        // build all presetList
        ValidateAll();
    }

    public scr_factionBlock prefab_FactionBlock;
    public RectTransform FactionList;

    protected void InitFloorList()
    {
        //Debug.Log("initfloorlist");
        var currentFaction = scr_System_CampaignManager.current.CurrentRoom.FactionOwner;
        BuildFaction(currentFaction);
        foreach (var connect in currentFaction.ConnectedFactions)
        {
            BuildFaction(connect);
        }
    }

    List<int> noDestroyList = new List<int>();

    protected void BuildFaction(I_IsJobGiver faction)
    {
        if (faction == null) 
        {
           // Debug.LogError("initfaction error faction null");
            return;
        }
        else
        {
           // Debug.Log("initffactionblock " + faction.FactionDisplayName);
        }

        var block = Instantiate(prefab_FactionBlock);
        block.transform.SetParent(FactionList, false);
        block.factionTitle.text = faction.FactionDisplayName;

        foreach(var floor in faction.ManagedFloors)
        {
            var btn = Instantiate(block.buttonPrefab);
            btn.transform.SetParent(block.floorList, false);

            btn.Initialize(this, new ButtonValidator_Floor(this, floor, btn));
            btn.SetText(floor.displayName);

            btn.optionID = floor.GetHashCode();
            buttonsByID.Add(btn.optionID, btn);
            validatorsByID.Add(btn.optionID, btn.Validator);

            noDestroyList.Add(btn.optionID);
        }
    }

    Coroutine CO = null;

    IEnumerator LoadFloorTex(Floor_Instance floornew = null)
    {
        floor = floornew;
        // wipe previous
        foreach (var button in currentFloorIDs)
        {
            if (noDestroyList.Contains(button)) continue;
            var validator = this.validatorsByID[button];

            this.buttonsByID.Remove(button);
            this.validatorsByID.Remove(button);

            validator.Destroy();
        }
        currentFloorIDs.Clear();
        Utility.DestroyAllChildrenFrom( roomList);
        var pictureRect = picture.rectTransform;
        Utility.DestroyAllChildrenFrom( pictureRect);

        Texture2D texture = null;
        yield return AssetsLoader.LoadTextureCoroutine(floor.FloorBase.imagePath, tex => texture = tex);

        picture.sprite = scr_System_CentralControl.current.GetSprite(texture);
        var scale = floor.FloorBase.resize;
        picture.rectTransform.localScale = new Vector3(scale, scale, scale);
        floorName.text = floor.displayName;

        foreach (Room_Instance ri in floor.rooms)
        {
            if (!buttonsByID.ContainsKey((floor.GetHashCode() + ri.GetHashCode()) * 2))
            {
                addBtn(prefab_roomButton, picture.rectTransform, ri, false, false, false);
                addBtn(prefab_roomButton, roomList, ri, true, true, true);

            }
            else
            {
                //Debug.Log("scr_Panel_Map OnEnable skipping redraw for room [" + ri.displayName + "]");
            }

            if (scr_System_CampaignManager.current.Map.floorDoorQuickSearch.ContainsKey(ri.RefID))
            {
                //Debug.Log("scr_Panel_Map searching room with floor exit found match ["+ri.displayName+"]");
                int i = scr_System_CampaignManager.current.Map.floorDoorQuickSearch[ri.RefID];
                Floor_Base.FloorPlan_Exit exit = floor.FloorBase.exits.Find(x => x.connectedRoom == ri.Base.ID);
                var j = scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(i);
                addExit(prefab_roomButton, picture.rectTransform, exit, j);
            }
        }

        ValidateAll();
    }

    public void LoadFloor(Floor_Instance floornew = null)
    {
        if (this.floor == null) InitFloorList();

        if (floornew == null) Debug.LogError("canvas_RoomDisplay ATTEMPTING TO DISPLAY NONEXISTENT ROOM");
        if (floor != floornew)
        {
            // create new
            if (CO != null)
            {
                StopCoroutine(CO);
                CO = null;
            }
            CO = StartCoroutine(LoadFloorTex(floornew));
        }
        else
        {
            ValidateAll();
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
                case -9999:
                    //this.gameObject.SetActive(false);
                    //parent.destroyFloorDisplay();
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
    }

    RectTransform selfRect;

    Texture2D SpriteTexture = null;
    Sprite NewSprite;

    public void NotifyMove()
    {
        this.Notify(-9999);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Destroy(SpriteTexture);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerPress == eventData.rawPointerPress && eventData.button == PointerEventData.InputButton.Left) Notify(-9999);
        else if (eventData.pointerPress != eventData.rawPointerPress && eventData.button == PointerEventData.InputButton.Right) Notify(-9999);

        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");
    }


    public class ButtonValidator_MoveRoom : ButtonValidator, I_ButtonClickable
    {
        Room_Instance room { get { return scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef); } }
        scr_SelectableText text;
        Job playerJob { get { return scr_System_CampaignManager.current.Player.InteractionJob;} }

        int roomRef = -1;
        bool ignorePathToggle;
        new canvas_RoomDisplay parent;

        string ttip = "";
        public ButtonValidator_MoveRoom(canvas_RoomDisplay parent, Room_Instance room, scr_SelectableText text, bool attachHover = false, bool ignorePathToggle = false) : base(parent)
        {
            this.roomRef = room.RefID;
            this.text = text;
            this.parent = parent;
            this.ignorePathToggle = ignorePathToggle;

            text.AttachOnHoverEnter(OnHoverEnter);
            text.AttachOnHoverExit(OnHoverExit);
            

            ttip = "Room: " + room.DisplayName;
            if (room.Furnitures.Count > 0) ttip += "\nFurnitures: " + room.DisplayableFurnitureNames;
            List<int> list = scr_System_CampaignManager.current.CharaInRoom(room.RefID);
            List<string> names = new List<string>();
            foreach (int ii in list) if (ii != 0) names.Add(scr_System_CampaignManager.current.FindInstanceByID(ii).FirstName);
            if (names.Count > 0) ttip += "\nCurrently in room: " + String.Join(" ", names);
        }

        protected void OnHoverExit()
        {
            parent.path = null;
            parent.ValidateAll();
        }

        protected void OnHoverEnter()
        {
            parent.path = scr_System_CampaignManager.current.Map.Findpath(0, room.RefID);
            float i = 0f;
            if (parent.path != null) foreach (TaggedEdge<int, Door_Instance> e in parent.path) i += e.Tag.Cost;
            parent.pathCost = (int)i;

            parent.ValidateAll();
        }

        public override bool IsButtonValid()
        {
            if (scr_System_CampaignManager.current.CurrentRoom == room)
            {
                this.text.Toggle(true, true);
                return true;
            }
            else if (parent.path != null && parent.path.ToList().Find(x => x.Source == room.RefID || x.Target == room.RefID) != null)
            {
                this.text.Toggle(true, false);
                this.tooltip = ttip + "\nTravel Cost: " + (parent.pathCost).ToString() + " minutes";
                return true;
            }
            else if (parent.path == null)
            {
                this.text.Toggle(true, false);
                this.tooltip = ttip;
                return true;
            }
            else // parent path not null and current room not in path
            {
                return ignorePathToggle;
            }
        }

        public void OnClickButton()
        {
            if (room.RefID == scr_System_CampaignManager.current.CurrentRoom.RefID)
            {
                scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
                return;
            }

            ActionPackage_PathTo package = new ActionPackage_PathTo(playerJob, 0, room.RefID);
            playerJob.AddPackage(new List<ActionPackage>(){ package});

            //scr_System_CampaignManager.current.FreeUpdate();
            scr_System_CampaignManager.current.FreeUpdate();
            parent.NotifyMove();
            //scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }

        public override void Destroy()
        {
            base.Destroy();
            DestroyImmediate(this.text);
        }
    }

    public class ButtonValidator_ChangeFloor : ButtonValidator, I_ButtonClickable
    {
        Floor_Instance floor;
        scr_SelectableText text;
        new canvas_RoomDisplay parent;
        public ButtonValidator_ChangeFloor(canvas_RoomDisplay parent, Floor_Instance floor, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.floor = floor;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            return true;
        }

        public void OnClickButton()
        {
            parent.LoadFloor(floor);
        }
        public override void Destroy()
        {
            base.Destroy();
            DestroyImmediate(this.text);
        }
    }

    public class ButtonValidator_Floor : ButtonValidator, I_ButtonClickable
    {
        Floor_Instance floor;
        scr_SelectableText text;
        new canvas_RoomDisplay parent;
        public ButtonValidator_Floor(canvas_RoomDisplay parent, Floor_Instance floor, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.floor = floor;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (parent.floor == floor)
            {
                text.Toggle(true, true);
            }
            else
            {
                text.Toggle(true, false);
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.LoadFloor(floor);
        }
    }
}
