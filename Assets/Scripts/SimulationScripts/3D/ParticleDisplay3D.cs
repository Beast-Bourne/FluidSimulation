using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticleDisplay3D : MonoBehaviour
{
    public Mesh mesh; // Set to the quad mesh in the inspector
    public Shader shader; // Set to the particle shader 'Particle2D' in the inspector
    public float scale;
    public Gradient colourMap;
    public int gradientResolution;
    public float densityDisplayMax;
    public float velocityDisplayMax;
    public bool useVelocityDisplay;

    Material material;
    ComputeBuffer argsBuffer;
    Bounds bounds;
    Texture2D gradientTexture;
    bool needsUpdate;

    // Creates a material from the 'shader' and sets the values of the position and velocity buffers
    // It then creates an argument buffer and the bounds to spawn the particles in
    public void Init(ParticleSimulator3D sim)
    {
        material = new Material(shader);
        material.SetBuffer("positions", sim.positionBuffer);
        material.SetBuffer("velocities", sim.velocityBuffer);
        material.SetBuffer("densities", sim.densityBuffer);

        argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.positionBuffer.count);
        bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
    }

    // Draws the particles in the scene
    void LateUpdate()
    {
        if (shader != null)
        {
            UpdateSettings();
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer); // the function responsible for drawing the particles
        }
    }

    // Updates any settings in the material when necessary
    void UpdateSettings()
    {
        if (needsUpdate)
        {
            needsUpdate = false;
            TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
            material.SetTexture("ColourMap", gradientTexture);
            material.SetFloat("densityMax", densityDisplayMax);
            material.SetFloat("scale", scale);
            material.SetFloat("velocityMax", velocityDisplayMax);
            material.SetInt("useVelocity", useVelocityDisplay ? 1 : 0);
        }
    }

    void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filter = FilterMode.Bilinear)
    {
        if (texture == null)
        {
            texture = new Texture2D(width, 1);
        }
        else if (texture.width != width)
        {
            texture.Reinitialize(width, 1);
        }

        if (gradient == null)
        {
            gradient = new Gradient();
            gradient.SetKeys(new GradientColorKey[] {new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1)},
                             new GradientAlphaKey[] {new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = filter;

        Color[] cols = new Color[width];
        for (int i = 0; i < cols.Length; i++)
        {
            float t = i/(cols.Length - 1.0f);
            cols[i] = gradient.Evaluate(t);
        }
        texture.SetPixels(cols);
        texture.Apply();
    }

    // Automatically called when the variables are changed in the editor
    private void OnValidate()
    {
        needsUpdate = true;
    }

    // Clears the memory of the argument buffer when the program is closed
    private void OnDestroy()
    {
        ComputeHelper.Release(argsBuffer);
    }
}
