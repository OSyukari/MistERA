using System.Collections.Generic;
using Newtonsoft.Json;

public class FurnitureBase
{
    public string ID = "";
    public string displayName = "";
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            return LocalizeDictionary.QueryThenParse(ID, displayName);
        }
    }
    // recipe
    public float furnitureSize = 0f;
    public List<Furniture_COMGiver> givesJob = new List<Furniture_COMGiver>();
    public bool noDisplay = false;
    [JsonIgnore] public bool isJobGiver { get { return this.givesJob.Count > 0; } }

    [JsonIgnore]
    public bool isValid
    {
        get
        {
            if (this.ID != "") return true;
            return false;
        }
    }

    public void OnAfterDeserialize()
    {

    }

    public void OnBeforeSerialize()
    {

    }

    public class Furniture_COMGiver
    {
        public List<string> comID = new List<string>();
        public List<string> comTags = new List<string>();
    }

}
