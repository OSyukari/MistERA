using QuikGraph;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class canvas_RoomDisplay : scr_Menu, IPointerClickHandler
{

    public TMP_Text floorName;
    public RectTransform roomList;

    public IEnumerable<TaggedEdge<int, Door_Instance>> path = null;
    public int pathCost = 0;

    public Image picture;

    Floor_Instance floor = null;

    public scr_roomBTN prefab_roomButton;

    public RectTransform WorldList;
    public scr_SelectableText prefab_WorldButton;

    Dictionary<string, scr_factionBlock> factionBlocksByID = new Dictionary<string, scr_factionBlock>();
    string selectedWorldID = "";
    public string SelectedWorldID { get { return selectedWorldID; } }

    /// <summary>
    /// Non-null while the picture area is showing a world map (doors) instead of a floor (rooms). Mutually
    /// exclusive with `floor` - each loader coroutine clears the other.
    /// </summary>
    WorldPlan worldView = null;
    Floor_Instance floorBeforeWorldView = null;
    bool floorListInitialized = false;

    //scr_Panel_Map parent;

    protected List<int> currentFloorIDs = new List<int>();


    /// ANCHOR CONVERSION ///
    /// <summary>
    /// Converts a room/door offset (defined in the picture's native, unscaled pixel space) into the picture
    /// RectTransform's local space. The picture is resized (not scaled) by resize/worldSizeMult - see LoadFloorTex
    /// /LoadWorldTex - so button positions must be scaled by the same factor here to still land on the correct
    /// spot, while the buttons themselves (children of the picture) keep their own unscaled size.
    /// </summary>
    private Vector2 ConvertOffset(float offsetX, float offsetY)
    {
        if (worldView != null)
        {
            Vector2 offset = worldView.AnchorType == FloorCoordinateAnchor.TopLeft
                ? new Vector2(offsetX - worldView.worldWidth / 2f, worldView.worldHeight / 2f - offsetY)
                : new Vector2(offsetX, offsetY);
            return offset * worldView.worldSizeMult;
        }
        if (floor != null)
        {
            Vector2 offset = floor.FloorBase.AnchorType == FloorCoordinateAnchor.TopLeft
                ? new Vector2(offsetX - floor.FloorBase.floorWidth / 2f, floor.FloorBase.floorHeight / 2f - offsetY)
                : new Vector2(offsetX, offsetY);
            return offset * floor.FloorBase.resize;
        }
        return new Vector2(offsetX, offsetY);
    }
    /// ANCHOR CONVERSION ///

    private void addWorldDoor(scr_roomBTN prefab, RectTransform parent, WorldPlan world, WorldPlan.DoorConnection door)
    {
        scr_roomBTN r2 = Instantiate(prefab);
        r2.SelfRect.SetParent(parent, false);
        r2.SelfRect.anchoredPosition = ConvertOffset(door.offset_x, door.offset_y);
        r2.transform.rotation = Quaternion.identity;

        r2.bgImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        scr_SelectableText btn = r2.Button;

        btn.Initialize(this, new ButtonValidator_SelectWorldDoor(this, world, door, btn));

        if (!string.IsNullOrEmpty(door.childWorldID))
        {
            var childWorld = scr_System_Serializer.current.GetByNameOrID_WorldPlan(door.childWorldID);
            btn.SetText(childWorld != null ? LocalizeDictionary.QueryThenParse(childWorld.worldID) : door.childWorldID);
        }
        else
        {
            var faction = scr_System_CampaignManager.current.FindFactionByID(door.factionID);
            btn.SetText(faction != null ? faction.FactionDisplayName : door.factionID);
        }

        btn.optionID = (world.GetHashCode() + door.GetHashCode());
        buttonsByID.Add(btn.optionID, btn);
        validatorsByID.Add(btn.optionID, btn.Validator);
        btn.Validate();

        r2.sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        currentFloorIDs.Add(btn.optionID);
    }

    private void addExit(scr_roomBTN prefab, RectTransform parent, Floor_Base.FloorPlan_Exit exits, Floor_Instance targetFloor)
    {
        scr_roomBTN r2 = Instantiate(prefab);
        r2.SelfRect.SetParent(parent, false);
        /// ANCHOR CONVERSION ///
        r2.SelfRect.anchoredPosition = ConvertOffset(exits.offsetX, exits.offsetY);
        /// ANCHOR CONVERSION ///

        r2.bgImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        r2.transform.rotation = Quaternion.identity;

        scr_SelectableText btn = r2.Button;

        btn.Initialize(this, new ButtonValidator_ChangeFloor(this, targetFloor, btn));
        btn.SetText(targetFloor.displayName);

        btn.optionID = (floor.GetHashCode() + exits.GetHashCode());
        buttonsByID.Add(btn.optionID, btn);
        validatorsByID.Add(btn.optionID, btn.Validator);
        btn.Validate();

        r2.sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        currentFloorIDs.Add(btn.optionID);
    }

    private void addBtn(scr_roomBTN prefab, RectTransform parent, Room_Instance ri, bool extraOffset = false, bool displayCharaName = false, bool ignorePathToggle = false)
    {
        scr_roomBTN r2 = Instantiate(prefab);
        r2.SelfRect.SetParent(parent, false);
        /// ANCHOR CONVERSION ///
        r2.SelfRect.anchoredPosition = ConvertOffset(ri.Base.offsetX, ri.Base.offsetY);
        /// ANCHOR CONVERSION ///

        r2.transform.rotation = Quaternion.identity;


        scr_SelectableText btn = r2.Button;

        Floor_Instance parentFloor = scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(ri.RefID);
        int tempRefID = ri.RefID;
        if (parentFloor != null) tempRefID -= parentFloor.FloorCode;

        if (displayCharaName)
        {
            btn.Initialize(this, new ButtonValidator_MoveRoom(this, ri, btn, false, ignorePathToggle));
            var list = scr_System_CampaignManager.current.CharaInRoom(ri.RefID);
            List<string> names = new List<string>();
            foreach (var i in list)  if (i.RefID != 0) names.Add(i.FirstName);

            btn.SetText(tempRefID + " - " + ri.DisplayNameShort);
            r2.bgImage.color = scr_System_CentralControl.current.DisplaySetting.TextColor_transparent;

            var namesRect = Instantiate(prefab_text_standard);
            namesRect.SetParent(parent, false);
            namesRect.GetComponent<TMP_Text>().text = String.Join(" ", names);
        }
        else
        {
            btn.Initialize(this, new ButtonValidator_MoveRoom(this, ri, btn, true, ignorePathToggle));
            //btn.SetText(tempRefID.ToString());
            btn.SetText(ri.DisplayNameShort);
            r2.bgImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
            r2.sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
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

        // first, start from current room, and floor and faction, recursively build a or multiple list of everything to be built
        // the built data need to distinguish between faction with standalone button and faction built inside other faction
        var plan = BuildFactionDrawPlan(currentFaction.FactionOwnerRoot);
        foreach (var faction in plan.Keys)
            foreach (var w in faction.worldConnection) if (!trackedWorld.Contains(w)) trackedWorld.Add(w);

        // then, go through the list and build each faction
        foreach (var kvp in plan) if (kvp.Value == kvp.Key) BuildFaction(kvp.Key);
        foreach (var kvp in plan)
            if (kvp.Value != kvp.Key && factionBlocksByID.TryGetValue(kvp.Value.FactionOwnerRoot.ID, out var hostBlock))
                AddFloorButtons(hostBlock, kvp.Key);

        // only once every faction (including merged-in companions) has finished drawing into its block can we
        // tell what actually ended up under it. A block left with no floor at all (e.g. its host faction was
        // hidden-from-world-map and no visible companion merged into it) gets removed outright instead of
        // showing an empty rect; otherwise its title visibility is decided from the final child count (a
        // block hosting just one floor of its own but merged with a companion's floor ends up with 2+
        // children and should show its title, so this checks floorList.childCount rather than the host
        // faction's own ManagedFloors.Count).
        foreach (var key in new List<string>(factionBlocksByID.Keys))
        {
            var block = factionBlocksByID[key];
            if (block.floorList.childCount == 0)
            {
                factionBlocksByID.Remove(key);
                Destroy(block.gameObject);
            }
            else
            {
                block.factionTitle.gameObject.SetActive(block.floorList.childCount >= 2);
            }
        }

        InitWorldList();

        selectedWorldID = ResolveWorldID(currentFaction.FactionOwnerRoot, new HashSet<Manageable>());

        RefreshFactionVisibility();
    }

    protected void InitWorldList()
    {
        var worldIDs = new List<string>();
        foreach (var w in scr_System_CampaignManager.current.GetLoadedWorldPlans()) if (!worldIDs.Contains(w.worldID)) worldIDs.Add(w.worldID);
        foreach (var id in trackedWorld) if (!worldIDs.Contains(id)) worldIDs.Add(id);

        foreach (var worldID in worldIDs)
        {
            var world = scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
            if (world == null) continue;

            var btn = Instantiate(prefab_WorldButton);
            btn.transform.SetParent(WorldList, false);

            btn.Initialize(this, new ButtonValidator_SelectWorld(this, world, btn));
            btn.SetText(LocalizeDictionary.QueryThenParse(world.worldID));

            btn.optionID = world.worldID.GetHashCode();
            buttonsByID.Add(btn.optionID, btn);
            validatorsByID.Add(btn.optionID, btn.Validator);

            noDestroyList.Add(btn.optionID);
        }
    }

    protected void RefreshFactionVisibility()
    {
        WorldPlan world = selectedWorldID == "" ? null : scr_System_Serializer.current.GetByNameOrID_WorldPlan(selectedWorldID);
        foreach (var kvp in factionBlocksByID)
        {
            bool visible = world == null || world.initializeFactions.ContainsKey(kvp.Key);
            if (!visible && world != null)
            {
                var faction = scr_System_CampaignManager.current.FindFactionByID(kvp.Key);
                if (faction != null) visible = IsFactionInWorld(faction, world, new HashSet<Manageable>());
            }
            kvp.Value.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// True if faction (or any faction reachable through its factionConnection chain) belongs to world - used
    /// so a door-connected annex faction with no worldConnection of its own (e.g. ErAV_KiryuGumi_Filmstudio)
    /// doesn't get its block hidden once selectedWorldID resolves to a connected neighbor's world.
    /// </summary>
    private bool IsFactionInWorld(Manageable faction, WorldPlan world, HashSet<Manageable> visited)
    {
        if (faction == null || visited.Contains(faction)) return false;
        visited.Add(faction);
        if (world.initializeFactions.ContainsKey(faction.ID)) return true;
        foreach (var connected in faction.factionConnection)
            if (IsFactionInWorld(connected, world, visited)) return true;
        return false;
    }

    List<int> noDestroyList = new List<int>();




    List<string> trackedWorld = new List<string>();

    /// <summary>
    /// Discovers every faction reachable from startFaction (recursively through ConnectedFactions) and resolves
    /// each to a host faction whose block it should be drawn in (itself = standalone). Resolved from data alone
    /// (worldConnection/factionConnection) before anything is built, so the outcome is independent of build
    /// order/viewing direction - see ResolveHost.
    /// </summary>
    private Dictionary<Manageable, Manageable> BuildFactionDrawPlan(Manageable startFaction)
    {
        var discovered = new List<Manageable> { startFaction };
        var seen = new HashSet<Manageable> { startFaction };
        for (int i = 0; i < discovered.Count; i++)
        {
            foreach (var connect in discovered[i].ConnectedFactions)
                if (seen.Add(connect)) discovered.Add(connect);
        }

        var hosts = new Dictionary<Manageable, Manageable>();
        foreach (var faction in discovered) ResolveHost(faction, hosts, new HashSet<Manageable>());
        return hosts;
    }

    /// <summary>
    /// faction -> the faction whose block it should be drawn in (itself = standalone). Memoized into hosts so
    /// each faction is resolved once; visiting guards against a factionConnection cycle with no world-connected
    /// anchor in it. A subfaction (e.g. a mall shop tenant) always resolves to its Parent's host instead of
    /// falling into the "no anchor - stand alone" case below - its rooms are a per-room-owned subset of the
    /// Parent's own floor (see Manageable.AddToFaction/Room_Base.subfactionOwnerOverwrite), not a distinct
    /// floor of its own, so it must never independently draw a block (AddFloorButtons dedupes by floor
    /// identity as a backstop, but this keeps the floor's button attributed to the right block instead of
    /// racing on build order).
    /// </summary>
    private Manageable ResolveHost(Manageable faction, Dictionary<Manageable, Manageable> hosts, HashSet<Manageable> visiting)
    {
        if (hosts.TryGetValue(faction, out var resolved)) return resolved;

        if (faction is Manageable_Subfaction sub && sub.Parent != null)
        {
            var parentHost = ResolveHost(sub.Parent, hosts, visiting);
            hosts[faction] = parentHost;
            return parentHost;
        }

        if (faction.worldConnection.Count > 0 || !visiting.Add(faction))
        {
            hosts[faction] = faction;
            return faction;
        }

        foreach (var connected in faction.factionConnection)
        {
            var host = ResolveHost(connected, hosts, visiting);
            if (host.worldConnection.Count > 0)
            {
                hosts[faction] = host;
                return host;
            }
        }

        hosts[faction] = faction; // no world-connected anchor reachable - stand alone as a last resort
        return faction;
    }

    protected scr_factionBlock BuildFaction(I_IsJobGiver faction)
    {
        if (faction == null)
        {
           // Debug.LogError("initfaction error faction null");
            return null;
        }
        else if (faction.MainExit == null)
        {
            return null;
           // Debug.Log("initffactionblock " + faction.FactionDisplayName);
        }

        var block = Instantiate(prefab_FactionBlock);
        block.transform.SetParent(FactionList, false);
        block.factionTitle.text = faction.FactionDisplayName;

        factionBlocksByID[faction.FactionOwnerRoot.ID] = block;

        AddFloorButtons(block, faction);

        return block;
    }

    private void AddFloorButtons(scr_factionBlock block, I_IsJobGiver faction)
    {
        // a faction hidden from the world map is still tracked (discovered, host-resolved, contributes to
        // trackedWorld) but its own floors stay undrawn unless debug mode is on - matches the world-map door
        // pin visibility rule (canvas_FloorDisplay.cs addWorldDoor).
        if (faction is Manageable m && m.hiddenOnWorldMap && !scr_System_CampaignManager.current.DebugMode) return;

        foreach(var floor in faction.ManagedFloors)
        {
            // a floor can be jointly owned by a host faction and a subfaction tenant (per-room
            // subfactionOwnerOverwrite, e.g. a mall shop) - both list the same Floor_Instance in their own
            // ManagedFloors, so skip it here if some earlier faction in this pass already drew it.
            if (buttonsByID.ContainsKey(floor.GetHashCode())) continue;

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

    private void ClearDisplay()
    {
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
        Utility.DestroyAllChildrenFrom( pictureRect, 1);
    }

    IEnumerator LoadFloorTex(Floor_Instance floornew = null)
    {
        floor = floornew;
        worldView = null;

        ClearDisplay();

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

        var scale = floor.FloorBase.resize;
        // resize the picture's own rect instead of its transform scale, so child buttons (room/exit buttons
        // parented to picture.rectTransform) don't inherit the zoom - only their ConvertOffset position does.
        picture.rectTransform.sizeDelta = new Vector2(floor.FloorBase.floorWidth, floor.FloorBase.floorHeight) * scale;
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


        // --- AI GENERATED FLOOR LINE DRAW FUNCTION --- //

        cachedPath = null;
        connectionSegments = new List<(Vector2, Vector2, Color)>();
        connectionPosLookup = new Dictionary<int, Vector2>();
        foreach (var ri in floor.rooms)
        {
            /// ANCHOR CONVERSION ///
            connectionPosLookup[ri.RefID] = ConvertOffset(ri.Base.offsetX, ri.Base.offsetY);

            if (scr_System_CampaignManager.current.Map.floorDoorQuickSearch.ContainsKey(ri.RefID))
            {
                int connectedRef = scr_System_CampaignManager.current.Map.floorDoorQuickSearch[ri.RefID];
                Floor_Base.FloorPlan_Exit exit = floor.FloorBase.exits.Find(x => x.connectedRoom == ri.Base.ID);
                if (exit != null) connectionPosLookup[connectedRef] = ConvertOffset(exit.offsetX, exit.offsetY);
            }
            /// ANCHOR CONVERSION ///
        }

        connectionLines.SetSegments(connectionSegments);

        // --- END --- //

        ValidateAll();
    }

    public void LoadFloor(Floor_Instance floornew = null)
    {
        if (!floorListInitialized)
        {
            floorListInitialized = true;
            InitFloorList();
        }

        if (floornew == null) Debug.LogError("canvas_RoomDisplay ATTEMPTING TO DISPLAY NONEXISTENT ROOM");

        if (floornew != null && floornew.rooms.Count == 1)
        {
            Room_Instance onlyRoom = floornew.rooms[0];
            bool alreadyThere = scr_System_CampaignManager.current.CurrentRoom == onlyRoom;

            if (!alreadyThere)
            {
                // a floor with a single room has nothing worth showing as its own screen - walk there directly
                MoveToRoom(onlyRoom);
                return;
            }
            else if (floornew.ConnectedDoors.Count < 1)// floornew.FloorBase == null || string.IsNullOrEmpty(floornew.FloorBase.imagePath))
            {
                // already standing in this floor's only room and there's no image to show it with -
                // jump to the parent world map instead, since there's nowhere useful to go from here locally
                var owner = onlyRoom.FactionOwner?.FactionOwnerRoot;
                if (owner != null)
                {
                    var worldID = ResolveWorldID(owner, new HashSet<Manageable>());
                    var world = string.IsNullOrEmpty(worldID) ? null : scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
                    if (world != null && !string.IsNullOrEmpty(world.mapImagePath)) { OpenWorldMap(world); return; }
                }
            }
        }

        if (floor != floornew || worldView != null)
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

    /// <summary>
    /// Queues a path-to job for the player to the given room and closes this canvas, without ever
    /// rendering a floor screen for it. Shared by single-room LoadFloor redirects and room buttons.
    /// </summary>
    public void MoveToRoom(Room_Instance room)
    {
        if (room.RefID == scr_System_CampaignManager.current.CurrentRoom.RefID)
        {
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
            return;
        }

        var playerJob = scr_System_CampaignManager.current.Player.InteractionJob;
        ActionPackage_PathTo package = new ActionPackage_PathTo(playerJob, 0, room.RefID);
        playerJob.AddPackage(new List<ActionPackage>() { package });

        scr_System_CampaignManager.current.FreeUpdate();
        NotifyMove();
    }

    /// <summary>
    /// Switches the picture area from the floor view to a graphical world map (background image + faction door
    /// pins at world.doors offsets), reusing the same picture/room-list infrastructure as LoadFloor.
    /// </summary>
    public void OpenWorldMap(WorldPlan world)
    {
        if (worldView == null) floorBeforeWorldView = floor;

        if (CO != null)
        {
            StopCoroutine(CO);
            CO = null;
        }
        CO = StartCoroutine(LoadWorldTex(world));
    }

    List<WorldPlan> worldViewStack = new List<WorldPlan>();

    /// <summary>
    /// Drills into a child world reached via a door's childWorldID, remembering the current world so
    /// WorldMapBack() can return to it. Instantiates the child world's factions on first visit, mirroring
    /// the campaign_init_world bootstrap.
    /// </summary>
    public void EnterChildWorld(WorldPlan childWorld)
    {
        if (worldView != null) worldViewStack.Add(worldView);

        if (!scr_System_CampaignManager.current.currentWorldPlanIDs.Contains(childWorld.worldID))
        {
            scr_System_CampaignManager.current.Map.AddWorldTemplate(childWorld.worldID, false);
            scr_System_CampaignManager.current.currentWorldPlanIDs.Add(childWorld.worldID);
        }

        OpenWorldMap(childWorld);
    }

    /// <summary>
    /// Back navigation. A floor backs up to the world map of its owning faction's world, if any; a world
    /// backs up to whichever world it's a child of - preferring drill-down history if we got here via
    /// EnterChildWorld, otherwise falling back to a live scan of every WorldPlan def for a door whose
    /// childWorldID references it (parent/child is pure JSON, never saved, so this must work even when we
    /// jumped straight into a child world without drilling in through its parent). Closes the canvas
    /// entirely only when no parent context exists.
    /// </summary>
    public void WorldMapBack()
    {
        WorldPlan parentWorld = null;

        if (worldView != null)
        {
            if (worldViewStack.Count > 0)
            {
                parentWorld = worldViewStack[worldViewStack.Count - 1];
                worldViewStack.RemoveAt(worldViewStack.Count - 1);
            }
            else
            {
                parentWorld = FindWorldByChildWorldID(worldView.worldID);
            }
        }
        else if (floor != null && floor.rooms.Count > 0)
        {
            var owner = floor.rooms[0].FactionOwner?.FactionOwnerRoot;
            if (owner != null)
            {
                var worldID = ResolveWorldID(owner, new HashSet<Manageable>());
                var world = string.IsNullOrEmpty(worldID) ? null : scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
                if (world != null && !string.IsNullOrEmpty(world.mapImagePath)) parentWorld = world;
            }
        }

        if (parentWorld != null) OpenWorldMap(parentWorld);
        else Notify(-9999);
    }

    /// <summary>
    /// Resolves the world a faction belongs to via its cached worldConnection, falling back to whichever world
    /// a door-connected faction belongs to (searched recursively through factionConnection) for a faction with
    /// no world membership of its own - e.g. an on-demand-instantiated annex faction like ErAV_KiryuGumi_Filmstudio,
    /// door-connected to ErAV_KiryuGumi_Office but never listed in any WorldPlan.initializeFactions.
    /// </summary>
    private string ResolveWorldID(Manageable faction, HashSet<Manageable> visited)
    {
        if (faction == null || visited.Contains(faction)) return "";
        visited.Add(faction);
        if (faction.worldConnection.Count > 0) return faction.worldConnection[0];
        foreach (var connected in faction.factionConnection)
        {
            var found = ResolveWorldID(connected, visited);
            if (!string.IsNullOrEmpty(found)) return found;
        }
        return "";
    }

    private static WorldPlan FindWorldByChildWorldID(string worldID)
    {
        foreach (var w in scr_System_Serializer.current.MasterList.MapPlans.worldInit)
        {
            if (w.doors.Exists(d => d.childWorldID == worldID)) return w;
        }
        return null;
    }

    /// <summary>
    /// The door on the given world map that represents the player's current location (matched by the
    /// current room's owning root faction), used as the travel-time tooltip's distance origin.
    /// </summary>
    public WorldPlan.DoorConnection FindPlayerOriginDoor(WorldPlan world)
    {
        var faction = scr_System_CampaignManager.current.CurrentRoom.FactionOwner?.FactionOwnerRoot;
        if (faction == null) return null;

        // a subfaction (e.g. a mall shop) has no door of its own on the world map - its MainExit resolves to
        // its parent's room, so use whichever faction actually owns that room instead of the subfaction's own ID.
        var ownerID = faction.MainExit != null && faction.MainExit.FactionOwner != null
            ? faction.MainExit.FactionOwner.FactionOwnerRoot.ID : faction.ID;

        return world.doors.Find(d => d.factionID == ownerID);
    }

    /// <summary>
    /// Cost for the player to walk from their current room to their current faction's MainExit - the "leaving
    /// the building" segment of a cross-faction trip, so the world-map tooltip can show the same total a real
    /// move would actually cost instead of just the map-distance segment.
    /// </summary>
    public static float PlayerExitCost()
    {
        var faction = scr_System_CampaignManager.current.CurrentRoom.FactionOwner?.FactionOwnerRoot;
        if (faction == null || faction.MainExit == null || scr_System_CampaignManager.current.CurrentRoom == faction.MainExit) return 0f;

        var path = scr_System_CampaignManager.current.Map.Findpath(0, faction.MainExit.RefID);
        if (path == null) return 0f;

        float cost = 0f;
        foreach (var e in path) cost += e.Tag.Cost;
        return cost;
    }

    /// <summary>
    /// takes 2 door points, calc pixel distance, divide by travelDistancePerMinute to get map travel time, and
    /// add the cost of walking from the player's current room to their faction's exit door.
    /// </summary>
    public static string TravelTimeMinutesString(WorldPlan world, WorldPlan.DoorConnection origin, WorldPlan.DoorConnection target)
    {
        if (origin == null || target == null || world.travelDistancePerMinute <= 0f) return "-";
        float dist = Vector2.Distance(new Vector2(origin.offset_x, origin.offset_y), new Vector2(target.offset_x, target.offset_y));
        float mapCost = dist / world.travelDistancePerMinute;
        return Mathf.CeilToInt(mapCost + PlayerExitCost()).ToString();
    }

    /// <summary>
    /// Builds the shared room tooltip text (room name, furniture, items, occupants, activity state) with
    /// $time$ left as a literal placeholder for the caller to fill in - shared by room buttons and, for a
    /// world door that leads to a single-room floor, the world map door tooltip.
    /// </summary>
    public static string BuildRoomTooltipTemplate(Room_Instance room)
    {
        List<string> names = new List<string>();
        var list = scr_System_CampaignManager.current.CharaInRoom(room.RefID);
        foreach (var ii in list) if (ii.RefID != 0) names.Add(ii.FirstName);

        return LocalizeDictionary.QueryThenParse("ui_map_roomTooltip")
            .Replace("$room$", room.DisplayName)
            .Replace("$items$", room.Inventory.PrintContent(" ", true))
            .Replace("$furnitures$", room.DisplayableFurnitureNames)
            .Replace("$names$", names.Count > 0 ? String.Join(" ", names) : "-")
            .Replace("$roomActivityState$", room.ActivityStateString);
    }

    /// <summary>
    /// Lists the display names of every door inside a child world, for previewing its contents on hover
    /// before drilling in.
    /// </summary>
    public static string BuildChildWorldDoorListTooltip(WorldPlan childWorld)
    {
        List<string> names = new List<string>();
        foreach (var d in childWorld.doors)
        {
            if (string.IsNullOrEmpty(d.floorExitID) && string.IsNullOrEmpty(d.childWorldID)) continue;

            if (!string.IsNullOrEmpty(d.childWorldID))
            {
                var nested = scr_System_Serializer.current.GetByNameOrID_WorldPlan(d.childWorldID);
                names.Add(nested != null ? LocalizeDictionary.QueryThenParse(nested.worldID) : d.childWorldID);
            }
            else
            {
                var faction = scr_System_CampaignManager.current.FindFactionByID(d.factionID);
                names.Add(faction != null ? faction.FactionDisplayName : d.factionID);
            }
        }
        return names.Count > 0 ? String.Join(", ", names) : "-";
    }

    IEnumerator LoadWorldTex(WorldPlan worldnew)
    {
        worldView = worldnew;
        floor = null;

        ClearDisplay();

        if (scr_System_CentralControl.current.GetSprite(worldView.mapImagePath, out var sprite))
        {
            picture.sprite = sprite;
        }
        else
        {
            Texture2D texture = null;
            yield return AssetsLoader.LoadTextureCoroutine(worldView.mapImagePath, tex => texture = tex);
            picture.sprite = scr_System_CentralControl.current.MakeSprite(worldView.mapImagePath, texture);
        }

        var scale = worldView.worldSizeMult;
        // resize the picture's own rect instead of its transform scale, so child buttons (door pins parented to
        // picture.rectTransform) don't inherit the zoom - only their ConvertOffset position does.
        picture.rectTransform.sizeDelta = new Vector2(worldView.worldWidth, worldView.worldHeight) * scale;
        floorName.text = LocalizeDictionary.QueryThenParse(worldView.worldID);

        foreach (var door in worldView.doors)
        {
            if (string.IsNullOrEmpty(door.floorExitID) && string.IsNullOrEmpty(door.childWorldID)) continue;

            // a door leading to a floor is hidden while its owning faction hasn't been revealed to the
            // player yet (Manageable.hiddenOnWorldMap, set from MapPlan.isPublic at faction instantiation
            // and cleared the moment the player joins) - falls back to the door's own isPublic when the
            // faction hasn't been instantiated at all yet. Child-world doors are unaffected.
            if (!string.IsNullOrEmpty(door.floorExitID) && !scr_System_CampaignManager.current.DebugMode)
            {
                var owner = scr_System_CampaignManager.current.FindFactionByID(door.factionID);
                bool hidden = owner != null ? owner.hiddenOnWorldMap : !door.isPublic;
                if (hidden) continue;
            }

            addWorldDoor(prefab_roomButton, picture.rectTransform, worldView, door);
        }

        cachedPath = null;
        connectionSegments = new List<(Vector2, Vector2, Color)>();
        connectionLines.SetSegments(connectionSegments);

        // recenter the (potentially oversized) world map on the player's current location - falls back to
        // the map's own center when the player's faction has no door here (e.g. a disconnected sub-world).
        var originDoor = FindPlayerOriginDoor(worldView);
        picture.rectTransform.anchoredPosition = originDoor != null
            ? -ConvertOffset(originDoor.offset_x, originDoor.offset_y)
            : Vector2.zero;

        ValidateAll();
    }

    /// <summary>
    /// Called when a door pin on the world map is clicked: filters the faction list to this world and jumps
    /// straight to the clicked faction's first managed floor.
    /// </summary>
    public void ApplySelectedWorld(WorldPlan world, string focusFactionID = "")
    {
        selectedWorldID = world == null ? "" : world.worldID;
        RefreshFactionVisibility();

        Floor_Instance target = floorBeforeWorldView;
        if (focusFactionID != "")
        {
            var faction = scr_System_CampaignManager.current.FindFactionByID(focusFactionID);
            if (faction != null && faction.ManagedFloors.Count > 0) target = faction.ManagedFloors[0];
        }
        LoadFloor(target != null ? target : scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(scr_System_CampaignManager.current.CurrentRoom.RefID));
    }


    public override void ValidateAll()
    {
        base.ValidateAll();
        DrawConnections();
    }

    // --- AI GENERATED FLOOR LINE DRAW FUNCTION --- //
    public scr_mapLineRenderer connectionLines;
    List<(Vector2 a, Vector2 b, Color c)> connectionSegments = new List<(Vector2, Vector2, Color)>();
    Dictionary<int, Vector2> connectionPosLookup = new Dictionary<int, Vector2>();
    IEnumerable<TaggedEdge<int, Door_Instance>> cachedPath = null;

    private void DrawConnections()
    {

        // --- AI GENERATED FLOOR LINE DRAW FUNCTION --- //
        if (connectionLines == null || floor == null) return;
        if (path == cachedPath) return;

        connectionSegments.Clear();
        cachedPath = path;

        if (path != null)
        {
            foreach (var e in path)
            {
                if (connectionPosLookup.TryGetValue(e.Source, out var posA) && connectionPosLookup.TryGetValue(e.Target, out var posB))
                    connectionSegments.Add((posA, posB, Color.white));
            }
        }

        connectionLines.SetSegments(connectionSegments);

        // --- END --- //
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
        if (eventData.pointerPress == eventData.rawPointerPress && eventData.button == PointerEventData.InputButton.Left) WorldMapBack();
        else if (eventData.pointerPress != eventData.rawPointerPress && eventData.button == PointerEventData.InputButton.Right) WorldMapBack();

        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");
    }


    public class ButtonValidator_MoveRoom : ButtonValidator, I_ButtonClickable
    {
        Room_Instance room { get { return scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef); } }
        scr_SelectableText text;

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

            ttip = canvas_RoomDisplay.BuildRoomTooltipTemplate(room);
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
                this.tooltip = ttip.Replace("$time$", (parent.pathCost).ToString());
                return true;
            }
            else if (parent.path == null)
            {
                this.text.Toggle(true, false);
                this.tooltip = ttip.Replace("$time$", "-");
                return true;
            }
            else // parent path not null and current room not in path
            {
                return ignorePathToggle;
            }
        }

        public void OnClickButton()
        {
            parent.MoveToRoom(room);
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

    public class ButtonValidator_SelectWorld : ButtonValidator, I_ButtonClickable
    {
        WorldPlan world;
        scr_SelectableText text;
        new canvas_RoomDisplay parent;
        public ButtonValidator_SelectWorld(canvas_RoomDisplay parent, WorldPlan world, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.world = world;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (world.mapImagePath == "") return false;
            if (world.initializeFactions.Count < 2) return false;
            this.text.Toggle(true, parent.SelectedWorldID == world.worldID);
            return true;
        }

        public void OnClickButton()
        {
            parent.OpenWorldMap(world);
        }
    }

    public class ButtonValidator_SelectWorldDoor : ButtonValidator, I_ButtonClickable
    {
        WorldPlan world;
        WorldPlan.DoorConnection door;
        scr_SelectableText text;
        new canvas_RoomDisplay parent;
        public ButtonValidator_SelectWorldDoor(canvas_RoomDisplay parent, WorldPlan world, WorldPlan.DoorConnection door, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.world = world;
            this.door = door;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            // factionID/childWorldID not resolvable yet (e.g. a placeholder door added ahead of its target being defined)
            if (!string.IsNullOrEmpty(door.childWorldID))
            {
                var childWorld = scr_System_Serializer.current.GetByNameOrID_WorldPlan(door.childWorldID);
                this.tooltip = childWorld != null
                    ? LocalizeDictionary.QueryThenParse("ui_worldmap_childWorldTooltip")
                        .Replace("$list$", canvas_RoomDisplay.BuildChildWorldDoorListTooltip(childWorld))
                    : "";
                return childWorld != null;
            }

            var faction = scr_System_CampaignManager.current.FindFactionByID(door.factionID);
            var timeString = canvas_RoomDisplay.TravelTimeMinutesString(world, parent.FindPlayerOriginDoor(world), door);

            var current = scr_System_CampaignManager.current.CurrentRoom;
   
            if (current != null && faction != null && current.FactionOwner.getLocaleFaction == faction.getLocaleFaction)
            {
                text.Toggle(true, true);
            }
            else text.Toggle(true, false);
            

            Floor_Instance targetFloor = faction != null && faction.ManagedFloors.Count > 0 ? faction.ManagedFloors[0] : null;
            this.tooltip = targetFloor != null && targetFloor.rooms.Count == 1
                ? canvas_RoomDisplay.BuildRoomTooltipTemplate(targetFloor.rooms[0]).Replace("$time$", timeString)
                : LocalizeDictionary.QueryThenParse("ui_worldmap_doorTooltip").Replace("$time$", timeString);

            return faction != null;
        }

        public void OnClickButton()
        {
            if (!string.IsNullOrEmpty(door.childWorldID))
            {
                var childWorld = scr_System_Serializer.current.GetByNameOrID_WorldPlan(door.childWorldID);
                if (childWorld != null) parent.EnterChildWorld(childWorld);
            }
            else
            {
                parent.ApplySelectedWorld(world, door.factionID);
            }
        }
    }
}
