using UnityEngine;
using UnityEngine.EventSystems;

public class scr_onEndEditTeam : MonoBehaviour, IPointerClickHandler
{
    public initScript_Expeditions parentScript;
    public scr_Canvas_Management parentCanvas;
    public void OnPointerClick(PointerEventData eventData)
    {
        if (this.gameObject.activeInHierarchy)
        {
            parentScript.CurrentMode = initScript_Expeditions.PartyEditUI.Neutral;
            parentCanvas.ValidateAll();
        }
    }


}
