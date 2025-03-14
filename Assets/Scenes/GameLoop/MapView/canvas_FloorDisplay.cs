using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using QuikGraph;
using System;

public class canvas_RoomDisplay : scr_Menu, IPointerClickHandler
{

    public TMP_Text floorName;
    public RectTransform roomList;

    public Image picture;

    Floor_Instance floor = null;

    public RectTransform prefab_roomButton;

    scr_Panel_Map parent;

    private void LoadFloor(int refID)
    {

    }
    public void InitializeWithArgument(scr_Panel_Map parent, int refID)
    {
        if (!initialized) this.Initialize();
        this.parent = parent;

        //Debug.Log("Chara Detail button keys :" + this.buttonsByID.Keys.ToString());

        //LoadFloor(refID);

        floor = scr_System_CampaignManager.current.Map.FindFloorByRefID(refID);
        if (floor != null)
        {
            picture.sprite = scr_System_CentralControl.current.LoadCachedSprite(floor.FloorBase.imagePath);

            floorName.text = floor.displayName;

            foreach(Room_Instance ri in floor.rooms)
            {
                if (!buttonsByID.ContainsKey((floor.GetHashCode() + ri.GetHashCode())*2))
                {
                    addBtn(prefab_roomButton, picture.rectTransform, ri, false);
                    addBtn(prefab_roomButton, roomList, ri, true, true);

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

           
        }
        else
        {
            Debug.LogError("canvas_RoomDisplay ATTEMPTING TO DISPLAY NONEXISTENT ROOM");
        }

        ValidateAll();

    }

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
    }

    private void addBtn(RectTransform prefab, RectTransform parent, Room_Instance ri, bool extraOffset = false, bool displayCharaName = false)
    {
        RectTransform r2 = Instantiate(prefab);
        r2.SetParent(parent, false);
        r2.anchoredPosition = new Vector2(ri.Base.offsetX, ri.Base.offsetY);

        r2.transform.rotation = Quaternion.identity;

        scr_SelectableText btn = r2.GetComponent<scr_SelectableText>();

        btn.Initialize(this, new ButtonValidator_MoveRoom(this, ri, btn));

        Floor_Instance parentFloor = scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(ri.RefID);
        int tempRefID = ri.RefID;
        if (parentFloor != null) tempRefID -= parentFloor.FloorCode;

        if (displayCharaName)
        {
            List<int> list = scr_System_CampaignManager.current.CharaInRoom(ri.RefID);
            List<string> names = new List<string>();
            foreach (int i in list)  if (i != 0) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);

            

            if (names.Count > 0)
            {
                btn.SetText(tempRefID + " - "+ ri.DisplayName + "] \n["+String.Join(" ", names));
            }
            else
            {
                btn.SetText(tempRefID + " - " + ri.DisplayName, true);
            }
        }
        else
        {
            btn.SetText(tempRefID.ToString());
        }

        if (extraOffset) btn.optionID = (floor.GetHashCode() + ri.GetHashCode()) * 2 + 1;
        else btn.optionID = (floor.GetHashCode() + ri.GetHashCode()) * 2;


        buttonsByID.Add(btn.optionID, btn);
        validatorsByID.Add(btn.optionID, btn.Validator);
        btn.Validate();
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
        //ValidateAll();
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
                case 9999:
                    this.gameObject.SetActive(false);
                    parent.destroyFloorDisplay();
                    //scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    
                    break;
                default: break;
            }
        }
    }

    RectTransform selfRect;

    

    private void readTXT(string path, TextMeshProUGUI box)
    {
        var sr = new StreamReader(Application.dataPath + "/" + path);
        var fileContents = sr.ReadToEnd();
        sr.Close();
        box.text = fileContents;
    }

    private Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        if (File.Exists(FilePath))
        {
            FileData = File.ReadAllBytes(FilePath);
            Tex2D = new Texture2D(2, 2);           // Create new "empty" texture
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                 // If data = readable -> return texture
        }
        return null;                     // Return null if load failed

    }

    Texture2D SpriteTexture = null;
    Sprite NewSprite;

    /*
    private void loadSprite(string path, Image image)
    {
        if (path == null || path.Length < 1) path = DataPath.portrait_default;
        SpriteTexture = LoadTexture(Application.dataPath + "/" + path);
        //Debug.Log("loadSprite " + Application.dataPath + "/" + path);
        NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), 100.0f);
        image.sprite = NewSprite;

        image.transform.rotation = Quaternion.identity;
        image.transform.Rotate(new Vector3(0, 0, scr_System_CampaignManager.current.Map.z_rotation));
    }*/

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Destroy(SpriteTexture);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerPress == eventData.rawPointerPress && eventData.button == PointerEventData.InputButton.Left)
        {
            this.gameObject.SetActive(false);
            parent.destroyFloorDisplay();
        }

        else if (eventData.pointerPress != eventData.rawPointerPress && eventData.button == PointerEventData.InputButton.Right)
        {
            this.gameObject.SetActive(false);
            parent.destroyFloorDisplay();
        }

        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");
    }


    public class ButtonValidator_MoveRoom : ButtonValidator, I_ButtonClickable
    {
        Room_Instance room { get { return scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef); } }
        scr_SelectableText text;
        Job playerJob { get { return scr_System_CampaignManager.current.Player.InteractionJob;} }

        int roomRef = -1;
        public ButtonValidator_MoveRoom(canvas_RoomDisplay parent, Room_Instance room, scr_SelectableText text) : base(parent)
        {
            this.roomRef = room.RefID;
            this.text = text;
        }


        public override bool IsButtonValid()
        {
            if (scr_System_CampaignManager.current.CurrentRoom == room)
            {
                this.text.Toggle(true, true);
            }
            else
            {
                this.text.Toggle(true, false);
            }

            IEnumerable<TaggedEdge<int, Door_Instance>> path = scr_System_CampaignManager.current.Map.Findpath(0, room.RefID);
            float i = 0f;
            if (path != null) foreach (TaggedEdge<int, Door_Instance> e in path) i += e.Tag.Cost;

            this.tooltip = "Room: "+room.DisplayName;
            if (room.Furnitures.Count > 0)
            {
                tooltip += "\nFurnitures: " + room.DisplayableFurnitureNames;
                //foreach (FurnitureInstance f in room.Furnitures) if (!f.noDisplay) tooltip += f.DisplayName + " ";
                //foreach (KeyValuePair<FurnitureBase, int> kvp in room.DisplayableFurnitures) tooltip += kvp.Key.displayName+(kvp.Value > 1?"x"+kvp.Value:"") + " ";
            }

            List<int> list = scr_System_CampaignManager.current.CharaInRoom(room.RefID);

            List<string> names = new List<string>();
            foreach (int ii in list) if (ii != 0) names.Add(scr_System_CampaignManager.current.FindInstanceByID(ii).FirstName);
            if (names.Count > 0)
            {
                tooltip += "\nCurrently in room: "+String.Join(" ", names);
            }

            this.tooltip += "\nTravel Cost: " + ((int)i).ToString() +" minutes";

            return true;
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
            //scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
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
            parent.parent.changeFloorDisplay(floor.refID);
        }
    }
}
