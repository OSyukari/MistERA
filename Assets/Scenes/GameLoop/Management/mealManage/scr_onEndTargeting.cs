using UnityEngine;
using UnityEngine.EventSystems;

public class scr_onEndTargeting : MonoBehaviour, IPointerClickHandler
{
    public scr_menu_mealadditives parentScript = null;
    public bool ignoreLMB = false;
    public void OnPointerClick(PointerEventData eventData)
    {
        if (ignoreLMB && eventData.button == PointerEventData.InputButton.Left) return;
        if (this.gameObject.activeInHierarchy && parentScript != null)
        {
            parentScript.Notify(9989);// = initScript_Expeditions.PartyEditUI.Neutral;
            //parentCanvas.ValidateAll();
        }
    }
}
