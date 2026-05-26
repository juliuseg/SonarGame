using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyBulletController : MonoBehaviour
{
    public float spd = 10f;
    public void Shoot(Vector3 direction){
        GetComponent<Rigidbody>().linearVelocity = direction.normalized * spd;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("HIT PLAYER");
            other.GetComponentInParent<PlayerHealthController>().TakeDamage(1);
            Destroy(gameObject);
        }
    }
}
