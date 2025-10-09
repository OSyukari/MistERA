using UnityEngine;
using UnityEngine.EventSystems;

public class scr_onEndEditTeam : MonoBehaviour, IPointerClickHandler
{
    public initScript_Expeditions parentScript = null;
    public scr_Canvas_Management parentCanvas = null;
    public bool ignoreLMB = false;
    public void OnPointerClick(PointerEventData eventData)
    {
        if (ignoreLMB && eventData.button == PointerEventData.InputButton.Left) return;
        if (this.gameObject.activeInHierarchy && parentScript != null && parentCanvas != null)
        {
            parentScript.CurrentMode = initScript_Expeditions.PartyEditUI.Neutral;
            parentCanvas.ValidateAll();
        }
    }


}
