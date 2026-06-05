using UnityEngine;

public class CrystalController : MonoBehaviour
{
    [SerializeField] GameObject Drill;
    public bool occupied;
    
    
    public void OnPlacementSucceeded()
    {
        Debug.Log("Placement Succeeded");
        Drill.SetActive(true);
        occupied = true;
    }
}
