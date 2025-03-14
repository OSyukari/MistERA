using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class scr_box_SexCOM_doerreceiver : MonoBehaviour
{
    public scr_SelectableText TargetBox, DoerBox, ReceiverBox;

    public TMP_Text M, B, C, V, A, W;

    public int refID = -1;
    private Character_Trainable chara = null;
    public Character_Trainable Chara { get { if (chara == null) { chara = scr_System_CampaignManager.current.FindInstanceByID(refID) ; } return chara; } }
    public void Refresh()
    {
        if (Chara == null) return;
      
        Chara.Stats.Sex_A.Draw(A);
        Chara.Stats.Sex_B.Draw(B);
        Chara.Stats.Sex_C.Draw(C);
        Chara.Stats.Sex_M.Draw(M);
        Chara.Stats.Sex_V.Draw(V);
        Chara.Stats.Sex_W.Draw(W);

    }

    public void SetChara(Character_Trainable chara)
    {
        this.chara = chara;
        this.refID = chara.RefID;
    }
    // script handled by scr_Panel_SexComTargets.cs's class ButtonValidator_COMChara
}
