Shader "Hidden/Particle3D"
{
    Properties
    {
        _Colour("Colour", Color) = (0,0,1,1)
        _Radius ("Radius", Float) = 0.5
        _MainTex("Base (RGB)", 2D) = "White" {}
    }
    SubShader
    {
        Tags { "Queue"="Geometry" }

        Pass
        {
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct ParticleData
            {
                float3 position;
                float3 predictedPos;
                float3 velocity;
                float density;
                float smoothingRadius;
                float internelEnergy;
                float pressureCorrection;
                float balsaraFactor;
                float temperature;
                float hydroWeight;
                float meanMolecularWeight;
            };

            StructuredBuffer<ParticleData> particles;

            Texture2D<float4> ColourMap;
            SamplerState linear_clamp_sampler;
            
            float scale;
            float2 densityRange;
            float2 velocityRange;
            float2 energyRange;
            float2 tempRange;
            float2 smoothingRange;
            float2 hydroRange;
            int displayType;

            struct v2f
            {
                float4 pos: SV_POSITION;
                float colour : TEXCOORD1;
            };

            v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
            {
                float density = particles[instanceID].density;
                float densityT = saturate((density - densityRange.x) * densityRange.y);
                float colT = densityT;

                if (displayType == 1)
                {
                    float velocity = length(particles[instanceID].velocity);
                    float velocityT = saturate((velocity - velocityRange.x) * velocityRange.y);
                    colT = velocityT;
                }
                else if (displayType == 2)
                {
                    float energy = particles[instanceID].internelEnergy;
                    float energyT = saturate((energy - energyRange.x) * energyRange.y);
                    colT = energyT;
                }
                else if (displayType == 3)
                {
                    float temperature = particles[instanceID].temperature;
                    float tempT = saturate((temperature - tempRange.x) * tempRange.y);
                    colT = tempT;
                }
                else if (displayType == 4)
                {
                    float smoothing = particles[instanceID].smoothingRadius;
                    float tempT = saturate((smoothing - smoothingRange.x) * smoothingRange.y);
                    colT = tempT;
                }
                else if (displayType == 5)
                {
                    float hydro = particles[instanceID].hydroWeight;
                    float tempT = saturate((hydro - hydroRange.x) * hydroRange.y);
                    colT = tempT;
                }

                float3 centreWorld = particles[instanceID].position;

                float3 camRight = UNITY_MATRIX_I_V._m00_m01_m02;
                float3 camUp = UNITY_MATRIX_I_V._m10_m11_m12;
                float3 worldOffset = (camRight * v.vertex.x + camUp * v.vertex.y) * scale;
                float3 worldVertPoss = centreWorld + worldOffset;
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(worldVertPoss, 1));

                v2f o;
                o.pos = clipPos;
                o.colour = colT;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(i.colour, 0.5), 0);
                return float4(colour, 1);
            }
            ENDCG
        }
    }
}
