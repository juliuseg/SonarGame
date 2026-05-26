using UnityEngine;
using TMPro;
public class PlayerHealthController : MonoBehaviour
{
    public int health = 100;
    public TextMeshProUGUI healthText;
    public void TakeDamage(int damage)
    {
        health -= damage;
        // if (health <= 0) Destroy(gameObject);
        healthText.text = "Health:\n" + health;
    }

}
