using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PartDisp2D : MonoBehaviour
{
    private PartSim2D sim;
    public GameObject mesh;
    public GameObject[] particles;

    public void Init(PartSim2D sim)
    {
        this.sim = sim;
        particles = new GameObject[sim.particleCount];
        SpawnParticles();
    }

    void SpawnParticles()
    {
        for (int i=0; i < sim.particleCount; i++)
        {
            particles[i] = Instantiate(mesh, new Vector3(sim.positions[i].x, sim.positions[i].y, 0), Quaternion.identity);
        }
    }

    public void MoveParticles()
    {
        for (int i = 0; i < sim.particleCount; i++)
        {
            particles[i].transform.position = new Vector3(sim.positions[i].x, sim.positions[i].y, 0);
        }
    }
}
