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

            StructuredBuffer<float3> positions;
            StructuredBuffer<float3> velocities;
            StructuredBuffer<float2> densities;
            StructuredBuffer<float> energies;

            Texture2D<float4> ColourMap;
            SamplerState linear_clamp_sampler;
            
            float scale;
            float densityMin;
            float densityMax;
            float velocityMin;
            float velocityMax;
            float energyMin;
            float energyMax;
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
                float density = densities[instanceID].x;
                float densityT = saturate((density - densityMin) / (densityMax - densityMin));
                float colT = densityT;

                if (displayType == 1)
                {
                    float velocity = length(velocities[instanceID]);
                    float velocityT = saturate((velocity - velocityMin) / (velocityMax - velocityMin));
                    colT = velocityT;
                }
                else if (displayType == 2)
                {
                    float energy = energies[instanceID];
                    float energyT = saturate((energy - energyMin) / (energyMax - energyMin));
                    colT = energyT;
                }

                float3 centreWorld = positions[instanceID];
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
