using UnityEngine;

public class CrystalController : MonoBehaviour
{
    [SerializeField] GameObject Drill;
    public bool occupied;

    void Start()
    {
        if (occupied && TryGetComponent(out SpawnNodeController spawnNode))
            spawnNode.MarkEdited();
    }
    
    public void OnPlacementSucceeded()
    {
        Debug.Log("Placement Succeeded");
        Drill.SetActive(true);
        occupied = true;

        if (TryGetComponent(out SpawnNodeController spawnNode))
            spawnNode.MarkEdited();
    }

    public void ClearMiner()
    {
        occupied = false;

        if (Drill != null)
            Drill.SetActive(false);
    }
}
