using UnityEngine;

public class EnemySnakeState : MonoBehaviour
{
    public float ShootCharge { get; set; }

    public static EnemySnakeState GetOrCreate(Transform from)
    {
        var state = from.GetComponentInParent<EnemySnakeState>();
        if (state != null) return state;

        Transform root = from;
        while (root.parent != null) root = root.parent;
        return root.GetComponent<EnemySnakeState>() ?? root.gameObject.AddComponent<EnemySnakeState>();
    }
}
