using System;
using System.Collections.Generic;
using System.Text;
using static LLMUtils;
using Newtonsoft.Json;
using System.Linq;

public partial class LLM_WorldState
{
    public class PossibleInteractions
    {
        public List<int> DoerRefs;
        public List<int> ReceiverRefs;
        public int MasterRef = -1;
        public Dictionary<string, Dictionary<string, SerializedAP>> possibleInteractions = new Dictionary<string, Dictionary<string, SerializedAP>>();
        public Dictionary<string, SerializedAP> FurnitureInteractions = new Dictionary<string, SerializedAP>();


        [JsonIgnore] public List<Character_Trainable> Doers = new List<Character_Trainable>();
        [JsonIgnore] public List<Character_Trainable> Receivers = new List<Character_Trainable>();
        [JsonIgnore] public Character_Trainable Master = null;

        // serialized actor summary strings, " - " when empty
        [JsonIgnore]
        public string InteractionCallName
        {
            get
            {
                List<string> doerNames = new List<string>();
                List<string> receiverNames = new List<string>();
                foreach (var i in Doers) doerNames.Add(ActorName(i));
                foreach (var i in Receivers) receiverNames.Add(ActorName(i));

                if (receiverNames.Count > 0)
                {
                    return $"possible interactions {String.Join(",", doerNames)} can do with {String.Join(",", receiverNames)}" + (Master == null ? "" : $" under order of {Master.FirstName}");
                }
                else
                {
                    return $"possible interactions {String.Join(",", doerNames)} can do" + (Master == null ? "" : $" under order of {Master.FirstName}");
                }
            }
        }

        static string ActorName(Character_Trainable c)
        {
            return c == null ? " - " : $"{c.FirstName}(refID {c.RefID})";
        }

        public PossibleInteractions()
        {

        }
        public PossibleInteractions(List<Character_Trainable> doers, List<Character_Trainable> receivers, Character_Trainable master = null)
        {
            if (doers != null) this.Doers = doers;
            if (receivers != null) this.Receivers = receivers;
            this.Master = master;

            this.DoerRefs = new List<int>(this.Doers.Select(c => c.RefID));
            this.ReceiverRefs = new List<int>(this.Receivers.Select(c => c.RefID));
            this.MasterRef = master == null ? -1 : master.RefID;
        }
        public PossibleInteractions(Character_Trainable doer, Character_Trainable receiver, Character_Trainable master = null)
        {
            this.Doers = doer == null ? new List<Character_Trainable>() : new List<Character_Trainable>() { doer };
            this.Receivers = receiver == null ? new List<Character_Trainable>() : new List<Character_Trainable>() { receiver };
            this.Master = master;

            this.DoerRefs = new List<int>(this.Doers.Select(c => c.RefID));
            this.ReceiverRefs = new List<int>(this.Receivers.Select(c => c.RefID));
            this.MasterRef = master == null ? -1 : master.RefID;

        }
    }
}
