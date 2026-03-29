using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class scr_box_sexRelation : MonoBehaviour
{

    public TextMeshProUGUI doer, receiver, com, isare;
    public scr_SelectableText removeButton;

    /*
    public void Initialize(Sex_Relation rel)
    {
        string doerText = "";
        string receiverText = "";
        if (rel.Doers.Count > 1) isare.text = "are";
        else isare.text = "is";

        foreach (int doerRef in rel.Doers) doerText += scr_System_CampaignManager.current.FindInstanceByID(doerRef).FirstName + "\n";

        foreach(int receiverRef in rel.Receivers) receiverText += scr_System_CampaignManager.current.FindInstanceByID(receiverRef).FirstName + "\n";

        com.text = scr_System_Serializer.current.index_COM.list.Find(x => x.ID == rel.comID).DisplayName(rel.Doers, rel.Receivers);
        doer.text = doerText;
        receiver.text = receiverText;
    }*/

    /// <summary>
    /// Parent must also take care of removeButton initializing
    /// </summary>
    /// <param name="rel"></param>
    public void Initialize(ActionPackage_Sex rel)
    {
        string doerText = "";
        string receiverText = "";
        if (rel.doer.Count > 1) isare.text = "are";
        else isare.text = "is";

        foreach (var doerRef in rel.doer) doerText += doerRef.FirstName + "\n";

        foreach (var receiverRef in rel.receiver) receiverText += receiverRef.FirstName + "\n";

        com.text = rel.targetCOM.DisplayName(rel.job, rel.doer, rel.receiver, true);
        doer.text = doerText;
        receiver.text = receiverText;
    }
}
