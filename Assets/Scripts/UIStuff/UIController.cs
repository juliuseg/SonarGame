using TMPro;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI oreText;

    Inventory _inventory;

    public void Init(Inventory inventory)
    {
        _inventory = inventory;
    }

    void Update()
    {
        if (_inventory == null || oreText == null)
            return;

        oreText.text = $"ore:\n{_inventory.Ore:0.#}";
    }
}
