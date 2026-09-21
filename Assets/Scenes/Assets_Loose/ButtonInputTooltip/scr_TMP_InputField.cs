using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Same idea as scr_ScrollRect - subclasses a built-in UI type wholesale to replace one method,
/// instead of trying to coexist with the base implementation via a sibling component.
///
/// TMP_InputField.OnScroll (Library/PackageCache/com.unity.ugui/Runtime/TMP/TMP_InputField.cs:2414)
/// already forwards correctly for single-line fields. For multiline fields it checks whether the
/// content overflows the viewport (textComponent.preferredHeight vs textViewport.rect.height) -
/// if it doesn't, it just returns without forwarding, silently swallowing the scroll instead of
/// passing it up. This override reuses that same check: when the field can scroll internally, it
/// defers to the base behavior; when it can't, it forwards to the nearest ancestor IScrollHandler
/// itself instead of dropping the event.
/// </summary>
public class scr_TMP_InputField : TMP_InputField
{
    IScrollHandler ancestorScrollHandler;
    bool resolvedAncestor = false;

    void ResolveAncestor()
    {
        if (resolvedAncestor) return;
        resolvedAncestor = true;

        var allHandlers = GetComponentsInParent<IScrollHandler>();
       // Debug.Log($"scr_TMP_InputField [{name}]: ResolveAncestor found {allHandlers.Length} IScrollHandler(s) in parent chain: {string.Join(", ", System.Array.ConvertAll(allHandlers, h => (h as Component)?.name + "(" + h.GetType().Name + ")"))}");

        foreach (var handler in allHandlers)
        {
            if (ReferenceEquals(handler, this)) continue;
            ancestorScrollHandler = handler;
            break;
        }

      //  Debug.Log($"scr_TMP_InputField [{name}]: ancestorScrollHandler resolved to {(ancestorScrollHandler == null ? "null" : (ancestorScrollHandler as Component)?.name + "(" + ancestorScrollHandler.GetType().Name + ")")}");
    }

    public override void OnScroll(PointerEventData eventData)
    {
        ResolveAncestor();

        // Auto-expanding fields size their viewport to exactly fit their content, so
        // preferredHeight and viewport height land equal (or float-imprecision-close) in the
        // common case - only a strict, meaningfully-larger preferredHeight counts as real overflow.
        bool canScrollInternally = multiLine && textComponent.preferredHeight > textViewport.rect.height + 0.5f;
       // Debug.Log($"scr_TMP_InputField [{name}]: OnScroll delta={eventData.scrollDelta}, multiLine={multiLine}, preferredHeight={textComponent.preferredHeight}, viewportHeight={textViewport.rect.height}, canScrollInternally={canScrollInternally}, ancestor={(ancestorScrollHandler == null ? "null" : (ancestorScrollHandler as Component)?.name)}");

        if (!multiLine || canScrollInternally)
        {
          //  Debug.Log($"scr_TMP_InputField [{name}]: deferring to base.OnScroll");
            base.OnScroll(eventData);
            return;
        }

       // Debug.Log($"scr_TMP_InputField [{name}]: forwarding to ancestor");
        ancestorScrollHandler?.OnScroll(eventData);
    }
}
