using UnityEngine;

public class FishWaveAnimator : MonoBehaviour
{
    [SerializeField] private float speed = 2f;
    private MeshRenderer rend;
    private Material[] mats;
    private float phase;


    void Start()
    {
        rend = GetComponent<MeshRenderer>();
        if (rend != null){
            mats = rend.materials;
            Debug.Log("Materials: " + mats.Length);
        }
    }

    void Update()
    {
        if (mats == null) return;

        phase += Time.deltaTime * speed;

        foreach (var m in mats){
            m.SetFloat("_Phase", phase);
            Debug.Log("Phase: " + phase);
        }
    }
}
