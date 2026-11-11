using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>방 화면 플레이어 이름 칸: 우클릭 → 방장 메뉴. 좌클릭은 원래대로 팀 칸(부모 버튼)으로 넘김</summary>
public class PlayerItemClick : MonoBehaviour, IPointerClickHandler
{
    public Action<PointerEventData> onRightClick;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
        {
            onRightClick?.Invoke(e);
            return;
        }
        if (transform.parent != null)
            ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, e, ExecuteEvents.pointerClickHandler);
    }
}
