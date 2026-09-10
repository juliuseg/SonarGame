using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyBulletController : MonoBehaviour
{
    public float spd = 10f;
    private float d;
    public void Shoot(Vector3 direction){
        GetComponent<Rigidbody>().linearVelocity = direction.normalized * spd;
        d = Time.time;
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

    private void Update()
    {
        if (Time.time > d + 5f)
        {
            Destroy(gameObject);
        }
    }
}
