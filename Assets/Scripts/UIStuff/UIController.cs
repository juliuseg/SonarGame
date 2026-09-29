using TMPro;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI oreText;

    IInventory _inventory;

    void Start()
    {
        _inventory = GameServices.EnsureInitialized().Resolve<IInventory>();
    }

    void Update()
    {
        if (_inventory == null || oreText == null)
            return;

        oreText.text = $"ore:\n{_inventory.Ore:0.#}";
    }
}
