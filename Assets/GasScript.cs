using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GasScript : MonoBehaviour
{
    public float orbitalZ;
    public float angularVelocity;
    public float radial;
    public int emissionPerSecond = 10;
    public float radius;
    public float multiplier;
    public float arc = 0.3333f;
    // Start is called before the first frame update
    void Start()
    {
        arc =  Random.Range(2, 9);
        angularVelocity = Random.Range(-4f, -64f);
        radius = Random.Range(10, 25f);
        AIData.gas.Add(this);
    }

    private void OnDestroy()
    {
        AIData.gas.Remove(this);
    }

    void Update()
    {
        GetComponent<Rigidbody2D>().angularDrag = 0;
        GetComponent<Rigidbody2D>().angularVelocity = angularVelocity * multiplier;
        var partSys = GetComponent<ParticleSystem>();
        var velo = partSys.velocityOverLifetime;
        velo.orbitalZ = orbitalZ * Mathf.Sqrt(multiplier);
        velo.radial = radial * multiplier;
        var emission = partSys.emission;
        var shape = partSys.shape;
        shape.radius = radius * multiplier;
        shape.arcSpread = 1 / arc;
        emission.rateOverTime = 0 * emissionPerSecond * Mathf.Sqrt(multiplier);
    }
}
