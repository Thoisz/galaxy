using TMPro;
using UnityEngine;

public class ItemDetailPanelUI : MonoBehaviour
{
    public TMP_Text xMoveText;
    public TMP_Text zMoveText;

    // One of these will be used:
    public TMP_Text descriptionText;           // plain label
    public TMP_InputField descriptionInput;    // or input field

    // Optional (hook these up if you have them)
    public GameObject leftArrow;
    public GameObject rightArrow;

    // The pager on this panel
    public ItemDetailPager pager;
}