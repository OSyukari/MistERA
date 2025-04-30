using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Runtime.Serialization;
using System.Linq;
using Newtonsoft.Json;


public interface I_IndexMergeable{
    public void MergeWith(I_IndexMergeable list);
}


[System.Serializable]

public class masterList : ISerializationCallbackReceiver
{
    /// <summary>
    /// serializer make masterlist <br/>
    /// foreach file in directory (search subdirectory) masterlist.mergewith(target) <br/>
    /// foreach element in list if 
    /// </summary>

    public void Initialize(){

        foreach (object l in List)
        {
            if (l is ISerializationCallbackReceiver) (l as ISerializationCallbackReceiver).OnAfterDeserialize();
        }

        foreach (object l in List){
            if (l is I_IndexHasID)  (l as I_IndexHasID).RegisterAllID();
            if (l is I_IndexHasTooltip) (l as I_IndexHasTooltip).RegisterAllTooltip();
        }

        foreach (object l in List)
        {
            if (l is I_NeedLateInitialize) (l as I_NeedLateInitialize).LateInitialize();
        }
    }


    public void OnAfterDeserialize()
    {
        /*
        foreach(var l in this.List){
            if (l == null) continue;
            var intf = l as ISerializationCallbackReceiver;
            if (intf != null) intf.OnAfterDeserialize();
        }*/
    }

    public void OnBeforeSerialize()
    {

    }

    protected ArrayList list = null;
    public ArrayList List
    {
        get
        {
            if (list == null)
            {
                list = new ArrayList();
                list.Add(Traits_Groups);
              //  list.Add(Skills);
                list.Add(Stats_Derived_Bases);
                list.Add(BodyPartBases);
                list.Add(Items);
              //  list.Add(COMs);
                list.Add(Floors);
                list.Add(MapPlans);

                list.Add(Status);
                list.Add(StatusEXs);
                list.Add(Sexperiences);
                list.Add(StatEXs);

            } 
            return list;
        }
    }

    public void InitializeLists()
    {
        this.Sexperiences = new Index_Sexperiences();
        
        this.Traits_Groups = new Traits_Group_Index();

        this.Stats_Derived_Bases = new Stats_Derived_Base_Index();
        this.StatEXs = new Stats_Derived_Extended_Index();
        this.Status = new Index_Status();
        this.Items = new Index_Item_Base();


        this.MapPlans = new Index_MapPlan();
       // this.COMs = new Index_COM();

        this.Character_Bases = new Character_Base_Index();
        this.BodyPartBases = new Index_BodyPartBase();
        this.Floors = new Index_Floor_Base();
        this.StatusEXs = new Index_StatusEx();

    }

    public Traits_Group_Index Traits_Groups = null;
   // public Skills_Index Skills = null;
    public Stats_Derived_Base_Index Stats_Derived_Bases = null;

    public Character_BaseID_Index Character_BaseIDs = null;
    public Index_BodyPartBase BodyPartBases = null;
    public Index_Item_Base Items = null;
    //public Index_COM COMs = null;
    public Index_Floor_Base Floors = null;
    public Index_MapPlan MapPlans = null;

    public Character_Base_Index Character_Bases = null;
    public Index_Status Status = null;
    public Index_StatusEx StatusEXs = null;
    public Index_Sexperiences Sexperiences = null;
    public Stats_Derived_Extended_Index StatEXs = null;


    public void MergeWith(masterList list)
    {
        for(int i = 0; i < this.List.Count; i++)
        {
            if (list.List[i] == null) continue;
            //if (this.List[i] == null) this.List[i] = 
            I_IndexMergeable a = this.List[i] as I_IndexMergeable;
            I_IndexMergeable b = list.List[i] as I_IndexMergeable;
            if (a != null && b != null) a.MergeWith(b); 
            else
            {
                Debug.LogError("Index Merge operation failed at index "+i);
            }
        }
    }
}