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

            float3 colour; // check if this can be removed
            float4x4 localToWorld;

            struct v2f
            {
                float4 pos: POSITION;
                float2 uv : TEXCOORD0;
                float3 colour : TEXCOORD1;
                float3 normal : NORMAL;
            };

            v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
            {
                float density = particles[instanceID].density;
                float densityT = saturate((density - densityRange.x) / (densityRange.y - densityRange.x));
                float colT = densityT;

                if (displayType == 1)
                {
                    float velocity = length(particles[instanceID].velocity);
                    float velocityT = saturate((velocity - velocityRange.x) / (velocityRange.y - velocityRange.x));
                    colT = velocityT;
                }
                else if (displayType == 2)
                {
                    float energy = particles[instanceID].internelEnergy;
                    float energyT = saturate((energy - energyRange.x) / (energyRange.y - energyRange.x));
                    colT = energyT;
                }
                else if (displayType == 3)
                {
                    float temperature = particles[instanceID].temperature;
                    float tempT = saturate((temperature - tempRange.x) / (tempRange.y - tempRange.x));
                    colT = tempT;
                }
                else if (displayType == 4)
                {
                    float smoothing = particles[instanceID].smoothingRadius;
                    float tempT = saturate((smoothing - smoothingRange.x) / (smoothingRange.y - smoothingRange.x));
                    colT = tempT;
                }
                else if (displayType == 5)
                {
                    float hydro = particles[instanceID].hydroWeight;
                    float tempT = saturate((hydro - hydroRange.x) / (hydroRange.y - hydroRange.x));
                    colT = tempT;
                }

                float3 centreWorld = particles[instanceID].position;
                float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
                float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

                v2f o;
                o.pos = UnityObjectToClipPos(objectVertPos);
                o.uv = v.texcoord;
                o.normal = v.normal;
                o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float shade = saturate(dot(_WorldSpaceLightPos0.xyz, i.normal));
                shade = (shade + 0.6) / 1.4;
                return float4(i.colour * shade, 1);
            }
            ENDCG
        }
    }
}
