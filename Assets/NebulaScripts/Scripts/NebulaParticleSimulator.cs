using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine;

public class NebulaParticleSimulator : MonoBehaviour
{
    [Header("References")]
    public NebulaParticleSpawner spawner;
    public NebulaParticleDisplay display;
    //public OctreeManager octreeManager;
    public ComputeShader compute;

    // buffers for the compute shader
    public ComputeBuffer particleBuffer { get; private set; }
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer renderBuffer1 { get; private set; }
    public ComputeBuffer renderBuffer2 { get; private set; }
    ComputeBuffer entropyDataBuffer;
    ComputeBuffer ResultantForceBuffer;
    ComputeBuffer gravityForceBuffer;
    ComputeBuffer gravityCorrectionBuffer;
    ComputeBuffer deltaTimeBuffer;
    ComputeBuffer globalDeltaTimeBuffer;
    //ComputeBuffer OctreeBuffer;
    //ComputeBuffer SpatialHashes;
    ComputeBuffer mortonKeyBuffer;
    ComputeBuffer newOctreeBuffer;
    ComputeBuffer debugBuffer;

    // spatial hash buffers and sorters
    ComputeBuffer SpatialDataBuffer;
    ComputeBuffer SpatialOffsetsBuffer;
    BufferSorter sorter;

    // reduction managers
    TimestepManager timestepManager;
    GravityReductionManager gravityReductionManager;
    OctreeScript octreeReductionManager;

    [Header("Simulation Settings")]
    public float CFLScale;
    public float damping;
    public float pressureMultiplier;
    public float adiabaticIndex;
    public bool isPaused;

    [Header("Smoothing Radius Settings")]
    public float minFactor;
    public float spatialStage1Size;
    public float spatialStage2Size;
    public float spatialStage3Size;

    [Header("Gravity Settings")]
    public float gravity;
    public float particleMass;
    public float barnesHutAccuracyThreshold;
    public float softeningLength;

    [Header("Entropy Settings")]
    public float InitialTemperature;
    public float BoltzmannConstant;
    public float ProtonMass;
    public float coolingLambda;
    public float coolingAlpha;
    public int coolingSubcycles;
    public float conductionCoefficient;

    [Header("Viscosity Settings")]
    public float viscocityMultiplier;
    public float alphaMax;
    public float alphaMin;
    public float viscosityEpsilon;

    [Header("Fusion Settings")]
    public float temperatureThreshold;
    public float densityThreshold;
    public float rateCoefficient;
    public float energyPerUnitMass;

    [Header("Data Collection Settings")]
    public bool enableDataCollection;
    public float TimeBetweenSamples;
    public float TotalSampleTime;
    public string logFileName;
    private bool canWriteLog = false;

    // kernel IDs for the compute shader
    const int UpdatePredictionsKernel = 0;
    const int gridHashKernel = 1;
    const int smoothingRadiusKernel = 2;
    const int correctionTermsKernel = 3;
    const int deltaTimeKernel = 4;
    const int fusionKernel = 5;
    const int neighbourDependentPropertiesKernel = 6;
    const int gravityKernel = 7;
    const int updatePositionKernel = 8;
    const int renderKernel = 9;
    const int initialiseEntropyKernel = 10;

    // other
    NebulaParticleSpawner.ParticleSpawnData spawnData;
    public int particleCount { get; private set; }
    float timeElapsed;
    float totalTimeElapsed;
    List<string> logData = new List<string>();
    bool useRender1 = true;

    // Initialisation. Gets the spawn data from the spawner and sets the initial buffer data before telling the display to initialise
    private void Start()
    {
        spawnData = spawner.GetSpawnData();
        particleCount = spawnData.positions.Length;
        particleBuffer = ComputeHelper.CreateStructuredBuffer<ParticleData>(particleCount);
        positionBuffer = ComputeHelper.CreateStructuredBuffer<float4>(particleCount);
        entropyDataBuffer = ComputeHelper.CreateStructuredBuffer<ParticleEntropyData>(particleCount);
        gravityForceBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        gravityCorrectionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(1);
        //OctreeBuffer = ComputeHelper.CreateStructuredBuffer<OctreeNode>(octreeManager.NumOfNodes);
        //SpatialHashes = ComputeHelper.CreateStructuredBuffer<uint>(octreeManager.NumBottomLayerNodes * (int)octreeManager.NumOfHashesPerLeafNode);
        debugBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        ResultantForceBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        deltaTimeBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        globalDeltaTimeBuffer = ComputeHelper.CreateStructuredBuffer<float>(1);
        SpatialDataBuffer = ComputeHelper.CreateStructuredBuffer<SpatialData>(particleCount);
        SpatialOffsetsBuffer = ComputeHelper.CreateStructuredBuffer<uint3>(particleCount);
        renderBuffer1 = ComputeHelper.CreateStructuredBuffer<float4>(particleCount);
        renderBuffer2 = ComputeHelper.CreateStructuredBuffer<float4>(particleCount);
        mortonKeyBuffer = ComputeHelper.CreateStructuredBuffer<uint2>(particleCount);
        newOctreeBuffer = ComputeHelper.CreateStructuredBuffer<NewOctreeNode>((particleCount * 2)-1); // has 2N-1 nodes. 0 to N-2 are internal nodes, N-1 to 2N-2 are leaf nodes
        SetInitialBufferData(spawnData);

        // tell the computer shader which kernels have access to which buffers
        ComputeHelper.SetBuffer(compute, particleBuffer, "ParticleBuffer", UpdatePredictionsKernel, updatePositionKernel, smoothingRadiusKernel, correctionTermsKernel, initialiseEntropyKernel, fusionKernel, neighbourDependentPropertiesKernel, renderKernel);
        ComputeHelper.SetBuffer(compute, positionBuffer, "PositionBuffer", UpdatePredictionsKernel, gridHashKernel, gravityKernel, smoothingRadiusKernel, correctionTermsKernel, deltaTimeKernel, neighbourDependentPropertiesKernel, renderKernel);
        ComputeHelper.SetBuffer(compute, entropyDataBuffer, "EntropyDataBuffer", correctionTermsKernel, deltaTimeKernel, fusionKernel, neighbourDependentPropertiesKernel);
        ComputeHelper.SetBuffer(compute, gravityForceBuffer, "GravityForceBuffer", gravityKernel);
        ComputeHelper.SetBuffer(compute, gravityCorrectionBuffer, "GravityCorrectionBuffer", updatePositionKernel);
        //ComputeHelper.SetBuffer(compute, OctreeBuffer, "Octree", gravityKernel);
        //ComputeHelper.SetBuffer(compute, SpatialHashes, "SpatialHashes", gravityKernel);
        ComputeHelper.SetBuffer(compute, debugBuffer, "DebugBuffer", gravityKernel);
        ComputeHelper.SetBuffer(compute, deltaTimeBuffer, "DeltaTimeBuffer", deltaTimeKernel);
        ComputeHelper.SetBuffer(compute, globalDeltaTimeBuffer, "GlobalDeltaTimeBuffer", deltaTimeKernel, fusionKernel, updatePositionKernel, UpdatePredictionsKernel, correctionTermsKernel, neighbourDependentPropertiesKernel);
        ComputeHelper.SetBuffer(compute, ResultantForceBuffer, "ResultantForces", updatePositionKernel, gravityKernel, UpdatePredictionsKernel, gravityKernel, neighbourDependentPropertiesKernel);
        ComputeHelper.SetBuffer(compute, SpatialDataBuffer, "SpatialDataBuffer", gridHashKernel, gravityKernel, updatePositionKernel, smoothingRadiusKernel, correctionTermsKernel, neighbourDependentPropertiesKernel);
        ComputeHelper.SetBuffer(compute, SpatialOffsetsBuffer, "SpatialOffsetDataBuffer", gridHashKernel, gravityKernel, updatePositionKernel, smoothingRadiusKernel, correctionTermsKernel, neighbourDependentPropertiesKernel);
        ComputeHelper.SetBuffer(compute, newOctreeBuffer, "NewOctreeBuffer", gravityKernel);
        ComputeHelper.SetBuffer(compute, renderBuffer1, "RenderBuffer", renderKernel);
        compute.SetInt("numParticles", particleCount);

        sorter = new();
        sorter.SetBuffers(SpatialDataBuffer, SpatialOffsetsBuffer);

        timestepManager = new();
        timestepManager.SetBuffers(deltaTimeBuffer, globalDeltaTimeBuffer, particleCount);

        gravityReductionManager = new();
        gravityReductionManager.SetBuffers(gravityForceBuffer, gravityCorrectionBuffer, particleCount);

        octreeReductionManager = new();
        octreeReductionManager.SetBuffers(newOctreeBuffer, positionBuffer, mortonKeyBuffer, particleCount, particleMass);

        //octreeManager.SetBuffers(OctreeBuffer, SpatialHashes, spatialStage1Size, SpatialDataBuffer, SpatialOffsetsBuffer, particleCount, positionBuffer, particleMass);

        InitialiseParticleProperties();

        display.Init(this);
    }

    // Runs the simulation frame by frame
    private void Update()
    {
        HandleInput();

        // run the simulation step
        RunSimulationFrame();

        // if data collection is enabled, sample the frame rate at set intervals
        if (totalTimeElapsed < TotalSampleTime && enableDataCollection)
        {
            SampleFrameRate(Time.deltaTime);
        }
        else if (enableDataCollection && totalTimeElapsed >= TotalSampleTime)
        {
            enableDataCollection = false;
            canWriteLog = true;
            Debug.Log("Finished Data Collection");
        }
    }

    // updates the compute settings and runs the simulation step. Flags an event to say the simulation step has finished
    void RunSimulationFrame()
    {
        if (isPaused) return;
        if (Time.frameCount < 10) return; // skip first few frames to avoid a disproportionate delta time

        //UpdateComputeSettings();
        RunSimulationStep();
        //ShowDebugData();
    }

    // NOTE: In the gravity and deltaTime reduction steps, those functions read buffer data back to the CPU which is whats taking the longest amount of time when running this (time on the CPU, not including GPU)
    // Dispatches the compute shader to run the simulation step
    void RunSimulationStep()
    {
        // PREDICTED POSITIONS
        ComputeHelper.Dispatch(compute, particleCount, UpdatePredictionsKernel);

        // SPATIAL HASHING AND SORTING
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel); // reset the spatial hash buffers
        sorter.SortAndCalcOffsets(); // sort the indices and calculate the offsets

        // SMOOTHING RADIUS AND DENSITY
        // calculates the smoothing radius and density
        ComputeHelper.Dispatch(compute, particleCount, smoothingRadiusKernel);

        // CORRECTIONS
        // calculates the balsara factor and pressure correction terms
        ComputeHelper.Dispatch(compute, particleCount, correctionTermsKernel);

        // ADAPTIVE DELTA TIME
        ComputeHelper.Dispatch(compute, particleCount, deltaTimeKernel);
        timestepManager.PerformReduction();

        // FUSION
        ComputeHelper.Dispatch(compute, particleCount, fusionKernel);

        // NEIGHBOUR DPENDENT PROPERTIES
        // updates the entropy and calcualtes the visocity force, pressure force and XSPH correction
        ComputeHelper.Dispatch(compute, particleCount, neighbourDependentPropertiesKernel);

        // GRAVITY AND OCTREE

        Profiler.BeginSample("Octree construction");
        octreeReductionManager.ConstructOctree();
        Profiler.EndSample();

        /*
        Profiler.BeginSample("Octree update");
        octreeManager.UpdateOctree(); // update the octree mass values
        Profiler.EndSample();
        */

        Profiler.BeginSample("Gravity kernel");
        ComputeHelper.Dispatch(compute, particleCount, gravityKernel); // apply gravity using the octree
        Profiler.EndSample();
        gravityReductionManager.PerformReduction(); // perform the reduction to get the gravity force correction value for this frame

        // APPLY VELOCITY
        ComputeHelper.Dispatch(compute, particleCount, updatePositionKernel);

        // RENDERING
        // call the render kernel to write the next frames render data then call to display the current frames render data
        // the render / write buffers are then swapped (prevents memory stalls from reading and writing to the same buffer at the same time)
        ComputeHelper.Dispatch(compute, particleCount, renderKernel);
        display.RenderParticles();
        ComputeHelper.SetBuffer(compute, (useRender1)? renderBuffer1 : renderBuffer2, "RenderBuffer", renderKernel);
        display.SwapRenderBuffer(this, useRender1);
        useRender1 = !useRender1;
    }

    // Updates the compute settings for the simulation that are intended to be changeable during runtime
    void UpdateComputeSettings()
    {
        compute.SetFloat("CFL", CFLScale);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("particleMass", particleMass);
        compute.SetFloat("BarnesHutTheta", barnesHutAccuracyThreshold);
        compute.SetFloat("softeningLength", softeningLength);
        //compute.SetVector("boundSize", new Vector3(octreeManager.BoundSize, octreeManager.BoundSize, octreeManager.BoundSize));
        compute.SetFloat("damping", damping);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("viscocityMultiplier", viscocityMultiplier);
        compute.SetFloat("adiabaticIndex", adiabaticIndex);

        compute.SetFloat("initialTemperature", InitialTemperature);
        compute.SetFloat("protonMass", ProtonMass);
        compute.SetFloat("boltzmannConstant", BoltzmannConstant);
        compute.SetFloat("coolingLambda", coolingLambda);
        compute.SetFloat("coolingAlpha", coolingAlpha);
        compute.SetInt("subcycles", coolingSubcycles);
        compute.SetFloat("viscAlphaMax", alphaMax);
        compute.SetFloat("viscAlphaMin", alphaMin);
        compute.SetFloat("viscEpsilon", viscosityEpsilon);
        compute.SetFloat("conductionCoefficient", conductionCoefficient);

        compute.SetFloat("fusionTempThreshold", temperatureThreshold);
        compute.SetFloat("fusionDensityThreshold", densityThreshold);
        compute.SetFloat("fusionRateCoefficient", rateCoefficient);
        compute.SetFloat("energyPerUnitMass", energyPerUnitMass);

        compute.SetFloat("stage1Size", spatialStage1Size);
        compute.SetFloat("stage2Size", spatialStage2Size);
        compute.SetFloat("stage3Size", spatialStage3Size);
        compute.SetFloat("minSizeFactor", minFactor);

        compute.SetInt("renderMode", (int)display.displayMode);

        compute.SetFloat("sigma", 1 / Mathf.PI);
        compute.SetFloat("C2Const", 21.0f/ (16.0f * Mathf.PI));
    }

    // Sets the initial buffer data for the simulation
    void SetInitialBufferData(NebulaParticleSpawner.ParticleSpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.positions.Length]; // This prevents the modification of the inital spawn data
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        ParticleData[] allParticles = new ParticleData[particleCount];
        ParticleEntropyData[] allEntropyData = new ParticleEntropyData[particleCount];
        float4[] allPositions = new float4[particleCount];
        float3[] debugData = new float3[allParticles.Length];

        for (int i = 0; i < allParticles.Length; i++)
        {
            debugData[i] = new float3(0.0f, 0.0f, 0.0f);

            ParticleData particle = new ParticleData
            {
                position = spawnData.positions[i],
                velocity = new float3(0.0f, 0.0f, 0.0f),// spawnData.velocities[i],
                entropy = 0.0f,
                density = 0.0f,
                pressureCorrection = 0.0f,
                balsaraFactor = 1.0f,
                temperature = InitialTemperature,
                hydroWeight = 1.0f,
                meanMolecularWeight = 0.5f // assume pure hydrogen initially
            };
            allParticles[i] = particle;

            ParticleEntropyData entropyData = new ParticleEntropyData
            {
                alpha = 2.0f,
                soundSpeed = 0.0f,
                divV = 0.0f,
                fusionEnergyRate = 0.0f
            };
            allEntropyData[i] = entropyData;

            float4 pos = new float4(spawnData.positions[i].x, spawnData.positions[i].y, spawnData.positions[i].z, spatialStage3Size);
            allPositions[i] = pos;
        }


        particleBuffer.SetData(allParticles);
        entropyDataBuffer.SetData(allEntropyData);
        positionBuffer.SetData(allPositions);
        debugBuffer.SetData(debugData);

        // temporary to test delta time buffer
        float[] deltaT = new float[1] { 0.007f };
        globalDeltaTimeBuffer.SetData(deltaT);

        float3[] gravityCorrectionData = new float3[] { new float3(0.0f, 0.0f, 0.0f) };
        gravityCorrectionBuffer.SetData(gravityCorrectionData);
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

    void ShowDebugData()
    {
        float3[] debugData = new float3[particleCount];
        debugBuffer.GetData(debugData);

        float average = 0.0f;
        float max = float.MinValue;
        float min = float.MaxValue;
        for (int i = 0; i < debugData.Length; i++)
        {
            average += debugData[i].x;
            if (debugData[i].x > max) max = debugData[i].x;
            if (debugData[i].x < min) min = debugData[i].x;
        }
        average /= particleCount;
        Debug.Log($"average: {average}, maximum: {max}, minumum: {min}");
    }

    private void OnApplicationQuit()
    {
        if (!canWriteLog) return;

        string path = Path.Combine("C:/My_Storage/Python_Stuff/FluidSimDataLogs/TextData", logFileName);
        if (File.Exists(path)) { Debug.Log("Log attempted for file name that already exists"); return; }
        File.WriteAllLines(path, logData);
        Debug.Log($"Logged Frame Data to path: {path}");
    }

    // runs the first few steps of the simulation to initialise particle properties like density and entropy
    private void InitialiseParticleProperties()
    {
        UpdateComputeSettings();
        ComputeHelper.Dispatch(compute, particleCount, UpdatePredictionsKernel);
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel);
        sorter.SortAndCalcOffsets();
        ComputeHelper.Dispatch(compute, particleCount, smoothingRadiusKernel);
        ComputeHelper.Dispatch(compute, particleCount, initialiseEntropyKernel); // only run here to initialse entropy based on initial temperature and density
        
        // write the initial render data then swap the render buffer
        ComputeHelper.Dispatch(compute, particleCount, renderKernel);
        ComputeHelper.SetBuffer(compute, renderBuffer2, "RenderBuffer", renderKernel);
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (isPaused) 
            {
                UpdateComputeSettings();
                RunSimulationStep();
            }
        }
    }

    // Clears the memory of all the buffers from the compute shader when the program is closed
    private void OnDestroy()
    {
        ComputeHelper.Release(particleBuffer, //OctreeBuffer, SpatialHashes, 
                              debugBuffer, 
                              ResultantForceBuffer, SpatialDataBuffer, SpatialOffsetsBuffer, 
                              entropyDataBuffer, gravityForceBuffer, gravityCorrectionBuffer,
                              globalDeltaTimeBuffer, renderBuffer1, renderBuffer2, positionBuffer,
                              newOctreeBuffer, mortonKeyBuffer);
        
        timestepManager.ReleaseBuffers();
        gravityReductionManager.ReleaseBuffers();
        octreeReductionManager.ReleaseBuffers();
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

public struct  ParticleData
{
    public Vector3 position;
    public Vector3 velocity;
    public float density;
    public float entropy;
    public float pressureCorrection;
    public float balsaraFactor;
    public float temperature;
    public float hydroWeight;
    public float meanMolecularWeight;
}

public struct ParticleEntropyData
{
    public float alpha;
    public float soundSpeed;
    public float divV;
    public float fusionEnergyRate;
}