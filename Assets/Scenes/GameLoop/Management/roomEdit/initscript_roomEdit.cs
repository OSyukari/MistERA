using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

public class initscript_roomEdit : MonoBehaviour
{
    public scr_Canvas_Management parent;

    public RectTransform floorsList, roomsList, inventoryList, furnitureList;

    Floor_Instance currentFloor = null;
    Room_Instance currentRoom = null;
    public scr_HoverableText currentRoomOwners;
    public RectTransform currentRoomTagRect;
    public Floor_Instance CurrentFloor { get { return currentFloor; } }
    public Room_Instance CurrentRoom { get { return currentRoom; } }

    Manageable currentFaction = null;
    public Manageable CurrentFaction { get { return currentFaction; } }

    public Image picture;

    public scr_inputFieldLink input_name;

    public void OnNameInputSelect()
    {
        if (!input_name.self_inputfield.isFocused) return;
        if (currentRoom == null) return;
        if (currentRoom.displayNameOverwrite != "") input_name.self_inputfield.SetTextWithoutNotify(currentRoom.displayNameOverwrite);
    }
    public void OnNameInputDelect()
    {
        if (!input_name.self_inputfield.isFocused) return;
        if (currentRoom == null) return;
        input_name.self_inputfield.SetTextWithoutNotify(currentRoom.DisplayNameShort);
    }
    public void OnNameInputChange(string s)
    {
        if (!input_name.self_inputfield.isFocused) return;
        if (currentRoom == null) return;
        currentRoom.displayNameOverwrite = s;
        currentRoom.InvalidateFurnitureDependentCache();
        Debug.Log($"OnNameInputChange, {currentRoom.DisplayName}, {currentRoom.DisplayNameShort}");
        parent.ValidateAll();
    }

    public canvas_furniturePacking prefab_furnitureEditUI;
    public scr_roomBTN prefab_roomButton;
    public scr_SelectableText prefab_roomButtonlist;


    List<int> floorButtonHashes = new List<int>();
    List<int> roomButtonHashes = new List<int>();
    List<int> ownerButtonHashes = new List<int>();

    Coroutine floorLoadCO = null;

    /// <summary>
    /// Called each time the Room Edit tab is (re)shown - re-derives the starting room/floor rather than
    /// caching a one-shot init, since the player may have moved between visits. Prefers wherever the
    /// player physically is, if that's one of currentFaction's managed rooms, else falls back to the
    /// faction's MainExit.
    /// </summary>
    public void Initialize(Manageable m)
    {
        if (m == null) return;
       // if (currentFaction == m) return;
        currentFaction = m;

        var player = scr_System_CampaignManager.current.Player;
        Room_Instance playerRoom = player.CurrentRoom;
        Room_Instance startRoom = playerRoom != null && currentFaction.ManagedRooms.ContainsKey(playerRoom.RefID)
            ? playerRoom
            : currentFaction.MainExit;

        SelectRoom(startRoom);
        RefreshFloorsList();
        RefreshInventoryList();
        RefreshOwnersList();

        EditMode = false;
    }

    public void SelectRoom(Room_Instance room)
    {
        currentRoom = room;
        LoadRoomData();

        if (room != null && room.parentFloor != currentFloor) LoadFloor(room.parentFloor);
        RefreshFurnitureList();
    }


    void LoadRoomData()
    {
        Utility.DestroyAllChildrenFrom(currentRoomTagRect);

        if (currentRoom == null)
        {
            input_name.self_inputfield.SetTextWithoutNotify(" - ");
            currentRoomOwners.SetText(" - ");
        }
        else
        {
            input_name.self_inputfield.SetTextWithoutNotify(currentRoom.DisplayNameShort);
            currentRoomOwners.SetText(currentRoom.OwnerNames.Count > 0 ? String.Join(" ", currentRoom.OwnerNames) : " - ");

            foreach(var tag in currentRoom.roomTypeTags)
            {
                var rect = Instantiate(parent.prefab_text_link);
                rect.SetParent(currentRoomTagRect, false);
                var text = rect.GetComponent<scr_HoverableText>();
                text.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Left;
                text.SetText(tag, false, "", true);
            }
        }

    }



    public void LoadFloor(Floor_Instance floor)
    {
        if (floor == null || floor == currentFloor) { RefreshFloorsList(); return; }
        if (floorLoadCO != null) StopCoroutine(floorLoadCO);
        floorLoadCO = StartCoroutine(LoadFloorCoroutine(floor));
    }

    IEnumerator LoadFloorCoroutine(Floor_Instance floor)
    {
        currentFloor = floor;

        Utility.DestroyAllChildrenFrom(roomsList);
        Utility.DestroyAllChildrenFrom(picture.rectTransform, 1);
        parent.UnloadButton(roomButtonHashes);
        roomButtonHashes.Clear();

        if (scr_System_CentralControl.current.GetSprite(floor.FloorBase.imagePath, out var sprite))
        {
            picture.sprite = sprite;
        }
        else
        {
            Texture2D texture = null;
            yield return AssetsLoader.LoadTextureCoroutine(floor.FloorBase.imagePath, tex => texture = tex);
            picture.sprite = scr_System_CentralControl.current.MakeSprite(floor.FloorBase.imagePath, texture);
        }

        picture.rectTransform.sizeDelta = new Vector2(floor.FloorBase.floorWidth, floor.FloorBase.floorHeight) * floor.FloorBase.resize;

        foreach (Room_Instance ri in floor.rooms)
        {
            AddRoomButton(ri, picture.rectTransform, false);
            AddRoomButton(ri, roomsList, true);
        }

        RefreshFloorsList();
        floorLoadCO = null;
        parent.ValidateAll();
    }

    /// <summary>
    /// Mirrors canvas_RoomDisplay.ConvertOffset's floor branch - converts a room's native pixel-space
    /// offset into the picture RectTransform's local space, accounting for AnchorType and the picture's
    /// resize scale. No worldView case here since Room Edit never shows a world map.
    /// </summary>
    Vector2 ConvertOffset(float offsetX, float offsetY)
    {
        if (currentFloor == null) return new Vector2(offsetX, offsetY);
        Vector2 offset = currentFloor.FloorBase.AnchorType == FloorCoordinateAnchor.TopLeft
            ? new Vector2(offsetX - currentFloor.FloorBase.floorWidth / 2f, currentFloor.FloorBase.floorHeight / 2f - offsetY)
            : new Vector2(offsetX, offsetY);
        return offset * currentFloor.FloorBase.resize;
    }

    void AddRoomButton(Room_Instance ri, RectTransform parentRect, bool inList)
    {
        scr_SelectableText btn = null;
        scr_HoverableText ownerText = null;
        ButtonValidator validator = null;
        if (inList)
        {
            var r3 = Instantiate(prefab_btnwtooltip);
            r3.selfRect.SetParent(parentRect, false);
            btn = r3.button;
            ownerText = r3.text;
            //btn.SetTextPreInit(ri.DisplayName);
            validator = new Button_SelectRoom(this, ri, btn, ownerText, true);
        }
        else
        {
            scr_roomBTN r2 = Instantiate(prefab_roomButton);
            r2.SelfRect.SetParent(parentRect, false);
            r2.transform.rotation = Quaternion.identity;
            r2.SelfRect.anchoredPosition = ConvertOffset(ri.Base.offsetX, ri.Base.offsetY);
            r2.bgImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
            r2.sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            btn = r2.Button;
            validator = new Button_SelectRoom(this, ri, btn, ownerText);
        }

       // btn.SetTextPreInit(ri.DisplayNameShort);

        int hash = parent.AssertUniqueHashPublic(ri.GetHashCode() * 2 + (inList ? 1 : 0));
        parent.RegisterButton(hash, btn, validator);
        roomButtonHashes.Add(hash);
    }

    void RefreshFloorsList()
    {
        Utility.DestroyAllChildrenFrom(floorsList);
        parent.UnloadButton(floorButtonHashes);
        floorButtonHashes.Clear();

        foreach (var floor in currentFaction.ManagedFloors)
        {
            scr_SelectableText btn = Instantiate(prefab_roomButtonlist);
            btn.SelfRect.SetParent(floorsList, false);
            btn.SetTextPreInit(floor.displayName);

            int hash = parent.AssertUniqueHashPublic(floor.GetHashCode());
            var validator = new Button_SelectFloor(this, floor, btn);
            parent.RegisterButton(hash, btn, validator);
            floorButtonHashes.Add(hash);
        }
    }

    void RefreshInventoryList()
    {
        Utility.DestroyAllChildrenFrom(inventoryList);
        if (currentFaction == null || currentFaction.Inventory == null) return;

        foreach (var item in currentFaction.Inventory.Contents)
        {
            if (item.GetComp("ItemComponent_Furniture") == null) continue;

            var text = Instantiate(parent.prefab_text_link).GetComponent<scr_HoverableText>();
            text.SetText(item.DisplayName);
            text.SetExternalTooltip(item.Tooltip);
            text.transform.SetParent(inventoryList, false);
        }
    }

    void RefreshOwnersList()
    {
        Utility.DestroyAllChildrenFrom(list_owners);
        parent.UnloadButton(ownerButtonHashes);
        ownerButtonHashes.Clear();

        foreach (var chara in currentFaction.ManagedChara)
        {
            if (currentFaction.isPrisoner(chara.RefID)) continue;

            var r = Instantiate(prefab_btnwtooltip);
            r.selfRect.SetParent(list_owners, false);
            r.button.SetTextPreInit(chara.FirstName);

            int hash = parent.AssertUniqueHashPublic(chara.RefID.GetHashCode());
            var validator = new Button_ToggleRoomOwner(this, chara, r.button, r.text);
            parent.RegisterButton(hash, r.button, validator);
            ownerButtonHashes.Add(hash);
        }
    }

    void RefreshFurnitureList()
    {
        Utility.DestroyAllChildrenFrom(furnitureList);
        if (currentRoom == null) return;

        string multiples = LocalizeDictionary.QueryThenParse("ui_entry_multipleCount");
        foreach (var kvp in currentRoom.DisplayableFurnitures)
        {
            var text = Instantiate(parent.prefab_text_link).GetComponent<scr_HoverableText>();
            text.SetText(kvp.Value > 1 ? multiples.Replace("$item$", kvp.Key.DisplayName).Replace("$count$", kvp.Value.ToString()) : kvp.Key.DisplayName);
            text.SetExternalTooltip(LocalizeDictionary.QueryThenParse(kvp.Key.ID + "_tooltip", ""));
            text.transform.SetParent(furnitureList, false);
        }
    }

    public class Button_SelectFloor : ButtonValidator, I_ButtonClickable
    {
        initscript_roomEdit script;
        Floor_Instance floor;
        scr_SelectableText text;

        public Button_SelectFloor(initscript_roomEdit script, Floor_Instance floor, scr_SelectableText text) : base(script.parent)
        {
            this.script = script;
            this.floor = floor;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            text.Toggle(true, script.CurrentFloor == floor);
            return true;
        }

        public void OnClickButton()
        {
            script.LoadFloor(floor);
        }
    }

    public class Button_SelectRoom : ButtonValidator, I_ButtonClickable
    {
        initscript_roomEdit script;
        Room_Instance room;
        scr_SelectableText text;
        scr_HoverableText ownerText;
        bool fullname = false;

        public Button_SelectRoom(initscript_roomEdit script, Room_Instance room, scr_SelectableText text, scr_HoverableText ownerText = null, bool fullname = false) : base(script.parent)
        {
            this.script = script;
            this.room = room;
            this.text = text;
            this.ownerText = ownerText;
            this.fullname = fullname;
            this.text.AttachOnHoverEnter(OnPointerEnter);
        }

        public override bool IsButtonValid()
        {
            text.Toggle(true, script.CurrentRoom == room);
            text.SetText(fullname ? room.DisplayName : room.DisplayNameShort);
            if (ownerText != null) ownerText.SetText(room.OwnerNames.Count > 0 ? String.Join(" ", room.OwnerNames) : "");
            return true;
        }

        public void OnPointerEnter()
        {
            script.SelectRoom(room);
            parent.ValidateAll();
        }

        public void OnClickButton()
        {
            script.SelectRoom(room);
        }
    }

    public class Button_ToggleRoomOwner : ButtonValidator, I_ButtonClickable
    {
        initscript_roomEdit script;
        Character_Trainable chara;
        scr_SelectableText button;
        scr_HoverableText text;

        public Button_ToggleRoomOwner(initscript_roomEdit script, Character_Trainable chara, scr_SelectableText button, scr_HoverableText text) : base(script.parent)
        {
            this.script = script;
            this.chara = chara;
            this.button = button;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            // keep "rooms this member owns" text live every validate pass
            var owned = script.CurrentFaction?.GetOwnedRooms(chara);
            var names = new List<string>();
            if (owned != null)
                foreach (var refID in owned)
                {
                    var r = scr_System_CampaignManager.current.Map.GetRoomByRef(refID);
                    if (r != null) names.Add(r.DisplayNameShort);
                }
            text.SetText(names.Count > 0 ? String.Join(", ", names) : "");

            var room = script.CurrentRoom;
            if (room == null) { button.Toggle(true, false); tooltip = "no room selected"; return false; }

            var ownerRefs = script.CurrentFaction.RoomOwners(room.RefID);
            bool isOwner = ownerRefs.Contains(chara.RefID);
            button.Toggle(true, isOwner);
            tooltip = "";
            if (isOwner) return true;

            // room has no owner right now (e.g. its sole owner was just unassigned) but someone else
            // is still physically inside - most likely asleep/unconscious in the bed. Block assigning
            // a different chara until that occupant leaves, instead of trying to interrupt their action.
            if (ownerRefs.Count == 0)
            {
                foreach (var occupant in room.RoomChara)
                {
                    if (occupant != chara)
                    {
                        tooltip = "room is currently occupied";
                        return false;
                    }
                }
            }

            var existingOwners = new List<Character_Trainable>();
            foreach (var refID in ownerRefs)
            {
                var o = scr_System_CampaignManager.current.FindInstanceByID(refID);
                if (o != null) existingOwners.Add(o);
            }
            foreach (var owner in existingOwners)
            {
                var others = new List<Character_Trainable> { chara };
                foreach (var co in existingOwners) if (co != owner) others.Add(co);
                if (!owner.WouldAgreeToShareRoom(others))
                {
                    tooltip = owner.FirstName + " does not agree to share the room";
                    return false;
                }
            }
            return true;
        }

        public void OnClickButton()
        {
            var room = script.CurrentRoom;
            if (room == null) return;
            bool isOwner = script.CurrentFaction.RoomOwners(room.RefID).Contains(chara.RefID);
            if (isOwner) script.CurrentFaction.RemoveRoomOwnership(chara.RefID, room.RefID);
            else script.CurrentFaction.AddRoomOwnership(chara.RefID, room.RefID);
            script.LoadRoomData();
        }
    }

    // Edit room furniture button.
    // on click instantiate a 2nd level UI prefab_furnitureEditUI on current room
    public class Button_EditFurniture : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;

        public Button_EditFurniture(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            return parent.roomEditScript != null && parent.roomEditScript.CurrentRoom != null
                && parent.roomEditScript.prefab_furnitureEditUI != null;
        }

        public void OnClickButton()
        {
            canvas_furniturePacking cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.roomEditScript.prefab_furnitureEditUI).GetComponent<canvas_furniturePacking>();
            cvs.InitializeWithArgument(parent.roomEditScript.CurrentRoom);
        }
    }

    public class Button_ToggleOwnerEditOn : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_ToggleOwnerEditOn(scr_Canvas_Management parent) : base(parent) { this.parent = parent; }

        public override bool IsButtonValid()
        {
            var room = parent.roomEditScript?.CurrentRoom;
            if (room == null || !room.isRoomPrivate) { tooltip = "select a private room first"; return false; }
            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            parent.roomEditScript.EditMode = true;
        }
    }

    public class Button_ToggleOwnerEditOff : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_ToggleOwnerEditOff(scr_Canvas_Management parent) : base(parent) { this.parent = parent; }

        public override bool IsButtonValid() { return true; }

        public void OnClickButton()
        {
            parent.roomEditScript.EditMode = false;
        }
    }

    public CanvasGroup cgroup_editOwners_false;
    public CanvasGroup cgroup_editOwners_true;

    public btn_w_tooltip prefab_btnwtooltip;

    public RectTransform list_owners;

    bool _editMode = false;
    public bool EditMode
    {
        get
        {
            return _editMode;
        }
        set
        {
            _editMode = value;

            cgroup_editOwners_false.alpha = _editMode ? 0 : 1;
            cgroup_editOwners_false.interactable = _editMode ? false : true;
            cgroup_editOwners_false.blocksRaycasts = _editMode ? false : true;


            cgroup_editOwners_true.alpha = !_editMode ? 0 : 1;
            cgroup_editOwners_true.interactable = !_editMode ? false : true;
            cgroup_editOwners_true.blocksRaycasts = !_editMode ? false : true;
        }
    }
}
