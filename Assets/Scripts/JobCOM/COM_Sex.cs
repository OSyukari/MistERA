using System.Collections.Generic;

public class COM_Sex : COM
{
    new public bool COMRepeat = true;
    public bool ValidateActorLength(ref List<string> tooltip, int doerRefID, int receiverRefID, List<string> doerTag = null, List<string> receiverTag = null)
    {
        return ValidateActorLength(ref tooltip, new List<int>() { doerRefID }, new List<int>() { receiverRefID }, doerTag, receiverTag);     

    }

    public bool ValidateActorLength(ref List<string> tooltip, List<int> doerRefIDs, List<int> receiverRefIDs, List<string> doerTag = null, List<string> receiverTag = null)
    {
        //Debug.Log("validateActorLength doer[" + String.Join(" ", doerRefIDs) + "] receiver[" + String.Join(" ", receiverRefIDs) + "]");
        int minDoer = -1, maxReceiver = -1;
        float minDoerLength = 99f, maxReceiverLength = 0f;

        if (doerTag == null) doerTag = requirements.requirement.doerBodyTags;
        //Debug.LogError("ValidateActorLength before forLoop doers");
        foreach (int id in doerRefIDs)
        {
            Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(id);
            foreach(var tag in doerTag)
            {
                //Debug.LogError("ValidateActorLength before 1st doer "+c.FullName+" with tags "+tag+" and with body count "+c.Body.Body.Count+" and internals "+c.Body.Internals.Count);
                BodyInternal_Instance doer = c.Body.GetRandomInternalWithTag(tag);
                //Debug.LogError("ValidateActorLength after 1st doer, instance "+(doer == null?"null":doer.baseID));
                if (doer == null) continue;
                else if (doer.canFuck)
                {
                    if (doer.CurrentDepth < minDoerLength)
                    {
                        minDoerLength = doer.CurrentDepth;
                        minDoer = id;
                    }
                }
                else if (doer.canBePenetrated)
                {
                    if (doer.CurrentDepth > maxReceiverLength)
                    {
                        maxReceiverLength = doer.CurrentDepth;
                        maxReceiver = id;
                    }
                }
            }
        }

        if (receiverTag == null) receiverTag = requirements.requirement.receiverBodyTags;
        
        //Debug.LogError("ValidateActorLength before forLoop receivers");
        foreach (int id in receiverRefIDs)
        {
            foreach(var tag in receiverTag)
            {
                //Debug.LogError("ValidateActorLength before 1st receiver");
                BodyInternal_Instance receiver = scr_System_CampaignManager.current.FindInstanceByID(id).Body.GetRandomInternalWithTag(tag);
                //Debug.LogError("ValidateActorLength after 1st receiver");

                if (receiver == null) continue;
                else if (receiver.canFuck)
                {
                    if (receiver.CurrentDepth < minDoerLength)
                    {
                        minDoerLength = receiver.CurrentDepth;
                        minDoer = id;
                    }
                }
                else if (receiver.canBePenetrated)
                {
                    if (receiver.CurrentDepth > maxReceiverLength)
                    {
                        maxReceiverLength = receiver.CurrentDepth;
                        maxReceiver = id;
                    }
                }
            }
        }
        //Debug.Log("sexcom validateactors: doers[" + doerRefIDs.ToArray() + "] receivers [" + receiverRefIDs.ToArray()+"] comName ["+ID+"] validVariant["+index+"]");
        //Debug.Log("mindoer [" + minDoer + "] maxreceiver [" + maxReceiver + "]");
        if (minDoer != -1 && maxReceiver != -1)
        {
            if (minDoerLength <= maxReceiverLength) tooltip.Add( "Command invalid: penis length below requirement");
            return minDoerLength > maxReceiverLength;
        }

        return false;

    }

    public override string PreEvaluate (List<Character_Trainable> doers, List<Character_Trainable> receivers)
    {
        string tooltip = "";
        if (requirements.requirement.doerBodyTags.Count < 1) return "";
        if (requirements.requirement.receiverBodyTags.Count < 1) return "";
        if (!comTags.Contains("penetration")) return "";

        int variant = GetValidVariant(doers, receivers);

        float doer_depth = -1f;
        float doer_size = -1f;
        float receiver_depth = 99f;
        float receiver_size = 99f;

        foreach (Character_Trainable c in doers)
        {
            var ii = c.Body.GetRandomInternalWithTag(Utility.GetRandomElement(requirements.requirement.doerBodyTags));
            if (ii == null) continue;
            if (ii.CurrentDepth > doer_depth) doer_depth = ii.CurrentDepth;
            if (ii.CurrentSize > doer_size) doer_size = ii.CurrentSize;
        }

        if (variant < 0) return "";

        if ((receivers == null || receivers.Count < 1) && variants[variant].requirements.requirement.receiverCount == 0)
        {
            // masturbate
            foreach (Character_Trainable c in doers)
            {
                var ii = c.Body.GetRandomInternalWithTag(Utility.GetRandomElement(requirements.requirement.receiverBodyTags));
                if (ii == null) continue;
                if (ii.CurrentDepth < receiver_depth) receiver_depth = ii.CurrentDepth;
                if (ii.CurrentSize < receiver_size) receiver_size = ii.CurrentSize;
            }
        }
        else
        {
            foreach (Character_Trainable c in receivers)
            {
                var ii = c.Body.GetRandomInternalWithTag(Utility.GetRandomElement(requirements.requirement.receiverBodyTags));
                if (ii == null) continue;
                if (ii.CurrentDepth < receiver_depth) receiver_depth = ii.CurrentDepth;
                if (ii.CurrentSize < receiver_size) receiver_size = ii.CurrentSize;
            }
        }
        if (doer_depth == -1f && doer_size == -1f) return "";
        if (receiver_depth == 99f && receiver_size == 99f) return "";

        if (doer_depth >= receiver_depth)
        {
            string s = "Command [" + ID + "] Receiver might experience pain if penetration is too strong\n";
            if (!tooltip.Contains(s)) tooltip += s;
        }
        if (doer_size >= receiver_size)
        {
            string s = "Command [" + ID + "] Receiver might experience pain due to insufficient orifice size\n";
            if (!tooltip.Contains(s)) tooltip += s;
        }

        return tooltip;
    }

}