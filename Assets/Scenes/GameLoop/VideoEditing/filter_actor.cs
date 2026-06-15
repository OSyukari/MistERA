
using System.Collections.Generic;
using UnityEngine;



public class filter_actor : MonoBehaviour
{
    public RectTransform selfRect;

    public scr_HoverableText actorName;
    public scr_inputFieldLink overwriteName;

    public scr_SelectableText btn_removeAP_related_only, btn_removeAP_related_include;
    public List<ActionPackageRecords> ap_related_only = new List<ActionPackageRecords>();
    public List<ActionPackageRecords> ap_related_include = new List<ActionPackageRecords>();

    public scr_SelectableText btn_removeMessage_related_only, btn_removeMessage_related_include;
    public List<scr_videoEdit_message_record> msg_related_only = new List<scr_videoEdit_message_record>();
    public List<scr_videoEdit_message_record> msg_related_include = new List<scr_videoEdit_message_record>();

    public ActorRecord innerActorRecord = null;

    public void OnNameChange(string sss)
    {

    }


}
