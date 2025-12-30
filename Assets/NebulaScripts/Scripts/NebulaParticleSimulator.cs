using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class NebulaParticleSimulator : MonoBehaviour
{
    public event System.Action SimulationStepFinished;

    [Header("References")]
    public NebulaParticleSpawner spawner;
    public NebulaParticleDisplay display;
    public OctreeManager octreeManager;
    public ComputeShader compute;

    // buffers for the compute shader
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    public ComputeBuffer InternalEnergyBuffer { get; private set; }
    ComputeBuffer ResultantForceBuffer;
    ComputeBuffer predictedPositionBuffer;
    ComputeBuffer OctreeBuffer;
    ComputeBuffer SpatialHashes;
    ComputeBuffer debugBuffer;
    ComputeBuffer smoothingRadiiBuffer;
    ComputeBuffer pressureCorrectionBuffer;

    // spatial hash buffers and sorters
    ComputeBuffer SpatialDataBuffer;
    ComputeBuffer SpatialOffsetsBuffer;
    BufferSorter sorter;

    [Header("Simulation Settings")]
    public bool fixedTimeStep;
    public float timeScale;
    public float damping;
    public float pressureMultiplier;
    public float viscocityMultiplier;
    public float gasConstant;
    public float adiabaticIndex;
    public bool useXSPH;
    public bool isPaused;

    [Header("Smoothing Radius Settings")]
    public float minFactor;
    public float spatialStage1Size;
    public float spatialStage2Size;
    public float spatialStage3Size;
    public int desiredNeighbors;
    public bool useDynamicSmoothingRadius;

    [Header("Gravity Settings")]
    public float gravity;
    public float particleMass;
    public float barnesHutAccuracyThreshold;
    public float softeningLength;

    [Header("Energy Settings")]
    public float InitialTemperature;
    public float BoltzmannConstant;
    public float ProtonMass;
    public float MeanMolecularWeight;
    public float coolingLambda;
    public float coolingAlpha;
    public int coolingSubcycles;
    public float viscocityAlpha;
    public float viscocityBeta;
    public float viscocityEpsilon;
    public float conductionCoefficient;

    [Header("Data Collection Settings")]
    public bool enableDataCollection;
    public float TimeBetweenSamples;
    public float TotalSampleTime;
    public string logFileName;

    // kernel IDs for the compute shader
    const int UpdatePredictionsKernel = 0;
    const int gridHashKernel = 1;
    const int smoothingRadiusKernel = 2;
    const int gravityKernel = 3;
    const int pressureCorrectionKernel = 4;
    const int pressureForceKernel = 5;
    const int internalEnergyKernel = 6;
    const int viscosityKernel = 7;
    const int updatePositionKernel = 8;

    // other
    bool isPausedNextFrame;
    NebulaParticleSpawner.ParticleSpawnData spawnData;
    public int particleCount { get; private set; }
    float timeElapsed;
    float totalTimeElapsed;
    List<string> logData = new List<string>();

    // Initialisation. Gets the spawn data from the spawner and sets the initial buffer data before telling the display to initialise
    private void Start()
    {
        spawnData = spawner.GetSpawnData(adiabaticIndex, InitialTemperature, BoltzmannConstant, ProtonMass, MeanMolecularWeight);
        particleCount = spawnData.positions.Length;

        smoothingRadiiBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        positionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        OctreeBuffer = ComputeHelper.CreateStructuredBuffer<OctreeNode>(octreeManager.NumOfNodes);
        SpatialHashes = ComputeHelper.CreateStructuredBuffer<uint>(octreeManager.NumBottomLayerNodes * (int)octreeManager.NumOfHashesPerLeafNode); // need to make this work with the octree sizes
        InternalEnergyBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        debugBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        ResultantForceBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        pressureCorrectionBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        SpatialDataBuffer = ComputeHelper.CreateStructuredBuffer<SpatialData>(particleCount);
        SpatialOffsetsBuffer = ComputeHelper.CreateStructuredBuffer<uint3>(particleCount);
        SetInitialBufferData(spawnData);

        // tell the computer shader which kernels have access to which buffers
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", UpdatePredictionsKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", updatePositionKernel, gridHashKernel, pressureForceKernel, UpdatePredictionsKernel, viscosityKernel, gravityKernel, internalEnergyKernel, smoothingRadiusKernel, pressureCorrectionKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", UpdatePredictionsKernel, updatePositionKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", pressureForceKernel, viscosityKernel, internalEnergyKernel, updatePositionKernel, pressureCorrectionKernel, smoothingRadiusKernel);
        ComputeHelper.SetBuffer(compute, OctreeBuffer, "Octree", gravityKernel);
        ComputeHelper.SetBuffer(compute, SpatialHashes, "SpatialHashes", gravityKernel);
        ComputeHelper.SetBuffer(compute, InternalEnergyBuffer, "InternalEnergies", pressureForceKernel, internalEnergyKernel, viscosityKernel, pressureCorrectionKernel);
        ComputeHelper.SetBuffer(compute, debugBuffer, "DebugBuffer", smoothingRadiusKernel, pressureCorrectionKernel);
        ComputeHelper.SetBuffer(compute, ResultantForceBuffer, "ResultantForces", updatePositionKernel, pressureForceKernel, viscosityKernel, gravityKernel, UpdatePredictionsKernel, gravityKernel);
        ComputeHelper.SetBuffer(compute, smoothingRadiiBuffer, "SmoothingRadii", pressureForceKernel, viscosityKernel, internalEnergyKernel, updatePositionKernel, smoothingRadiusKernel, pressureCorrectionKernel);
        ComputeHelper.SetBuffer(compute, SpatialDataBuffer, "SpatialDataBuffer", gridHashKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel, updatePositionKernel, smoothingRadiusKernel, pressureCorrectionKernel);
        ComputeHelper.SetBuffer(compute, SpatialOffsetsBuffer, "SpatialOffsetDataBuffer", gridHashKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel, updatePositionKernel, smoothingRadiusKernel, pressureCorrectionKernel);
        ComputeHelper.SetBuffer(compute, pressureCorrectionBuffer, "PressureCorrections", pressureCorrectionKernel, pressureForceKernel, internalEnergyKernel);
        compute.SetInt("numParticles", particleCount);

        sorter = new();
        sorter.SetBuffers(SpatialDataBuffer, SpatialOffsetsBuffer);

        octreeManager.SetBuffers(OctreeBuffer, SpatialHashes, spatialStage1Size, SpatialDataBuffer, SpatialOffsetsBuffer, particleCount, positionBuffer, particleMass);

        display.Init(this);
    }

    // Runs the simulation frame by frame
    private void Update()
    {
        if (!fixedTimeStep)
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (isPausedNextFrame)
        {
            isPaused = true;
            isPausedNextFrame = false;
        }

        HandleUserImput();

        if (totalTimeElapsed < TotalSampleTime && enableDataCollection)
        {
            SampleFrameRate(Time.deltaTime);
        }
        else if (enableDataCollection && totalTimeElapsed >= TotalSampleTime)
        {
            enableDataCollection = false;
            Debug.Log("Finished Data Collection");
        }
    }

    // Runs the simulation step in fixed time intervals if the fixedTimeStep variable is set to true
    private void FixedUpdate()
    {
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime * timeScale);
        }
    }

    // updates the compute settings and runs the simulation step. Flags an event to say the simulation step has finished
    void RunSimulationFrame(float frameTime)
    {
        if (isPaused) return;
        if (Time.frameCount < 10) return; // skip first few frames to avoid a disproportionate delta time

        UpdateComputeSettings(frameTime);
        RunSimulationStep();
        SimulationStepFinished?.Invoke(); // the '?' is a null-conditional operator (won't run the event if there is no subscribed action)
    }

    // Dispatches the compute shader to run the simulation step
    void RunSimulationStep()
    {
        ComputeHelper.Dispatch(compute, particleCount, UpdatePredictionsKernel); // first update the predicted postions
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel); // then calculate the spatial hashes and indices
        sorter.SortAndCalcOffsets(); // sort the indices and calculate the offsets
        octreeManager.UpdateOctree(); // update the octree mass values
        ComputeHelper.Dispatch(compute, particleCount, smoothingRadiusKernel); // calculate the smoothing radii for each particle
        ComputeHelper.Dispatch(compute, particleCount, gravityKernel); // apply gravity between particles
        //ComputeHelper.Dispatch(compute, particleCount, densityCalculationKernel); // calculate the density at each particle
        ComputeHelper.Dispatch(compute, particleCount, internalEnergyKernel); // update the internal energies of the particles
        ComputeHelper.Dispatch(compute, particleCount, pressureCorrectionKernel); // calculate pressure corrections for smoothing radii changes
        ComputeHelper.Dispatch(compute, particleCount, pressureForceKernel); // calculate and apply pressure forces
        ComputeHelper.Dispatch(compute, particleCount, viscosityKernel); // calculate and apply viscocity forces
        ComputeHelper.Dispatch(compute, particleCount, updatePositionKernel); // finally update the particle positions and velocities

    }

    // Updates the compute settings for the simulation that are intended to be changeable during runtime
    void UpdateComputeSettings(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("particleMass", particleMass);
        compute.SetFloat("BarnesHutTheta", barnesHutAccuracyThreshold);
        compute.SetFloat("softeningLength", softeningLength);
        compute.SetVector("boundSize", new Vector3(octreeManager.BoundSize, octreeManager.BoundSize, octreeManager.BoundSize));
        compute.SetFloat("damping", damping);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetBool("useXSPH", useXSPH);
        compute.SetFloat("viscocityMultiplier", viscocityMultiplier);
        compute.SetFloat("gasConstant", gasConstant);
        compute.SetFloat("adiabaticIndex", adiabaticIndex);
        compute.SetInt("targetNeighborCount", desiredNeighbors);
        compute.SetBool("useDynamicSmoothingRadius", useDynamicSmoothingRadius);

        compute.SetFloat("protonMass", ProtonMass);
        compute.SetFloat("meanMolecularWeight", MeanMolecularWeight);
        compute.SetFloat("boltzmannConstant", BoltzmannConstant);
        compute.SetFloat("coolingLambda", coolingLambda);
        compute.SetFloat("coolingAlpha", coolingAlpha);
        compute.SetInt("subcycles", coolingSubcycles);
        compute.SetFloat("viscAlpha", viscocityAlpha);
        compute.SetFloat("viscBeta", viscocityBeta);
        compute.SetFloat("viscEpsilon", viscocityEpsilon);
        compute.SetFloat("conductionCoefficient", conductionCoefficient);

        compute.SetFloat("stage1Size", spatialStage1Size);
        compute.SetFloat("stage2Size", spatialStage2Size);
        compute.SetFloat("stage3Size", spatialStage3Size);
        compute.SetFloat("minSizeFactor", minFactor);

        compute.SetFloat("Pow2Factor", 6 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 4)));
        compute.SetFloat("Pow2DerivativeFactor", 12 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 4)));
        compute.SetFloat("Pow3Factor", 10 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 5)));
        compute.SetFloat("Pow3DerivativeFactor", 30 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 5)));
        compute.SetFloat("PolynomialPow6Factor", 4 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 8)));

        compute.SetFloat("CubicSplineFactor", 3 / (4*spatialStage1Size));

        //ShowDebugData();
        //DebugSmoothingRadii();
    }

    // Sets the initial buffer data for the simulation
    void SetInitialBufferData(NebulaParticleSpawner.ParticleSpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.positions.Length]; // This prevents the modification of the inital spawn data
        float[] allSmooths = new float[spawnData.positions.Length];
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        for (int i = 0; i < allSmooths.Length; i++)
        {
            allSmooths[i] = spatialStage1Size;
        }

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
        InternalEnergyBuffer.SetData(spawnData.energies);
        smoothingRadiiBuffer.SetData(allSmooths);
    }

    // Handles user input to pause the simulation, reset it, or run a single frame
    void HandleUserImput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            isPaused = true;
            SetInitialBufferData(spawnData);
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            isPaused = false;
            isPausedNextFrame = true;
        }
    }

    void SampleFrameRate(float deltaTime)
    {
        totalTimeElapsed += deltaTime;
        timeElapsed += deltaTime;

        if (timeElapsed >= TimeBetweenSamples)
        {
            float fps = 1.0f / deltaTime;
            logData.Add($"{totalTimeElapsed:F2},{fps:F2}");
            timeElapsed = 0.0f;
        }
    }

    void DebugSmoothingRadii()
    {
        float[] radii = new float[particleCount];
        smoothingRadiiBuffer.GetData(radii);

        Dictionary<float, int> radiusCounts = new Dictionary<float, int>();

        
        for (int i = 0; i < radii.Length; i++)
        {
            float radius = radii[i];
            if (radiusCounts.ContainsKey(radius))
            {
                radiusCounts[radius]++;
            }
            else
            {
                radiusCounts[radius] = 1;
            }
        }

        int count = radiusCounts.ContainsKey(1.0f) ? radiusCounts[1.0f] : 0;
        Debug.Log($"Radius: {1.0f}, Count: {count}");
    }
    void ShowDebugData()
    {
        float2[] debugData = new float2[particleCount];
        debugBuffer.GetData(debugData);
        Debug.Log("Omega: " + debugData[200].x + "  PressureTerm: " + debugData[200].y);
    }

    private void OnApplicationQuit()
    {
        if (!enableDataCollection) return;

        string path = Path.Combine("C:/My_Storage/Python_Stuff/FluidSimDataLogs/TextData", logFileName);
        if (File.Exists(path)) { Debug.Log("Log attempted for file name that already exists"); return; }
        File.WriteAllLines(path, logData);
        Debug.Log($"Logged Frame Data to path: {path}");
    }

    // Clears the memory of all the buffers from the compute shader when the program is closed
    private void OnDestroy()
    {
        ComputeHelper.Release(positionBuffer, velocityBuffer, densityBuffer, predictedPositionBuffer,
            OctreeBuffer, SpatialHashes, InternalEnergyBuffer, debugBuffer, pressureCorrectionBuffer, 
            ResultantForceBuffer, smoothingRadiiBuffer, SpatialDataBuffer, SpatialOffsetsBuffer);
    }
}

public struct SpatialData
{
    uint index1;
    uint hash1;
    uint key1;

    uint index2;
    uint hash2;
    uint key2;

    uint index3;
    uint hash3;
    uint key3;
}